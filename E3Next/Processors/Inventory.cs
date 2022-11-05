﻿using E3Core.Data;
using E3Core.Settings;
using E3Core.Settings.FeatureSettings;
using E3Core.Utility;
using E3Core.Processors;
using IniParser;
using MonoCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace E3Core.Processors
{
    public static class Inventory
    {
        public static Logging _log = E3.Log;
        private static IMQ MQ = E3.Mq;
        private static ISpawns _spawns = E3.Spawns;
        private static readonly List<string> _invSlots = new List<string>() { "charm", "leftear", "head", "face", "rightear", "neck", "shoulder", "arms", "back", "leftwrist", "rightwrist", "ranged", "hands", "mainhand", "offhand", "leftfinger", "rightfinger", "chest", "legs", "feet", "waist", "powersource", "ammo" };
        private static readonly List<string> _fdsSlots = new List<string>(_invSlots) { "fingers", "wrists", "ears" };

        [SubSystemInit]
        public static void Init()
        {
            RegisterEvents();
        }
        private static bool FDSPrint(string slot)
        {

            if (_fdsSlots.Contains(slot))
            {
                if (slot == "fingers")
                {
                    MQ.Cmd("/g Left:${InvSlot[leftfinger].Item.ItemLink[CLICKABLE]}   Right:${InvSlot[rightfinger].Item.ItemLink[CLICKABLE]} ");
                }
                else if (slot == "wrists")
                {
                    MQ.Cmd("/g Left:${InvSlot[leftwrist].Item.ItemLink[CLICKABLE]}   Right:${InvSlot[rightwrist].Item.ItemLink[CLICKABLE]} ");

                }
                else if (slot == "ears")
                {
                    MQ.Cmd("/g Left:${InvSlot[leftear].Item.ItemLink[CLICKABLE]}   Right:${InvSlot[rightear].Item.ItemLink[CLICKABLE]} ");
                }
                else
                {
                    MQ.Cmd($"/g {slot}:${{InvSlot[{slot}].Item.ItemLink[CLICKABLE]}}");

                }
                return true;
            }
            else
            {
                E3.Bots.Broadcast("Cannot find slot. Valid slots are:" + String.Join(",", _fdsSlots));
                return false;
            }
        }
        private static void FindItemCompact(string itemName)
        {

            bool weHaveItem = MQ.Query<bool>($"${{FindItemCount[={itemName}]}}");
            bool weHaveItemInBank = MQ.Query<bool>($"${{FindItemBankCount[={itemName}]}}");
            Int32 totalItems = 0;

            List<string> report = new List<string>();


            //search equiped items
            for (int i = 0; i <= 22; i++)
            {
                string name = MQ.Query<string>($"${{InvSlot[{i}].Item}}");

                if (MQ.Query<bool>($"${{InvSlot[{i}].Item.Name.Find[{itemName}]}}"))
                {
                    Int32 stackCount = MQ.Query<Int32>($"${{InvSlot[{i}].Item.Stack}}");
                    totalItems += stackCount;
                    report.Add($"\ag[Worn] \ap{name}\aw ({stackCount})");
                }
                Int32 augCount = MQ.Query<Int32>($"${{InvSlot[{i}].Item.Augs}}");
                if (augCount > 0)
                {
                    for (int a = 1; a <= 6; a++)
                    {
                        string augname = MQ.Query<string>($"${{InvSlot[{i}].Item.AugSlot[{a}].Name}}");

                        if (augname.IndexOf(itemName, 0, StringComparison.OrdinalIgnoreCase) > -1)
                        {
                            totalItems += 1;
                            report.Add($"\ag[Worn] \ap{name}-\a-o{augname} \aw(aug-slot[{a}])");
                        }
                    }
                }

            }
            for (Int32 i = 1; i <= 10; i++)
            {
                bool SlotExists = MQ.Query<bool>($"${{Me.Inventory[pack{i}]}}");
                if (SlotExists)
                {
                    Int32 ContainerSlots = MQ.Query<Int32>($"${{Me.Inventory[pack{i}].Container}}");

                    if (ContainerSlots > 0)
                    {
                        for (Int32 e = 1; e <= ContainerSlots; e++)
                        {
                            //${Me.Inventory[${itemSlot}].Item[${j}].Name.Equal[${itemName}]}
                            String bagItem = MQ.Query<String>($"${{Me.Inventory[pack{i}].Item[{e}]}}");
                            Int32 stackCount = MQ.Query<Int32>($"${{Me.Inventory[pack{i}].Item[{e}].Stack}}");
                            if (bagItem.IndexOf(itemName, 0, StringComparison.OrdinalIgnoreCase) > -1)
                            {
                                report.Add($"\ag[Pack] \ap{bagItem}- \awbag({i}) slot({e}) count({stackCount})");
                            }
                        }
                    }
                }
            }

            for (int i = 1; i <= 26; i++)
            {
                string bankItemName = MQ.Query<string>($"${{Me.Bank[{i}].Name}}");
                if (bankItemName.IndexOf(itemName, 0, StringComparison.OrdinalIgnoreCase) > -1)
                {
                    Int32 bankStack = MQ.Query<Int32>($"${{Me.Bank[{i}].Stack}}");
                    report.Add($"\ag[Bank] \ap{bankItemName} \aw- slot({i}) count({bankStack})");
                }


                //look through container
                Int32 ContainerSlots = MQ.Query<Int32>($"${{Me.Bank[{i}].Container}}");
                for (int e = 1; e <= ContainerSlots; e++)
                {
                    bankItemName = MQ.Query<string>($"${{Me.Bank[{i}].Item[{e}].Name}}");

                    if (bankItemName.IndexOf(itemName, 0, StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        Int32 bankStack = MQ.Query<Int32>($"${{Me.Bank[{i}].Item[{e}].Stack}}");
                        report.Add($"\ag[Bank] \ap{bankItemName} \aw- slot({i}) bagslot({e}) count({bankStack})");
                    }
                    Int32 augCount = MQ.Query<Int32>($"${{Me.Bank[{i}].Item[{e}].Augs}}");
                    if (augCount > 0)
                    {
                        for (int a = 1; a <= 6; a++)
                        {
                            string augname = MQ.Query<string>($"${{Bank[{i}].Item[{e}].AugSlot[{a}].Name}}");

                            if (augname.IndexOf(itemName, 0, StringComparison.OrdinalIgnoreCase) > -1)
                            {
                                totalItems += 1;
                                report.Add($"\ag[Bank-Aug-Worn] \ap{bankItemName}-\ao{augname} slot({i}) bagslot({e}) (aug-slot[{a}])");
                            }

                        }
                    }
                }
            }

            foreach (var value in report)
            {
                E3.Bots.Broadcast(value);

            }
        }

        private static void GetFromBank(List<string> args)
        {
            if (args.Count == 0)
            {
                MQ.Write("\arYou need to tell me what to get!");
                return;
            }

            var item = args[0];
            if (!MQ.Query<bool>("${Window[BigBankWnd]}"))
            {
                MQ.Write("\arYou need to open the bank window before issuing this command");
                return;
            }

            if (!MQ.Query<bool>($"${{FindItemBank[={item}]}}"))
            {
                MQ.Write($"\arYou do not have any {item}s in the bank");
                return;
            }

            var slot = MQ.Query<int>($"${{FindItemBank[={item}].ItemSlot}}");
            var slot2 = MQ.Query<int>($"${{FindItemBank[={item}].ItemSlot2}}");

            // different syntax for if the item is in a bag vs if it's not
            if (slot2 >= 0)
            {
                MQ.Cmd($"/itemnotify bank{slot + 1} rightmouseup");
                MQ.Delay(100);
                MQ.Cmd($"/itemnotify in bank{slot + 1} {slot2 + 1} leftmouseup");
            }
            else
            {
                MQ.Cmd($"/itemnotify bank{slot + 1} leftmouseup");
            }

            MQ.Delay(250);

            if (args.Count() > 1)
            {
                var myQuantity = MQ.Query<int>($"${{FindItemBank[={item}].Stack}}");
                if (!int.TryParse(args[1], out var requestedQuantity))
                {
                    MQ.Write($"\arYou requested a quantity of {args[1]}, and that's not a number. Grabbing all {item}s");
                }
                else if (requestedQuantity > myQuantity)
                {
                    MQ.Write($"\arYou requested {requestedQuantity} {item}s and you only have {myQuantity}. Grabbing all {item}s");
                }
                else
                {
                    MQ.Cmd($"/notify QuantityWnd QTYW_slider newvalue {requestedQuantity}");
                    MQ.Delay(250);
                }
            }

            MQ.Cmd("/notify QuantityWnd QTYW_Accept_Button leftmouseup");
            MQ.Delay(50);
            MQ.Cmd($"/itemnotify bank{slot + 1} rightmouseup");
        }

        private static void Upgrade(List<string> args)
        {
            if (args.Count < 2)
            {
                MQ.Write("\arYou must provide the slot name and new item name to run this command");
                return;
            }

            var slotName = args[0];
            var newItem = args[1];

            if (!_invSlots.Contains(slotName))
            {
                MQ.Write($"\arInvalid slot name of {slotName}. The options are {string.Join(", ", _invSlots)}");
                return;
            }

            if (!MQ.Query<bool>($"${{FindItem[={newItem}]}}"))
            {
                MQ.Write($"\arYou do not have {newItem} in your inventory");
                return;
            }

            var distiller = "Perfected Augmentation Distiller";
            var distillerCount = MQ.Query<int>($"${{FindItemCount[={distiller}]}}");
            var curItem = MQ.Query<string>($"${{Me.Inventory[{slotName}]}}");
            var slotsWithAugs = new Dictionary<int, string>();
            for (int i = 1; i <= 6; i++)
            {
                var augInSlot = MQ.Query<string>($"${{Me.Inventory[{slotName}].AugSlot[{i}]}}");
                if (!string.Equals(augInSlot, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    slotsWithAugs.Add(i, augInSlot);
                }
            }

            var freeInvSlots = MQ.Query<int>("${Me.FreeInventory}");
            if (distillerCount < slotsWithAugs.Count())
            {
                MQ.Write($"\arYou do not have enough {distiller}s in your inventory to de-aug {curItem}");
                return;
            }

            if(freeInvSlots < slotsWithAugs.Count())
            {
                MQ.Write("\arYou do not have enough free inventory space to hold removed augs");
                return;
            }

            MQ.Cmd($"/itemnotify \"${{Me.Inventory[{slotName}]}}\" rightmouseheld");

            foreach(var kvp in slotsWithAugs)
            {
                MQ.Cmd($"/notify ItemDisplayWindow IDW_Socket_Slot_{kvp.Key}_Item leftmouseup");
                MQ.Delay(500);
                e3util.ClickYesNo(true);
                MQ.Delay(3000, "${Cursor.ID}");
                e3util.ClearCursor();
            }

            MQ.Cmd("/keypress esc");
            MQ.Cmd($"/itemnotify \"${{FindItem[={newItem}]}}\" rightmouseheld");
            MQ.Delay(500);

            foreach (var kvp in slotsWithAugs)
            {
                if (!e3util.PickUpItemViaFindItemTlo(kvp.Value))
                {
                    // we know it's here since we just took it out of our last item
                    bool foundItem = false;
                    for (int i = 1; i <= 10; i++)
                    {
                        if (foundItem)
                        {
                            break;
                        }

                        // first check to see if the slot has our aug in it
                        var item = MQ.Query<string>($"${{Me.Inventory[pack{i}]}}");
                        if (string.Equals(item, kvp.Value))
                        {
                            MQ.Cmd($"/itemnotify pack{i} leftmouseup");
                            break;
                        }

                        // then check inside the container
                        var containerSlots = MQ.Query<int>($"${{Me.Inventory[pack{i}].Container}}");
                        for (int j = i; j <= containerSlots; j++)
                        {
                            item = MQ.Query<string>($"${{Me.Inventory[pack{i}].Item[{j}]}}");
                            if (string.Equals(item, kvp.Value))
                            {
                                MQ.Cmd($"/itemnotify in pack{i} {j} leftmouseup");
                                foundItem = true;
                                break;
                            }
                        }
                    }
                }

                MQ.Cmd($"/notify ItemDisplayWindow IDW_Socket_Slot_{kvp.Key}_Item leftmouseup");
                MQ.Delay(500);
                e3util.ClickYesNo(true);
                MQ.Delay(3000, "!${Cursor.ID}");
                MQ.Delay(500);
            }

            MQ.Delay(250);
            MQ.Cmd($"/exchange \"{newItem}\" {slotName}");
            MQ.Cmd("/keypress esc");
            MQ.Write("\agUpgrade complete!");
        }

        static void RegisterEvents()
        {
            EventProcessor.RegisterCommand("/fds", (x) =>
            {

                if (x.args.Count > 0)
                {
                    string slot = x.args[0];
                    if (FDSPrint(slot))
                    {
                        if (x.args.Count == 1)
                        {
                            E3.Bots.BroadcastCommandToGroup($"/fds {slot} group");
                        }

                    }
                }

            });
            EventProcessor.RegisterCommand("/fic", (x) =>
            {
                string itemName = x.args[0];
                if (x.args.Count == 1)
                {
                    E3.Bots.BroadcastCommandToGroup($"/fic \"{itemName}\" all", x);
                }

                if (!e3util.FilterMe(x))
                {
                    FindItemCompact(itemName);
                }

            });
            EventProcessor.RegisterCommand("/finditem", (x) =>
            {
                string itemName = x.args[0];
                if (x.args.Count == 1)
                {
                    E3.Bots.BroadcastCommandToGroup($"/finditem \"{itemName}\" all", x);
                }

                if (!e3util.FilterMe(x))
                {
                    FindItemCompact(itemName);
                }

            });
            EventProcessor.RegisterCommand("/getfrombank", (x) => GetFromBank(x.args));
            EventProcessor.RegisterCommand("/upgrade", (x) => Upgrade(x.args));
            //restock generic reusable items from vendors
            
        }
    }
}
