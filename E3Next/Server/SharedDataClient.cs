﻿using E3Core.Processors;

using MonoCore;

using NetMQ;
using NetMQ.Sockets;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace E3Core.Server
{

    public class ShareDataEntry
    {
        public String Data { get; set; }
        public Int64 LastUpdate
        {
            get; set;
        }
    }
    public class SharedDataClient
    {
        public ConcurrentDictionary<string, ConcurrentDictionary<string, ShareDataEntry>> TopicUpdates = new ConcurrentDictionary<string, ConcurrentDictionary<string, ShareDataEntry>>(StringComparer.OrdinalIgnoreCase);
        public ConcurrentQueue<OnCommandData> CommandQueue = new ConcurrentQueue<OnCommandData>();

        private static IMQ MQ = E3.MQ;


        Dictionary<string, Task> _processTasks = new Dictionary<string, Task>();
        static object _processLock = new object();
        public bool RegisterUser(string user, string path)
        {

            //fix situations where it doesn't end in a slash
            if (!path.EndsWith(@"\"))
            {
                path += @"\";
            }

            //see if they exist in the collection
            if (!TopicUpdates.ContainsKey(user))
            {
                lock (_processLock)
                {
                    if (!_processTasks.ContainsKey(user))
                    {
                        //lets see if the file exists
                        string filePath = $"{path}{user}_{E3.ServerName}_pubsubport.txt";

                        if (System.IO.File.Exists(filePath))
                        {
                            //lets load up the port information
                            TopicUpdates.TryAdd(user, new ConcurrentDictionary<string, ShareDataEntry>());
                            string data = System.IO.File.ReadAllText(filePath);
                            //its now port:ipaddress
                            string[] splitData = data.Split(new char[] { ',' });
                            string port = splitData[0];
                            string ipaddress = splitData[1];

                            var newTask = Task.Factory.StartNew(() => { Process(user, port, ipaddress, filePath); }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                            _processTasks.Add(user, newTask);
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        public void ProcessE3BCCommands()
        {
            if (EventProcessor.CommandList.ContainsKey("/e3bc") && EventProcessor.CommandList["/e3bc"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bc");
            }
            if (EventProcessor.CommandList.ContainsKey("/e3bcz") && EventProcessor.CommandList["/e3bcz"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bcz");
            }
            if (EventProcessor.CommandList.ContainsKey("/e3bcg") && EventProcessor.CommandList["/e3bcg"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bcg");
            }
            if (EventProcessor.CommandList.ContainsKey("/e3bcgz") && EventProcessor.CommandList["/e3bcgz"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bcgz");
            }
            if (EventProcessor.CommandList.ContainsKey("/e3bct") && EventProcessor.CommandList["/e3bct"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bct");
            }
            if (EventProcessor.CommandList.ContainsKey("/e3bcga") && EventProcessor.CommandList["/e3bcga"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bcga");
            }
            if (EventProcessor.CommandList.ContainsKey("/e3bcgaz") && EventProcessor.CommandList["/e3bcgaz"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bcgaz");
            }
            if (EventProcessor.CommandList.ContainsKey("/e3bcaa") && EventProcessor.CommandList["/e3bcaa"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bcaa");
            }
            if (EventProcessor.CommandList.ContainsKey("/e3bcaaz") && EventProcessor.CommandList["/e3bcaaz"].queuedEvents.Count > 0)
            {
                EventProcessor.ProcessEventsInQueues("/e3bcaaz");
            }
        }
        //primary E3N C# thread, we pull off the collections that was populated by the network thread
        //this way we can do queries/command/etc.
        public void ProcessCommands()
        {
            while (CommandQueue.Count > 0)
            {
                if (CommandQueue.TryDequeue(out var data))
                {
                    //command format
                    //$"{E3.CurrentName}:{noparse}:{command}"

                    string message = data.Data;
                    var typeInfo = data.TypeOfCommand;

                    data.Dispose(); //put back to be reused ,we have the data we want out of it.

                    if (typeInfo == OnCommandData.CommandType.BroadCastMessage || typeInfo == OnCommandData.CommandType.BroadCastMessageZone)
                    {
                        Int32 indexOfSeperator = message.IndexOf(':');
                        Int32 currentIndex = 0;
                        string user = message.Substring(currentIndex, indexOfSeperator);

                        if (typeInfo == OnCommandData.CommandType.BroadCastMessageZone)
                        {
                            //check to see if we are in the same zone as the person
                            Int32 spawnID = MQ.Query<Int32>($"${{Spawn[{user}].ID}}");
                            if (spawnID < 1)
                            {
                                //we can safely ignore this.
                                continue;
                            }
                        }

                        currentIndex = indexOfSeperator + 1;
                        string bcMessage = message.Substring(currentIndex, message.Length - currentIndex);

                        MQ.Cmd($"/echo \a#336699[{MainProcessor.ApplicationName}]\a-w{System.DateTime.Now.ToString("HH:mm:ss")}\ar<\ay{user}\ar> \aw{bcMessage}");
                    }
                    else
                    {
                        Int32 indexOfSeperator = message.IndexOf(':');
                        Int32 currentIndex = 0;
                        string user = message.Substring(currentIndex, indexOfSeperator);
                        currentIndex = indexOfSeperator + 1;
                        indexOfSeperator = message.IndexOf(':', indexOfSeperator + 1);

                        string noparseString = message.Substring(currentIndex, indexOfSeperator - currentIndex);
                        currentIndex = indexOfSeperator + 1;
                        string command = message.Substring(currentIndex, message.Length - currentIndex);
                        //a command type

                        if (typeInfo == OnCommandData.CommandType.OnCommandAllExceptMeZone || typeInfo == OnCommandData.CommandType.OnCommandAllZone || typeInfo == OnCommandData.CommandType.OnCommandGroupZone || typeInfo == OnCommandData.CommandType.OnCommandGroupAllZone)
                        {

                            //this is a zone type command lets verify zone logic
                            //check to see if we are in the same zone as the person
                            Int32 spawnID = MQ.Query<Int32>($"${{Spawn[{user}].ID}}");
                            if (spawnID < 1)
                            {
                                //we can safely ignore this.
                                continue;
                            }
                        }


                        if (typeInfo == OnCommandData.CommandType.OnCommandGroup || typeInfo == OnCommandData.CommandType.OnCommandGroupZone)
                        {
                            //check to see if we are part of their group
                            if (user == E3.CurrentName)
                            {
                                //not for us only group members
                                continue;
                            }
                            //check to see if we are part of their group
                            Int32 groupMemberIndex = MQ.Query<Int32>($"${{Group.Member[{user}].Index}}");

                            if (groupMemberIndex < 0)
                            {
                                //ignore it
                                continue;
                            }
                        }
                        else if (typeInfo == OnCommandData.CommandType.OnCommandGroupAll || typeInfo == OnCommandData.CommandType.OnCommandGroupAllZone)
                        {
                            //check to see if we are part of their group
                            Int32 groupMemberIndex = MQ.Query<Int32>($"${{Group.Member[{user}].Index}}");

                            if (groupMemberIndex < 0)
                            {
                                //ignore it
                                continue;
                            }
                        }
                        else if (typeInfo == OnCommandData.CommandType.OnCommandRaid)
                        {
                            //check to see if we are part of their group
                            var inRaid = MQ.Query<bool>($"${{Raid.Member[{user}]}}");

                            if (!inRaid)
                            {
                                continue;
                            }
                        }

                        //check to see if we are part of their group
                        if (user == E3.CurrentName && (!(typeInfo == OnCommandData.CommandType.OnCommandName || typeInfo == OnCommandData.CommandType.OnCommandGroupAll || typeInfo == OnCommandData.CommandType.OnCommandAll || typeInfo == OnCommandData.CommandType.OnCommandGroupAllZone || typeInfo == OnCommandData.CommandType.OnCommandAllZone)))
                        {
                            //if not an all type command and not us, kick out.
                            //not for us only group members
                            continue;
                        }

                        MQ.Write($"\ag<\ap{user}\ag> Command:" + command);
                        if (command.StartsWith("/mono ", StringComparison.OrdinalIgnoreCase) || command.StartsWith("/shutdown", StringComparison.OrdinalIgnoreCase))
                        {
                            //in case this is a restart command, we need to delay the command so it happens outside of the OnPulse. just assume all /mono commands are 
                            //delayed
                            if (Core._MQ2MonoVersion >= 0.22m)
                            {
                                //needs to be delayed, so that the restarts happes outside of the E3N OnPulse
                                MQ.Cmd(command, true);
                            }
                            else
                            {
                                E3.Bots.Broadcast("Sorry cannot execute /mono commands via broadcast unless your MQ2Mono version is 0.22 or above.");
                            }
                        }
                        else
                        {
                            bool internalComand = false;
                            foreach (var pair in EventProcessor.CommandList)
                            {
                                if (command.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
                                {
                                    internalComand = true;
                                    //no need to send this to mq if its our own command, just drop it into the queues to be processed. 
                                    EventProcessor.ProcessMQCommand(command);
                                    break;
                                }

                            }
                            if (!internalComand)
                            {
                                MQ.Cmd(command);

                            }
                        }

                    }



                }

            }
        }
        //reading the commands off the wire from this thread, to post to the main thread collection
        //with proper enums
        public void Process(string user, string port, string serverName, string fileName)
        {
            System.DateTime lastFileUpdate = System.IO.File.GetLastWriteTime(fileName);
            string OnCommandName = "OnCommand-" + E3.CurrentName;

            //timespan we expect to have some type of message
            TimeSpan recieveTimeout = new TimeSpan(0, 0, 0, 2, 0);
            using (var subSocket = new SubscriberSocket())
            {
                try
                {

                    subSocket.Options.ReceiveHighWatermark = 1000;
                    subSocket.Options.TcpKeepalive = true;
                    subSocket.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(5);
                    subSocket.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(1);
                    subSocket.Connect($"tcp://{serverName}:" + port);
                    subSocket.Subscribe(OnCommandName);
                    subSocket.Subscribe("OnCommand-All");
                    subSocket.Subscribe("OnCommand-AllZone");
                    subSocket.Subscribe("OnCommand-AllExceptMe");
                    subSocket.Subscribe("OnCommand-AllExceptMeZone");
                    subSocket.Subscribe("OnCommand-Group");
                    subSocket.Subscribe("OnCommand-GroupAll");
                    subSocket.Subscribe("OnCommand-GroupAllZone");
                    subSocket.Subscribe("OnCommand-Raid");
                    subSocket.Subscribe("OnCommand-Zone");
                    subSocket.Subscribe("BroadCastMessage");
                    subSocket.Subscribe("BroadCastMessageZone");
                    subSocket.Subscribe("${Me."); //all Me stuff should be subscribed to
                    MQ.Write("\agShared Data Client: Connecting to user:" + user + " on port:" + port + " server:" + serverName); ;

                    while (Core.IsProcessing && E3.NetMQ_SharedDataServerThradRun)
                    {
                        string messageTopicReceived;
                        if (subSocket.TryReceiveFrameString(recieveTimeout, out messageTopicReceived))
                        {
                            string messageReceived = subSocket.ReceiveFrameString();

                            if (messageTopicReceived == "OnCommand-All")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandAll;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "OnCommand-AllZone")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandAllZone;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "OnCommand-Group")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandGroup;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "OnCommand-GroupZone")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandGroupZone;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "OnCommand-AllExceptMe")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandAllExceptMe;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "OnCommand-AllExceptMeZone")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandAllExceptMeZone;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "OnCommand-GroupAll")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandGroupAll;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "OnCommand-GroupAllZone")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandGroupAllZone;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "OnCommand-Raid")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandRaid;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "BroadCastMessage")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.BroadCastMessage;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == "BroadCastMessageZone")
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.BroadCastMessageZone;

                                CommandQueue.Enqueue(data);
                            }
                            else if (messageTopicReceived == OnCommandName)
                            {
                                var data = OnCommandData.Aquire();
                                data.Data = messageReceived;
                                data.TypeOfCommand = OnCommandData.CommandType.OnCommandName;

                                CommandQueue.Enqueue(data);

                            }
                            else
                            {
                                Int64 updateTime = Core.StopWatch.ElapsedMilliseconds;
                                if (!TopicUpdates[user].ContainsKey(messageTopicReceived))
                                {
                                    TopicUpdates[user].TryAdd(messageTopicReceived, new ShareDataEntry() { Data = messageReceived, LastUpdate = updateTime });
                                }


                                var entry = TopicUpdates[user][messageTopicReceived];

                                lock (entry)
                                {
                                    //why do work if its the same data?									
                                    if (entry.Data != messageReceived)
                                    {
                                        entry.Data = messageReceived;
                                        entry.LastUpdate = updateTime;
                                    }
                                }

                            }
                        }
                        else
                        {   //we didn't get a message in the timespan we were expecting, verify if we need to reconnect
                            try
                            {
                                System.DateTime currentTime = System.IO.File.GetLastWriteTime(fileName);
                                if (currentTime > lastFileUpdate)
                                {
                                    MQ.Write($"\agShared Data Client: Disconnecting server:{serverName} port:" + port + " for toon:" + user);
                                    //shutown the socket and restart it
                                    subSocket.Disconnect($"tcp://{serverName}:" + port);
                                    string data = System.IO.File.ReadAllText(fileName);
                                    string[] splitData = data.Split(new char[] { ':' });
                                    port = splitData[0];
                                    serverName = splitData[1];
                                    MQ.Write($"\agShared Data Client: Reconnecting to server:{serverName} port:" + port + " for toon:" + user);
                                    subSocket.Connect($"tcp://{serverName}:" + port);
                                    lastFileUpdate = currentTime;
                                }
                            }
                            catch (Exception)
                            {
                                //file deleted most likely, kill the thread
                                MQ.Write("\agShared Data Client: Issue reading port file, shutting down thread for toon:" + user);

                                subSocket.Dispose();
                                if (TopicUpdates.TryRemove(user, out var tout))
                                {

                                }

                                break;

                            }
                        }

                    }

                    subSocket.Dispose();
                }
                catch (Exception)
                {
                }

            }


            MQ.Write($"Shutting down Share Data Thread for {user}.");
            lock (_processLock)
            {
                _processTasks.Remove(user);
            }
        }

        public class OnCommandData
        {
            public enum CommandType
            {
                None,
                OnCommandAll,
                OnCommandAllZone,
                OnCommandAllExceptMe,
                OnCommandAllExceptMeZone,
                OnCommandGroup,
                OnCommandGroupZone,
                OnCommandGroupAll,
                OnCommandGroupAllZone,
                OnCommandRaid,
                BroadCastMessage,
                BroadCastMessageZone,
                OnCommandName
            }
            public string Data { get; set; }
            public CommandType TypeOfCommand { get; set; }

            public Dictionary<Int32, Int64> BuffDurations = new Dictionary<int, Int64>();
            public Int64 LastUpdate = 0;

            public static OnCommandData Aquire()
            {
                OnCommandData obj;
                if (!StaticObjectPool.TryPop<OnCommandData>(out obj))
                {
                    obj = new OnCommandData();
                }

                return obj;
            }
            public void Dispose()
            {
                TypeOfCommand = CommandType.None;
                Data = String.Empty;
                StaticObjectPool.Push(this);
            }
            ~OnCommandData()
            {
                //DO NOT CALL DISPOSE FROM THE FINALIZER! This should only ever be used in using statements
                //if this is called, it will cause the domain to hang in the GC when shuttind down
                //This is only here to warn you

            }
        }
    }
}
