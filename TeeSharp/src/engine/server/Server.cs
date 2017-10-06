﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace TeeSharp.Server
{
    public class Server : IServer
    {
        public long Tick => _currentGameTick;
        public bool IsRunning;

        protected IGameConsole _gameConsole;
        protected IGameContext _gameContext;
        protected IEngineMap _map;
        protected IStorage _storage;
        protected INetworkServer _networkServer;
        protected Configuration _config;
        protected ServerBan _serverBan;
        protected ServerRegister _register;

        protected ServerClient[] _clients;
        protected long _currentGameTick;
        protected long _gameStartTime;

        protected int _currentMapCrc;
        protected int _currentMapSize;
        protected byte[] _currentMapData;

        public virtual bool LoadMap(string mapName)
        {
            return true;
        }

        public ServerClient GetClient(int clientId)
        {
            return _clients[clientId];
        }

        public virtual void RegisterCommands()
        {
            _gameConsole.RegisterCommand("kick", "int", ConfigFlags.SERVER, ConsoleKick, this, "Kick player with specified id for any reason");
            _gameConsole.RegisterCommand("ban", "string", ConfigFlags.SERVER | ConfigFlags.STORE, ConsoleBan, this, "Ban player with ip/id for x minutes for any reason");
            _gameConsole.RegisterCommand("unban", "string", ConfigFlags.SERVER | ConfigFlags.STORE, ConsoleUnBan, this, "Unban ip");
            _gameConsole.RegisterCommand("bans", "", ConfigFlags.SERVER | ConfigFlags.STORE, ConsoleBans, this, "Show banlist");
            _gameConsole.RegisterCommand("status", "", ConfigFlags.SERVER, ConsoleStatus, this, "List players");
            _gameConsole.RegisterCommand("shutdown", "", ConfigFlags.SERVER, ConsoleShutdown, this, "Shut down");
            _gameConsole.RegisterCommand("reload", "", ConfigFlags.SERVER, ConsoleMapReload, this, "Reload the map");
            
            _gameConsole.OnExecuteCommand("sv_name", SpecialInfoUpdate);
            _gameConsole.OnExecuteCommand("password", SpecialInfoUpdate);
            _gameConsole.OnExecuteCommand("sv_max_clients_per_ip", MaxClientsPerIpUpdate);
            _gameConsole.OnExecuteCommand("mod_command", ModCommandUpdate);
            _gameConsole.OnExecuteCommand("console_output_level", ConsoleOutputLevelUpdate);

            // register console commands in sub parts
            _serverBan.InitServerBan();
            _gameContext.OnConsoleInit();
        }

        protected virtual void ConsoleOutputLevelUpdate(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void ModCommandUpdate(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void MaxClientsPerIpUpdate(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void SpecialInfoUpdate(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void ConsoleMapReload(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void ConsoleShutdown(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void ConsoleStatus(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void ConsoleBans(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void ConsoleUnBan(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void ConsoleBan(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual void ConsoleKick(ConsoleResult result, object data)
        {
            throw new NotImplementedException();
        }

        protected virtual long TickStartTime(long tick)
        {
            return _gameStartTime + (Base.TimeFreq() * tick) / Consts.SERVER_TICK_SPEED;
        }

        protected virtual void DefaultBindinds()
        {
            if (!Kernel.IsBinded<ServerClient>()) Kernel.Bind<ServerClient, ServerClient>();
            if (!Kernel.IsBinded<IPlayer>()) Kernel.Bind<IPlayer, Player>();

            // bind singletons
            if (!Kernel.IsBinded<ServerRegister>()) Kernel.Bind<ServerRegister, ServerRegister>(new ServerRegister());
            if (!Kernel.IsBinded<ServerBan>()) Kernel.Bind<ServerBan, ServerBan>(new ServerBan());
            if (!Kernel.IsBinded<Configuration>()) Kernel.Bind<Configuration, Configuration>(new Configuration());
            if (!Kernel.IsBinded<IGameContext>()) Kernel.Bind<IGameContext, GameContext>(new GameContext());
            if (!Kernel.IsBinded<IEngineMap>()) Kernel.Bind<IEngineMap, Map>(new Map());
            if (!Kernel.IsBinded<IStorage>()) Kernel.Bind<IStorage, Storage>(new Storage());
            if (!Kernel.IsBinded<INetworkServer>()) Kernel.Bind<INetworkServer, NetworkServer>(new NetworkServer());
            if (!Kernel.IsBinded<IGameConsole>()) Kernel.Bind<IGameConsole, GameConsole>(new GameConsole());
        }

        public virtual void Init(string[] args)
        {
            DefaultBindinds();

            _currentGameTick = 0;
            _clients = new ServerClient[Consts.MAX_CLIENTS];

            for (var i = 0; i < _clients.Length; i++)
            {
                _clients[i] = Kernel.Get<ServerClient>();
                _clients[i].SnapshotStorage.Init();
            }

            _serverBan = Kernel.Get<ServerBan>();
            _register = Kernel.Get<ServerRegister>();
            _config = Kernel.Get<Configuration>();
            _gameContext = Kernel.Get<IGameContext>();
            _map = Kernel.Get<IEngineMap>();
            _storage = Kernel.Get<IStorage>();
            _networkServer = Kernel.Get<INetworkServer>();
            _gameConsole = Kernel.Get<IGameConsole>();

            var registerFail = false;
            registerFail = registerFail || _gameContext == null;
            registerFail = registerFail || _map == null;
            registerFail = registerFail || _storage == null;
            registerFail = registerFail || _networkServer == null;
            registerFail = registerFail || _config == null;
            registerFail = registerFail || _gameConsole == null;

            if (registerFail)
                throw new Exception("Register components fail");

            _register.Init();
            _storage.Init("Teeworlds");
            _gameConsole.Init();
            _networkServer.Init();

            // register all console commands
            RegisterCommands();

            // execute autoexec file
            _gameConsole.ExecuteFile("autoexec.cfg");
            _gameConsole.ParseArguments(args);
        }

        protected virtual void SendRconLineAuthed(string str)
        {
            
        }

        protected virtual void ProcessClientPacket(NetChunk packet)
        {
            var clientId = packet.ClientId;
            var unpacker = new Unpacker();
            unpacker.Reset(packet.Data, packet.DataSize);

            var msg = unpacker.GetInt();
            var sys = msg & 1;
            msg >>= 1;

            if (unpacker.Error)
                return;

            var message = (NetMessages)msg;
            if (_config.GetInt("SvNetlimit") != 0 && message != NetMessages.NETMSG_REQUEST_MAP_DATA)
            {
                var time = Base.TimeGet();
                var diff = time - _clients[clientId].TrafficSince;
                var alpha = _config.GetInt("SvNetlimitAlpha") / 100.0f;
                var limit = (float) _config.GetInt("SvNetlimit") * 1024 / Base.TimeFreq();

                if (_clients[clientId].Traffic > limit)
                {
                    _serverBan.BanAddr(packet.Address, 600, "Stressing network");
                    return;
                }

                if (diff > 100)
                {
                    _clients[clientId].Traffic = (long) ((alpha * ((float) packet.DataSize / diff)) + (1.0f - alpha) * _clients[clientId].Traffic);
                    _clients[clientId].TrafficSince = time;
                }
            }

            if (sys != 0)
            {
                switch (message)
                {
                    case NetMessages.NETMSG_INFO:
                        NetMsgInfo(packet, unpacker, clientId);
                        break;
                    case NetMessages.NETMSG_REQUEST_MAP_DATA:
                        NetMsgRequestMapData(packet, unpacker, clientId);
                        break;
                    case NetMessages.NETMSG_READY:
                        NetMsgReady(packet, unpacker, clientId);
                        break;
                    case NetMessages.NETMSG_ENTERGAME:
                        NetMsgEnterGame(packet, unpacker, clientId);
                        break;
                    case NetMessages.NETMSG_INPUT:
                        NetMsgInput(packet, unpacker, clientId);
                        break;
                    case NetMessages.NETMSG_RCON_CMD:
                        NetMsgRconCmd(packet, unpacker, clientId);
                        break;
                    case NetMessages.NETMSG_RCON_AUTH:
                        NetMsgRconAuth(packet, unpacker, clientId);
                        break;
                    case NetMessages.NETMSG_PING:
                        NetMsgPing(packet, unpacker, clientId);
                        break;
                    default:
                        if (_config.GetInt("Debug") != 0)
                            _gameConsole.Print(ConsoleOutputLevel.DEBUG, "server", $"strange message clientId={clientId} msg={msg} data_size={packet.DataSize}");
                        break;
                }
            }
            else
            {
                // game message
                if (_clients[clientId].ClientState >= ServerClientState.READY)
                    _gameContext.OnMessage(message, unpacker, clientId);
            }
        }

        protected virtual void NetMsgPing(NetChunk packet, Unpacker unpacker, int clientId)
        {
            var msg = new MsgPacker(NetMessages.NETMSG_PING_REPLY);
            SendMsgEx(msg, 0, clientId, true);
        }

        protected virtual void NetMsgRconAuth(NetChunk packet, Unpacker unpacker, int clientId)
        {
            
        }

        protected virtual void NetMsgRconCmd(NetChunk packet, Unpacker unpacker, int clientId)
        {
            var cmd = unpacker.GetString();
            if (unpacker.Error || _clients[clientId].AccessLevel == 0)
                return;

            _gameConsole.Print(ConsoleOutputLevel.ADDINFO, "server", $"ClientID={clientId} rcon='{cmd}'");
            _gameConsole.ExecuteLine(cmd, _clients[clientId].AccessLevel);
        }

        protected virtual void NetMsgInput(NetChunk packet, Unpacker unpacker, int clientId)
        {
            _clients[clientId].LastAckedSnapshot = unpacker.GetInt();
            var intendedTick = (long) unpacker.GetInt();
            var size = unpacker.GetInt();

            if (unpacker.Error || size / 4 > Consts.MAX_INPUT_SIZE)
                return;

            if (_clients[clientId].LastAckedSnapshot > 0)
                _clients[clientId].SnapRate = SnapRate.FULL;

            var time = Base.TimeGet();
            long tagTime;
            Snapshot snapshot;

            if (_clients[clientId].SnapshotStorage.Get(_clients[clientId].LastAckedSnapshot, out tagTime, out snapshot) >= 0) 
                _clients[clientId].Latency = (int)(((time - tagTime) * 1000) / Base.TimeFreq());

            if (intendedTick > _clients[clientId].LastInputTick)
            {
                var timeLeft = (int)(((TickStartTime(intendedTick) - time) * 1000) / Base.TimeFreq());
                var msg = new MsgPacker(NetMessages.NETMSG_INPUTTIMING);
                msg.AddInt((int) intendedTick);
                msg.AddInt(timeLeft);
                SendMsgEx(msg, 0, clientId, true);
            }

            _clients[clientId].LastInputTick = intendedTick;
            var input = _clients[clientId].Inputs[_clients[clientId].CurrentInput];

            if (intendedTick <= Tick)
                intendedTick = Tick + 1;

            input.GameTick = intendedTick;
            input.Data = new int[size / 4];

            for (var i = 0; i < input.Data.Length; i++)
                input.Data[i] = unpacker.GetInt();

            _clients[clientId].LatestInput.Data = input.Data;
            _clients[clientId].CurrentInput++;
            _clients[clientId].CurrentInput %= _clients[clientId].Inputs.Length;

            if (_clients[clientId].ClientState == ServerClientState.INGAME)
                _gameContext.OnClientDirectInput(clientId, _clients[clientId].LatestInput.Data);
        }

        protected virtual void NetMsgEnterGame(NetChunk packet, Unpacker unpacker, int clientId)
        {
            if (_clients[clientId].ClientState != ServerClientState.READY ||
                !_gameContext.IsClientReady(clientId))
                return;

            _gameConsole.Print(ConsoleOutputLevel.STANDARD, "server", $"player has entered the game. ClientID={clientId} addr={_networkServer.ClientAddr(clientId)}");
            _clients[clientId].ClientState = ServerClientState.INGAME;
            _gameContext.OnClientEnter(clientId);
        }

        protected virtual void NetMsgReady(NetChunk packet, Unpacker unpacker, int clientId)
        {
            if (_clients[clientId].ClientState != ServerClientState.CONNECTING)
                return;

            _gameConsole.Print(ConsoleOutputLevel.ADDINFO, "server", $"player is ready. ClientID={clientId} addr={_networkServer.ClientAddr(clientId)}");
            _clients[clientId].ClientState = ServerClientState.READY;
            _gameContext.OnClientConnected(clientId);

            var msg = new MsgPacker(NetMessages.NETMSG_CON_READY);
            SendMsgEx(msg, MsgFlags.VITAL | MsgFlags.FLUSH, clientId, true);
        }

        protected virtual void NetMsgRequestMapData(NetChunk packet, Unpacker unpacker, int clientId)
        {
            if (_clients[clientId].ClientState < ServerClientState.CONNECTING)
                return;

            var chunk = unpacker.GetInt();
            if (chunk != _clients[clientId].NextMapChunk || _config.GetInt("SvFastDownload") == 0)
            {
                SendMapData(clientId, chunk);
                return;
            }

            var mapWindow = _config.GetInt("SvMapWindow");
            if (chunk == 0)
            {
                for (var i = 0; i < mapWindow; i++)
                {
                    SendMapData(clientId, i);
                }
            }

            SendMapData(clientId, mapWindow + _clients[clientId].NextMapChunk);
            _clients[clientId].NextMapChunk++;
        }

        protected virtual void NetMsgInfo(NetChunk packet, Unpacker unpacker, int clientId)
        {
            if (_clients[clientId].ClientState != ServerClientState.AUTH)
                return;

            var version = unpacker.GetString(SanitizeType.SANITIZE_CC);
            if (!version.StartsWith(GameVersion.GAME_NETVERSION))
            {
                // wrong version
                _networkServer.Drop(clientId, $"Wrong version. Server is running '{GameVersion.GAME_NETVERSION}' and client '{version}'");
                return;
            }

            var password = unpacker.GetString(SanitizeType.SANITIZE_CC);
            if (!string.IsNullOrEmpty(_config.GetString("Password")) && password != _config.GetString("Password"))
            {
                // wrong password
                _networkServer.Drop(clientId, "Wrong password");
                return;
            }

            // reserved slot
            if (clientId >= _config.GetInt("SvMaxClients") - _config.GetInt("SvReservedSlots") &&
                !string.IsNullOrEmpty(_config.GetString("SvReservedSlotsPass")) && _config.GetString("SvReservedSlotsPass") != password)
            {
                _networkServer.Drop(clientId, "This server is full");
                return;
            }

            _clients[clientId].ClientState = ServerClientState.CONNECTING;
            SendMap(clientId);
        }

        protected virtual void SendMapData(int clientId, int chunk)
        {
            var chunkSize = 1024 - 128;
            var offset = chunk * chunkSize;
            var last = 0;

            if (chunk < 0 || offset > _currentMapSize)
                return;

            if (offset + chunkSize >= _currentMapSize)
            {
                chunkSize = _currentMapSize - offset;
                last = 1;
            }

            var msg = new MsgPacker(NetMessages.NETMSG_MAP_DATA);
            msg.AddInt(last);
            msg.AddInt(_currentMapCrc);
            msg.AddInt(chunk);
            msg.AddInt(chunkSize);
            msg.AddRaw(_currentMapData, offset, chunkSize);
            SendMsgEx(msg, MsgFlags.VITAL | MsgFlags.FLUSH, clientId, true);

            if (_config.GetInt("Debug") != 0)
                _gameConsole.Print(ConsoleOutputLevel.DEBUG, "server", $"sending chunk {chunk} with size {chunkSize}");
        }

        protected virtual void SendMap(int clientId)
        {
            var msg = new MsgPacker(NetMessages.NETMSG_MAP_CHANGE);
            msg.AddString(GetMapName());
            msg.AddInt(_currentMapCrc);
            msg.AddInt(_currentMapSize);
            SendMsgEx(msg, MsgFlags.VITAL | MsgFlags.FLUSH, clientId, true);
        }

        public bool SendMsg(MsgPacker msg, MsgFlags flags, int clientId)
        {
            return SendMsgEx(msg, flags, clientId, false);
        }

        public bool SendMsgEx(MsgPacker msg, MsgFlags flags, int clientId, bool system)
        {
            if (msg == null)
                return false;

            var packet = new NetChunk()
            {
                ClientId = clientId,
                DataSize = msg.Size(),
                Data = msg.Data(),
            };

            packet.Data[0] <<= 1;
            if (system)
                packet.Data[0] |= 1;

            if ((flags & MsgFlags.VITAL) != 0)
                packet.Flags |= SendFlag.VITAL;
            if ((flags & MsgFlags.FLUSH) != 0)
                packet.Flags |= SendFlag.FLUSH;

            if ((flags & MsgFlags.NOSEND) == 0)
            {
                if (clientId == -1)
                {
                    for (var i = 0; i < Consts.MAX_CLIENTS; i++)
                    {
                        if (_clients[i].ClientState == ServerClientState.INGAME)
                        {
                            packet.ClientId = i;
                            _networkServer.Send(packet);
                        }
                    }
                }
                else
                {
                    _networkServer.Send(packet);
                }
            }

            return true;
        }

        private void SendServerInfo(IPEndPoint addr, int token, bool showMore, int offset = 0)
        {
            var packet = new NetChunk();
            var p = new Packer();

            // count the players
            var playerCount = 0;
            var clientCount = 0;

            for (var i = 0; i < Consts.MAX_CLIENTS; i++)
            {
                if (_clients[i].ClientState != ServerClientState.EMPTY)
                {
                    if (_gameContext.IsClientPlayer(i))
                        playerCount++;
                    clientCount++;
                }
            }

            p.Reset();

            if (showMore)
                p.AddRaw(MasterServer.SERVERBROWSE_INFO64, 0, MasterServer.SERVERBROWSE_INFO64.Length);
            else
                p.AddRaw(MasterServer.SERVERBROWSE_INFO, 0, MasterServer.SERVERBROWSE_INFO.Length);

            p.AddString(token + "", 6);
            p.AddString(GameVersion.GAME_VERSION, 32);

            if (showMore)
            {
                p.AddString(_config.GetString("SvName"), 256);
            }
            else
            {
                if (Consts.NET_MAX_CLIENTS <= Consts.VANILLA_MAX_CLIENTS)
                    p.AddString(_config.GetString("SvName"), 64);
                else
                {
                    var aBuf = $"{_config.GetString("SvName")} [{clientCount}/{Consts.NET_MAX_CLIENTS}]";
                    p.AddString(aBuf, 64);
                }
            }

            p.AddString(GetMapName(), 32);
            p.AddString(_gameContext.GameType(), 16);

            // flags
            var pass = 0;
            if (!string.IsNullOrEmpty(_config.GetString("Password"))) // password set
                pass |= Consts.SERVER_FLAG_PASSWORD;
            p.AddString(pass.ToString(), 2);

            var maxClients = Consts.NET_MAX_CLIENTS;
            if (!showMore)
            {
                if (clientCount >= Consts.VANILLA_MAX_CLIENTS)
                {
                    if (clientCount < maxClients)
                        clientCount = Consts.VANILLA_MAX_CLIENTS - 1;
                    else
                        clientCount = Consts.VANILLA_MAX_CLIENTS;
                }

                if (maxClients > Consts.VANILLA_MAX_CLIENTS)
                    maxClients = Consts.VANILLA_MAX_CLIENTS;
            }

            if (playerCount > clientCount)
                playerCount = clientCount;

            p.AddString(playerCount + "", 3);                                       // num players
            p.AddString((maxClients - _config.GetInt("SvSpectatorSlots")) + "", 3); // max players
            p.AddString(clientCount + "", 3);                                       // num clients
            p.AddString(maxClients + "", 3);                                        // max clients

            if (showMore)
                p.AddInt(offset);

            var clientsPerPacket = showMore ? 24 : Consts.VANILLA_MAX_CLIENTS;
            var skip = offset;
            var take = clientsPerPacket;

            for (var i = 0; i < Consts.MAX_CLIENTS; i++)
            {
                if (_clients[i].ClientState != ServerClientState.EMPTY)
                {
                    if (skip-- > 0)
                        continue;
                    if (--take < 0)
                        break;

                    p.AddString(ClientName(i), Consts.MAX_NAME_LENGTH);         // client name
                    p.AddString(ClientClan(i), Consts.MAX_CLAN_LENGTH);         // client clan
                    p.AddString($"{ClientCountry(i)}", 6);                      // client country
                    p.AddString($"{ClientScore(i)}", 6);                        // client score
                    p.AddString(_gameContext.IsClientPlayer(i) ? "1" : "0", 2); // is player?
                }
            }

            packet.ClientId = -1;
            packet.Address = addr;
            packet.Flags = SendFlag.CONNLESS;
            packet.DataSize = p.Size();
            packet.Data = p.Data();

            _networkServer.Send(packet);

            if (showMore && take < 0)
                SendServerInfo(addr, token, true, offset + clientsPerPacket);
        }

        public int ClientScore(int clientId)
        {
            return 0;
        }

        public string ClientClan(int clientId)
        {
            return "clan";
        }

        public int ClientCountry(int clientId)
        {
            return 0;
        }

        public string ClientName(int clientId)
        {
            return "name";
        }

        protected virtual string GetMapName()
        {
            return Path.GetFileNameWithoutExtension(_config.GetString("SvMap"));
        }

        protected virtual void PumpNetwork()
        {
            _networkServer.Update();

            NetChunk packet;
            while (_networkServer.Receive(out packet))
            {
                if (packet.ClientId == -1)
                {
                    if (packet.DataSize == MasterServer.SERVERBROWSE_GETINFO.Length + 1 &&
                        Base.CompareArrays(packet.Data, MasterServer.SERVERBROWSE_GETINFO, 
                            MasterServer.SERVERBROWSE_GETINFO.Length))
                    {
                        SendServerInfo(packet.Address, packet.Data[MasterServer.SERVERBROWSE_GETINFO.Length], false);
                    }
                    else if (packet.DataSize == MasterServer.SERVERBROWSE_GETINFO64.Length + 1 &&
                        Base.CompareArrays(packet.Data, MasterServer.SERVERBROWSE_GETINFO64,
                            MasterServer.SERVERBROWSE_GETINFO64.Length))
                    {
                        SendServerInfo(packet.Address, packet.Data[MasterServer.SERVERBROWSE_GETINFO64.Length], true);
                    }
                    continue;   
                }

                ProcessClientPacket(packet);
            }

            _serverBan.Update();
            // econ update
        }

        public virtual void Run()
        {
            if (IsRunning)
                return;

            Base.DbgMessage("server", "starting...", ConsoleColor.Red);
#if DEBUG
            Base.DbgMessage("server", "running on debug version", ConsoleColor.Red);
#endif

            _gameConsole.RegisterPrintCallback((ConsoleOutputLevel) _config.GetInt("ConsoleOutputLevel"),
                SendRconLineAuthed, null);

            // load map
            if (!LoadMap(_config.GetString("SvMap")))
            {
                Base.DbgMessage("server", $"failed to load map. mapname='{_config.GetString("SvMap")}'");
                return;
            }

            _networkServer.Open(new IPEndPoint(IPAddress.Any, _config.GetInt("SvPort")),
                _config.GetInt("SvMaxClients"), _config.GetInt("SvMaxClientsPerIP"));
            _networkServer.SetCallbacks(NewClientCallback, DelClientCallback);

            _gameConsole.Print(ConsoleOutputLevel.STANDARD, "server", $"server name is '{_config.GetString("SvName")}'");
            _gameContext.OnInit();

            _gameStartTime = Base.TimeGet();
            IsRunning = true;

            var time = 0L;
            var ticks = 0;

            while (IsRunning)
            {
                time = Base.TimeGet();
                ticks = 0;

                while (time > TickStartTime(_gameStartTime + 1))
                {
                    _currentGameTick++;
                    ticks++;

                    for (var client = 0; client < Consts.MAX_CLIENTS; client++)
                    {
                        if (_clients[client].ClientState != ServerClientState.INGAME)
                            continue;

                        for (var input = 0; input < _clients[client].Inputs.Length; input++)
                        {
                            if (_clients[client].Inputs[input].GameTick == Tick)
                            {
                                _gameContext.OnClientPredictedInput(client, _clients[client].Inputs[input].Data);
                                break;
                            }
                        }
                    }

                    _gameContext.OnTick();
                }

                if (ticks != 0)
                {
                    if (_currentGameTick % 2 == 0 || _config.GetInt("SvHighBandwidth") != 0)
                        DoSnapshot();
                }

                _register.RegisterUpdate(_networkServer.NetType());
                PumpNetwork();

                Thread.Sleep(5);
            }

            for (var i = 0; i < Consts.MAX_CLIENTS; ++i)
            {
                if (_clients[i].ClientState != ServerClientState.EMPTY)
                    _networkServer.Drop(i, "Server shutdown");
            }

            _gameContext.OnShutdown();
            _map.
        }

        private void SendRconLineAuthed(string str, object data)
        {
            
        }

        protected virtual void DoSnapshot()
        {
        }

        protected virtual void NewClientCallback(int clientId)
        {
            _clients[clientId].ClientState = ServerClientState.AUTH;
            _clients[clientId].Reset();
        }

        protected virtual void DelClientCallback(int clientId, string reason)
        {
            var ip = _networkServer.ClientAddr(clientId).Address.ToString();
            _gameConsole.Print(ConsoleOutputLevel.ADDINFO, "server", $"client dropped. cid={clientId} addr={ip} reason='{reason}'");

            if (_clients[clientId].ClientState >= ServerClientState.READY)
                _gameContext.OnClientDrop(clientId, reason);

            _clients[clientId].ClientState = ServerClientState.EMPTY;
            _clients[clientId].Reset();
        }
    }
}
