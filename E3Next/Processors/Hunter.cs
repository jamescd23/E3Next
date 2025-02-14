﻿using E3Core.Data;
using E3Core.Settings;
using E3Core.Utility;

using IniParser;
using IniParser.Model;

using MonoCore;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace E3Core.Processors
{
    public class HuntingProfile : BaseSettings, IBaseSettings
    {
        private string _zoneName;
        private string _profileName;

        private bool _whiteListEnabled = true;
        private readonly List<string> _whiteList = new List<string>();

        private bool _blackListEnabled = false;
        private readonly List<string> _blackList = new List<string>();

        public static HuntingProfile LoadProfile(Zone currentZone, string profileName)
        {
            if (string.IsNullOrEmpty(profileName))
                profileName = "Default";

            var profile = new HuntingProfile();
            profile._zoneName = currentZone.ShortName;
            profile._profileName = profileName;
            profile.LoadData();
            return profile;
        }

        private void LoadData()
        {
            string filename = GetSettingsFilePath($"Hunt_{_zoneName}_{_profileName}.ini");
            var parsedData = CreateSettings(filename);

            LoadKeyData("WhiteList", "WhiteListEnabled", parsedData, ref _whiteListEnabled);
            LoadKeyData("WhiteList", "WhiteListRegex", parsedData, _whiteList);

            LoadKeyData("BlackList", "BlackListEnabled", parsedData, ref _blackListEnabled);
            LoadKeyData("BlackList", "BlackListRegex", parsedData, _blackList);
        }

        public bool IsValid()
        {
            if (_whiteListEnabled && _blackListEnabled)
            {
                MQ.Write("Both whitelist and blacklist cannot be enabled or disabled");
                return false;
            }

            if (_whiteListEnabled && _whiteList.Count == 0)
            {
                MQ.Write("Whitelist is enabled, but has no entries");
                return false;
            }

            return true;
        }

        public IniData CreateSettings(string fileName)
        {
            FileIniDataParser parser = e3util.CreateIniParser();
            IniData newFile = new IniData();

            newFile.Sections.AddSection("WhiteList");
            var wlSection = newFile.Sections.GetSectionData("WhiteList");
            wlSection.Keys.AddKey("WhiteListEnabled", "On");

            newFile.Sections.AddSection("BlackList");
            var blSection = newFile.Sections.GetSectionData("BlackList");
            blSection.Keys.AddKey("BlackListEnabled", "Off");

            if (!File.Exists(fileName))
            {
                if (!Directory.Exists(_configFolder + _botFolder))
                {
                    Directory.CreateDirectory(_configFolder + _botFolder);
                }
                //file straight up doesn't exist, lets create it
                parser.WriteFile(fileName, newFile);
                _fileLastModified = File.GetLastWriteTime(fileName);
                _fileLastModifiedFileName = fileName;
            }
            else
            {
                //File already exists, may need to merge in new settings lets check
                //Parse the ini file
                //Create an instance of a ini file parser
                FileIniDataParser fileIniData = e3util.CreateIniParser();
                IniData tParsedData = fileIniData.ReadFile(fileName);

                //overwrite newfile with what was already there
                tParsedData.Merge(newFile);
                newFile = tParsedData;
                //save it it out now
                File.Delete(fileName);
                parser.WriteFile(fileName, tParsedData);

                _fileLastModified = File.GetLastWriteTime(fileName);
                _fileLastModifiedFileName = fileName;
            }

            return newFile;
        }

        public bool Matches(Spawn s)
        {
            if (_whiteListEnabled)
            {
                foreach (var entry in _whiteList)
                {
                    bool r = Regex.IsMatch(s.CleanName, entry);
                    if (r) return true;
                }
                return false;
            }
            else  /* if (_blackListEnabled) */
            {
                foreach (var entry in _blackList)
                {
                    bool r = Regex.IsMatch(s.CleanName, entry);
                    if (r) return false;
                }
                return true;
            }
        }
    }

    public static class Hunter
    {
        public static Logging _log = E3.Log;

        private static IMQ MQ = E3.MQ;
        private static ISpawns _spawns = E3.Spawns;

        private static HuntingProfile _Profile;

        private static int _ActiveTarget = 0;

        private static DateTime _NextAction = DateTime.MinValue;

        private enum State
        {
            Disabled = 0,
            Acquiring = 1,
            Navigating = 2,
            Murder = 3,
            Looting = 4
        }

        private static State _CurrentStateV = State.Disabled;

        private static Random _Rand = new Random();

        private static State CurrentState
        {
            get => _CurrentStateV;
            set
            {
                _CurrentStateV = value;
                int rSec = _Rand.Next(2, 10);
                _NextAction = DateTime.Now.AddSeconds(rSec);
                MQ.Write($"State is now {value}; acting in {rSec}s");
            }
        }

        private const int navFuzzyDistance = 30;

        [SubSystemInit]
        public static void SystemInit()
        {
            EventProcessor.RegisterCommand("/hunt", (x) =>
            {
                ClearXTargets.FaceTarget = true;
                ClearXTargets.StickTarget = false;

                bool doLoad = false;
                string profileName = null;

                if (x.args.Count == 0)
                {
                    doLoad = true;
                    // Name = null, let load specify;
                }
                else if (x.args.Count == 1 && x.args[0] == "off")
                {
                    Reset();
                }
                else if (x.args.Count == 1)
                {
                    doLoad = true;
                    profileName = x.args[0];
                }

                if (doLoad)
                {
                    MQ.Write($"Hunter enabled with profile [{profileName}]");

                    var tmpProfile = HuntingProfile.LoadProfile(Zoning.CurrentZone, profileName);
                    if (tmpProfile.IsValid())
                    {
                        _Profile = tmpProfile;
                        CurrentState = State.Acquiring;
                    }
                }
            });
        }

        [ClassInvoke(Data.Class.All)]
        public static void Check_Hunter()
        {
            if (CurrentState == State.Disabled) return;

            if (DateTime.UtcNow < _NextAction) return;

            e3util.YieldToEQ();
            _spawns.RefreshList();

            switch (CurrentState)
            {
                case State.Acquiring: HandleStateAcquiring(); break;

                case State.Navigating: HandleStateNavigating(); break;

                case State.Murder: HandleStateMurder(); break;

                case State.Looting: HandleStateLooting(); break;
            }
        }

        public static void Reset()
        {
            MQ.Write("Hunter disabled");
            CurrentState = State.Disabled;
            _ActiveTarget = 0;
            _Profile = null;
            MQ.Cmd("/nav stop");
            MQ.Cmd("/stick off");
        }

        private static void HandleStateAcquiring()
        {
            _ActiveTarget = _spawns.Get()
                .Where(x => x.TypeDesc == "NPC")
                .Where(x => _Profile.Matches(x))
                .Where(x => MQ.Query<bool>($"${{Navigation.PathExists[id {x.ID} distance={navFuzzyDistance}]}}"))
                .OrderBy(x => x.Distance)
                //.OrderBy(x => MQ.Query<bool>($"${{Navigation.PathLength[id {x.ID} distance={navFuzzyDistance}]}}"))
                .Select(x => x.ID)
                .FirstOrDefault();

            if (_ActiveTarget > 0)
            {
                MQ.Cmd($"/target id {_ActiveTarget}");
                StartNavigation();
            }
        }

        private static void HandleStateNavigating()
        {
            if (!ActiveTargetExists())
            {
                CurrentState = State.Acquiring;
            }
            else
            {
                // TODO - Mob may be moving, so when velocity = 0 we need to stick then wait until within melee range. Thats a new state

                if (MQ.Query<bool>("${Navigation.Active}")) return; // still moving

                MQ.Cmd("/nav stop");

                MQ.Write("Sticking...");
                MQ.Cmd("/stick 5");
                MQ.Delay(1000);

                MQ.Write("Murder Time");

                CurrentState = State.Murder;
                _ = Casting.TrueTarget(_ActiveTarget);

                MQ.Cmd("/stand");
                MQ.Cmd("/keypress 1");
                MQ.Cmd("/attack");
                MQ.Cmd("/stick hold moveback 10");
            }
        }

        private static void HandleStateMurder()
        {
            if (!ActiveTargetExists())
            {
                MQ.Write("2");
                CurrentState = State.Acquiring;
            }

            bool sticking = MQ.Query<bool>("${Stick.Active}");
            if (!sticking) MQ.Cmd("/stick hold moveback 5");
            if (ActiveTargetDead()) CurrentState = State.Looting;
        }

        private static void HandleStateLooting()
        {
            MQ.Delay(100);
            Loot.LootArea(false);
            CurrentState = State.Acquiring;
        }

        private static bool ActiveTargetExists()
        {
            return _spawns.TryByID(_ActiveTarget, out var _);
        }

        private static bool ActiveTargetDead()
        {
            if (!_spawns.TryByID(_ActiveTarget, out Spawn s)) return true;
            if (s.TypeDesc == "Corpse") return true;
            return false;
        }

        private static void StartNavigation()
        {
            bool navPathExists = MQ.Query<bool>($"${{Navigation.PathExists[id {_ActiveTarget} distance={navFuzzyDistance}]}}");

            if (!navPathExists)
            {
                //early return if no path available
                MQ.Write($"\arNo nav path available to spawn ID: {_ActiveTarget}");
                _ActiveTarget = 0;
                CurrentState = State.Disabled;
                return;
            }

            CurrentState = State.Navigating;
            MQ.Cmd($"/nav id {_ActiveTarget} distance={navFuzzyDistance}");
        }
    }
}
