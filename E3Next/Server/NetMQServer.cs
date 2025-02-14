﻿using E3Core.Processors;

using MonoCore;

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace E3Core.Server
{
    /// <summary>
    /// This publishes out data to the UI client, but could be for anything that wants to pub/sub
    /// </summary>
    public static class NetMQServer
    {

        static PubServer _pubServer;
        static RouterServer _routerServer;
        static PubClient _pubClient;
        public static SharedDataClient SharedDataClient;

        public static Int32 RouterPort;
        public static Int32 PubPort;
        public static Int32 PubClientPort;
        public static Process UIProcess;
        public static Process DiscordProcess;
        private static IMQ MQ = E3.MQ;


        public static void Init()
        {
            SharedDataClient = new SharedDataClient();

            RouterPort = FreeTcpPort();
            PubPort = FreeTcpPort();
            PubClientPort = FreeTcpPort();



            if (Debugger.IsAttached)
            {
                PubPort = 51711;
                RouterPort = 51712;
                PubClientPort = 51713;
            }
            _pubServer = new PubServer();
            _routerServer = new RouterServer();
            _pubClient = new PubClient();

            _pubServer.Start(PubPort);
            _routerServer.Start(RouterPort);
            _pubClient.Start(PubClientPort);


            EventProcessor.RegisterUnfilteredEventMethod("E3UI", (x) =>
            {

                if (x.typeOfEvent == EventProcessor.eventType.EQEvent)
                {
                    PubServer.IncomingChatMessages.Enqueue(x.eventString);
                }
                else if (x.typeOfEvent == EventProcessor.eventType.MQEvent)
                {
                    PubServer.MQChatMessages.Enqueue(x.eventString);
                }

            });
            EventProcessor.RegisterCommand("/ui", (x) =>
            {
                MQ.Write("/ui has been depreciated, please use /e3ui");
            });
            EventProcessor.RegisterCommand("/e3ui", (x) =>
            {
                ToggleUI();
            });
            EventProcessor.RegisterCommand("/e3discord", (x) =>
            {
                ToggleDiscordBot();
            });
            EventProcessor.RegisterCommand("/e3ui-debug", (x) =>
            {
                Int32 processID = System.Diagnostics.Process.GetCurrentProcess().Id;
                var path = $"{Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", "").Replace("/", "\\").Replace("e3.dll", "")}E3NextUI.exe";
                MQ.Write($"{path} {PubPort} {RouterPort} {PubClientPort} {processID}");
            });
            EventProcessor.RegisterCommand("/e3ui-kill", (x) =>
            {
                if (UIProcess != null)
                {
                    UIProcess.Kill();
                    UIProcess = null;
                }
            });
        }
        /// <summary>
        /// Turns on the UI program, and then from then on, hide/shows it as needed. To close restart e3.
        /// </summary>
        static void ToggleUI()
        {
            string dllFullPath = Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", "").Replace("/", "\\").Replace("e3.dll", "");
#if DEBUG
            dllFullPath = "C:\\Code\\E3next\\E3Next\\bin\\Debug\\";
#endif
            if (UIProcess == null)
            {
                Int32 processID = System.Diagnostics.Process.GetCurrentProcess().Id;
                MQ.Write("Trying to start:" + dllFullPath + @"E3NextUI.exe");
                UIProcess = System.Diagnostics.Process.Start(dllFullPath + @"E3NextUI.exe", $"{PubPort} {RouterPort} {PubClientPort} {processID}");
            }
            else
            {
                //we have a process, is it up?
                if (UIProcess.HasExited)
                {
                    Int32 processID = System.Diagnostics.Process.GetCurrentProcess().Id;
                    //start up a new one.
                    MQ.Write("Trying to start:" + dllFullPath + @"E3NextUI.exe");
                    UIProcess = System.Diagnostics.Process.Start(dllFullPath + @"E3NextUI.exe", $"{PubPort} {RouterPort} {PubClientPort} {processID}");
                }
                else
                {
                    PubServer.CommandsToSend.Enqueue("#toggleshow");

                }
            }
        }

        static void ToggleDiscordBot()
        {
            var dllFullPath = Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", "").Replace("/", "\\").Replace("e3.dll", "");
#if DEBUG
            dllFullPath = "C:\\Code\\E3next\\E3Next\\bin\\Debug\\";
#endif
            var processName = $"{dllFullPath}E3Discord.exe";
            if (DiscordProcess == null)
            {
                var existingDiscordProcess = Process.GetProcessesByName("E3Discord.exe");
                if (existingDiscordProcess.Any())
                {
                    MQ.Write("\agAnother E3Discord is already runnning. Not starting another one");
                    return;
                }
                Int32 processID = System.Diagnostics.Process.GetCurrentProcess().Id;
                MQ.Write("\ayTrying to start:" + processName);
                var discordMyUserId = string.IsNullOrEmpty(E3.GeneralSettings.DiscordMyUserId) ? string.Empty : E3.GeneralSettings.DiscordMyUserId;
                var commandLineArgs = $"{PubPort} {RouterPort} {PubClientPort} {E3.GeneralSettings.DiscordBotToken} " +
                    $"{E3.GeneralSettings.DiscordGuildChannelId} {E3.GeneralSettings.DiscordServerId} {processID} {E3.GeneralSettings.DiscordMyUserId}";
                DiscordProcess = System.Diagnostics.Process.Start(dllFullPath + "E3Discord.exe", commandLineArgs);
                MQ.Write($"\agStarted {processName}");
            }
            else
            {
                MQ.Write($"\ayKilling {processName}");
                if (!DiscordProcess.HasExited)
                    DiscordProcess.Kill();

                DiscordProcess = null;
                MQ.Write("\agIt's dead Jim");
            }
        }

        /// <summary>
        /// best way to find a free open port that i can figure out
        /// windows won't reuse the port for a bit, so safe to open/close -> reuse.
        /// </summary>
        /// <returns></returns>
        static int FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
