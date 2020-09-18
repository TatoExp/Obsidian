using Microsoft.Extensions.Logging;
using Obsidian.Blocks;
using Obsidian.Chat;
using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Events.EventArgs;
using Obsidian.Items;
using Obsidian.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Net.Packets.Play.Server;
using Obsidian.Plugins;
using Obsidian.Sounds;
using Obsidian.Util;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Debug;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Qmmands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian
{
    public struct QueueChat
    {
        public string Message;
        public sbyte Position;
    }

    public class Server
    {
        private readonly ConcurrentQueue<QueueChat> chatmessages;
        private readonly ConcurrentQueue<PlayerDigging> diggers;
        private readonly ConcurrentQueue<PlayerBlockPlacement> placed;
        private readonly ConcurrentHashSet<Client> clients;

        private readonly CancellationTokenSource cts;
        private readonly TcpListener tcpListener;

        public DateTimeOffset StartTime { get; private set; }

        public WorldGenerator WorldGenerator { get; private set; }

        public MinecraftEventHandler Events { get; }
        public PluginManager PluginManager { get; }

        public OperatorList Operators { get; }

        public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; } = new ConcurrentDictionary<Guid, Player>();

        internal ConcurrentDictionary<int, Inventory> CachedWindows { get; } = new ConcurrentDictionary<int, Inventory>();

        public Dictionary<string, WorldGenerator> WorldGenerators { get; } = new Dictionary<string, WorldGenerator>();

        public CommandService Commands { get; }
        public Config Config { get; }

        public ILogger Logger { get; }

        public LoggerProvider LoggerProvider { get; }

        public int TotalTicks { get; private set; }

        public int Id { get; }
        public string Version { get; }
        public int Port { get; }

        public World World { get; }

        public string ServerFolderPath => Path.GetFullPath($"Server-{this.Id}");

        /// <summary>
        /// Creates a new Server instance.
        /// </summary>
        /// <param name="version">Version the server is running.</param>
        public Server(Config config, string version, int serverId)
        {
            this.Config = config;

            this.LoggerProvider = new LoggerProvider(LogLevel.Information);
            this.Logger = this.LoggerProvider.CreateLogger($"Server/{this.Id}");
            //This stuff down here needs to be looked into
            Program.PacketLogger = this.LoggerProvider.CreateLogger("Packets");
            PacketDebug.Logger = this.LoggerProvider.CreateLogger("PacketDebug");
            Registry.Logger = this.LoggerProvider.CreateLogger("Registry");

            this.Port = config.Port;
            this.Version = version;
            this.Id = serverId;

            this.tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this.clients = new ConcurrentHashSet<Client>();

            this.cts = new CancellationTokenSource();
            this.chatmessages = new ConcurrentQueue<QueueChat>();
            this.diggers = new ConcurrentQueue<PlayerDigging>();
            this.placed = new ConcurrentQueue<PlayerBlockPlacement>();
            this.Commands = new CommandService(new CommandServiceConfiguration()
            {
                CaseSensitive = false,
                DefaultRunMode = RunMode.Parallel,
                IgnoreExtraArguments = true
            });
            this.Commands.AddModule<MainCommandModule>();
            this.Commands.AddTypeParser(new LocationTypeParser());


            this.Events = new MinecraftEventHandler();

            this.PluginManager = new PluginManager(this);
            this.Operators = new OperatorList(this);

            this.World = new World("", this.WorldGenerator);

            this.Events.PlayerLeave += this.Events_PlayerLeave;
            this.Events.PlayerJoin += this.Events_PlayerJoin;
            Console.CancelKeyPress += this.Console_CancelKeyPress;
        }

        /// <summary>
        /// Checks if a player is online
        /// </summary>
        /// <param name="username">The username you want to check for</param>
        /// <returns>True if the player is online</returns>
        public bool IsPlayerOnline(string username) => this.OnlinePlayers.Any(x => x.Value.Username == username);

        public bool IsPlayerOnline(Guid uuid) => this.OnlinePlayers.ContainsKey(uuid);

        /// <summary>
        /// Sends a message to all online players on the server
        /// </summary>
        public Task BroadcastAsync(string message, sbyte position = 0)
        {
            this.chatmessages.Enqueue(new QueueChat() { Message = message, Position = position });
            this.Logger.LogInformation(message);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Registers a new entity to the server
        /// </summary>
        /// <param name="input">A compatible entry</param>
        /// <exception cref="Exception">Thrown if unknown/unhandable type has been passed</exception>
        public Task RegisterAsync(params object[] input)
        {
            foreach (object item in input)
            {
                switch (item)
                {
                    default:
                        throw new Exception($"Input ({item.GetType()}) can't be handled by RegisterAsync.");

                    case WorldGenerator generator:
                        Logger.LogDebug($"Registering {generator.Id}...");
                        this.WorldGenerators.Add(generator.Id, generator);
                        break;
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts this server
        /// </summary>
        public async Task StartServer()
        {
            this.StartTime = DateTimeOffset.Now;

            this.Logger.LogInformation($"Launching Obsidian Server v{Version} with ID {Id}");

            //Check if MPDM and OM are enabled, if so, we can't handle connections
            if (this.Config.MulitplayerDebugMode && this.Config.OnlineMode)
            {
                this.Logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
                this.StopServer();
                return;
            }

            await Registry.RegisterBlocksAsync();
            await Registry.RegisterItemsAsync();
            await Registry.RegisterBiomesAsync();

            this.Logger.LogInformation($"Loading properties...");
            await this.Operators.InitializeAsync();
            await this.RegisterDefaultAsync();

            this.Logger.LogInformation("Loading plugins...");
            await this.PluginManager.LoadPluginsAsync(this.Logger);

            if (this.WorldGenerators.TryGetValue(this.Config.Generator, out WorldGenerator value))
            {
                this.WorldGenerator = value;
            }
            else
            {
                this.Logger.LogWarning($"Generator ({this.Config.Generator}) is unknown. Using default generator");
                this.WorldGenerator = new SuperflatGenerator();
            }

            this.Logger.LogInformation($"World generator set to {this.WorldGenerator.Id} ({this.WorldGenerator})");

            this.Logger.LogInformation("Starting backend...");
            await Task.Factory.StartNew(async () => { await this.ServerLoop().ConfigureAwait(false); });

            if (!this.Config.OnlineMode)
                this.Logger.LogInformation($"Starting in offline mode...");

            this.Logger.LogDebug($"Listening for new clients...");
            this.tcpListener.Start();

            while (!cts.IsCancellationRequested)
            {
                var tcp = await this.tcpListener.AcceptTcpClientAsync();

                this.Logger.LogDebug($"New connection from client with IP {tcp.Client.RemoteEndPoint}");

                var clnt = new Client(tcp, this.Config, Math.Max(0, this.clients.Count), this);
                this.clients.Add(clnt);

                await Task.Factory.StartNew(async () => { await clnt.StartConnectionAsync().ConfigureAwait(false); });
            }

            this.Logger.LogWarning($"Cancellation has been requested. Stopping server...");
        }

        internal async Task BroadcastBlockPlacementAsync(Player player, PlayerBlockPlacement pbp)
        {
            foreach (var (uuid, other) in this.OnlinePlayers.Except(player))
            {
                var client = other.client;

                var location = pbp.Location;
                var face = pbp.Face;

                switch (face)
                {
                    case BlockFace.Bottom:
                        location.Y -= 1;
                        break;

                    case BlockFace.Top:
                        location.Y += 1;
                        break;

                    case BlockFace.North:
                        location.Z -= 1;
                        break;

                    case BlockFace.South:
                        location.Z += 1;
                        break;

                    case BlockFace.West:
                        location.X -= 1;
                        break;

                    case BlockFace.East:
                        location.X += 1;
                        break;

                    default:
                        break;
                }

                var placedBlock = (Materials)player.GetHeldItem().Id;
                await client.QueuePacketAsync(new BlockChange(location, Registry.GetBlock(placedBlock).Id));
            }
        }

        internal async Task ParseMessage(string message, Client source, sbyte position = 0)
        {
            if (!CommandUtilities.HasPrefix(message, '/', out string output))
            {
                await this.BroadcastAsync($"<{source.Player.Username}> {message}", position);
                return;
            }

            //TODO command logging
            var context = new CommandContext(source, this);
            IResult result = await Commands.ExecuteAsync(output, context);
            if (!result.IsSuccessful)
                await context.Player.SendMessageAsync($"{ChatColor.Red}Command error: {(result as FailedResult).Reason}", position);
        }

        internal async Task BroadcastPacketAsync(Packet packet, params Player[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Except(excluded))
                await player.client.QueuePacketAsync(packet);
        }

        internal async Task BroadcastPacketWithoutQueueAsync(Packet packet, params Player[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Except(excluded))
                await player.client.SendPacketAsync(packet);
        }

        internal async Task DisconnectIfConnectedAsync(string username, ChatMessage reason = null)
        {
            var player = this.OnlinePlayers.Values.FirstOrDefault(x => x.Username == username);
            if (player != null)
            {
                if (reason is null)
                    reason = ChatMessage.Simple("Connected from another location");

                await player.KickAsync(reason);
            }
        }

        internal void EnqueueDigging(PlayerDigging d) => this.diggers.Enqueue(d);

        internal void StopServer()
        {
            this.WorldGenerators.Clear(); //Clean up for memory and next boot
            this.cts.Cancel();

            foreach (var client in this.clients)
                client.Disconnect();

            Console.WriteLine("shutting down..");
        }

        private async Task ServerLoop()
        {
            var keepaliveticks = 0;
            while (!this.cts.IsCancellationRequested)
            {
                await Task.Delay(50);

                this.TotalTicks++;
                await this.Events.InvokeServerTickAsync();

                keepaliveticks++;
                if (keepaliveticks > 50)
                {
                    var keepaliveid = DateTime.Now.Millisecond;

                    foreach (var clnt in this.clients.Where(x => x.State == ClientState.Play))
                        _ = Task.Run(async () => { await clnt.ProcessKeepAlive(keepaliveid); });

                    keepaliveticks = 0;
                }

                foreach (var (uuid, player) in this.OnlinePlayers)
                {
                    if (this.Config.Baah.HasValue)
                    {
                        var pos = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z);
                        await player.SendSoundAsync(461, pos, SoundCategory.Master, 1.0f, 1.0f);
                    }

                    if (this.chatmessages.TryPeek(out QueueChat msg))
                        await player.SendMessageAsync(msg.Message, msg.Position);

                    if (this.diggers.TryPeek(out PlayerDigging d))
                    {
                        var b = new BlockChange(d.Location, Registry.GetBlock(Materials.Air).Id);

                        await player.client.QueuePacketAsync(b);
                    }
                }

                this.chatmessages.TryDequeue(out var _);
                this.diggers.TryDequeue(out var _);

                foreach (var client in clients)
                {
                    if (!client.tcp.Connected)
                    {
                        this.clients.TryRemove(client);

                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Registers the "obsidian-vanilla" entities and objects
        /// </summary>
        private async Task RegisterDefaultAsync()
        {
            await this.RegisterAsync(new SuperflatGenerator());
            await this.RegisterAsync(new TestBlocksGenerator());
        }

        private async Task SendSpawnPlayerAsync(Player except)
        {
            foreach (var (_, player) in this.OnlinePlayers.Except(except))
            {
                await player.client.QueuePacketAsync(new EntityMovement { EntityId = except.client.id });
                await player.client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = except.client.id,
                    Uuid = except.Uuid,
                    Position = except.Position,
                    Yaw = 0,
                    Pitch = 0
                });

                await except.client.QueuePacketAsync(new EntityMovement { EntityId = player.client.id });
                await except.client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = player.client.id,
                    Uuid = player.Uuid,
                    Position = player.Position,
                    Yaw = 0,
                    Pitch = 0
                });
            }
        }

        private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            // TODO: TRY TO GRACEFULLY SHUT DOWN THE SERVER WE DONT WANT ERRORS REEEEEEEEEEE
            this.StopServer();
        }

        #region events

        private async Task Events_PlayerLeave(PlayerLeaveEventArgs e)
        {
            foreach (var (_, other) in this.OnlinePlayers.Except(e.Player))
                await other.client.RemovePlayerFromListAsync(e.Player);

            await this.BroadcastAsync(string.Format(this.Config.LeaveMessage, e.Player.Username));
        }

        private async Task Events_PlayerJoin(PlayerJoinEventArgs e)
        {
            await this.BroadcastAsync(string.Format(this.Config.JoinMessage, e.Player.Username));
            foreach (var (_, other) in this.OnlinePlayers)
            {
                await other.client.AddPlayerToListAsync(e.Player);
            }

            await this.SendSpawnPlayerAsync(e.Player);
        }

        #endregion events
    }
}