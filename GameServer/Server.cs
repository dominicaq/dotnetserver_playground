using System;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GameServer;

public class Server
{
    public ServerConfig Config { get; }

    private Thread? _thread;
    private bool _running;
    private NetManager? _netManager;
    private EventBasedNetListener? _listener;

    public Server(ServerConfig? config = null)
    {
        var loadedConfig = config ?? ServerConfig.LoadFromFile();
        if (loadedConfig == null)
        {
            Console.WriteLine("Failed to load config file. Please ensure server_config.json exists with valid settings.");
            Environment.Exit(1);
        }
        Config = loadedConfig;
    }

    public void Start()
    {
        SetupNetworking();
        SetupEventHandlers();
        StartServerLoop();

        Console.WriteLine($"{Config.ServerName} started on port {Config.ServerPort}");
    }

    public void Stop()
    {
        _running = false;
        _netManager?.Stop();
        Console.WriteLine("Server stopped.");
    }

    private void SetupNetworking()
    {
        _listener = new EventBasedNetListener();
        _netManager = new NetManager(_listener)
        {
            DisconnectTimeout = Config.NetworkDisconnectTimeout
        };
        _netManager.Start(Config.ServerPort);
    }

    private void SetupEventHandlers()
    {
        _listener!.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPlayerJoined;
        _listener.PeerDisconnectedEvent += OnPlayerLeft;
        _listener.NetworkReceiveEvent += OnMessageReceived;
    }

    private void StartServerLoop()
    {
        _running = true;
        _thread = new Thread(ServerLoop) { IsBackground = true };
        _thread.Start();
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        if (_netManager!.ConnectedPeersCount < Config.ServerMaxPlayers)
        {
            request.AcceptIfKey(Config.ServerConnectionKey);
        }
        else
        {
            request.Reject();
            if (Config.LoggingEnableConsole)
                Console.WriteLine("Connection rejected: Server full");
        }
    }

    private void OnPlayerJoined(NetPeer peer)
    {
        if (Config.LoggingPlayerEvents)
            Console.WriteLine($"Player joined: {peer.Address}:{peer.Port} ({_netManager!.ConnectedPeersCount}/{Config.ServerMaxPlayers})");
    }

    private void OnPlayerLeft(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (Config.LoggingPlayerEvents)
            Console.WriteLine($"Player left: {peer.Address}:{peer.Port} ({_netManager!.ConnectedPeersCount}/{Config.ServerMaxPlayers})");
    }

    private void OnMessageReceived(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        try
        {
            var message = reader.GetString();

            if (Config.LoggingEnableConsole)
                Console.WriteLine($"Received: {message}");

            BroadcastMessage(message, excludePeer: peer);
        }
        catch (Exception ex)
        {
            if (Config.LoggingEnableConsole)
                Console.WriteLine($"Error processing message: {ex.Message}");
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void BroadcastMessage(string message, NetPeer? excludePeer = null)
    {
        var writer = new NetDataWriter();
        writer.Put(message);

        foreach (var peer in _netManager!.ConnectedPeerList)
        {
            if (peer != excludePeer)
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    private void ServerLoop()
    {
        if (Config.NetworkTickRate <= 0)
        {
            Console.WriteLine($"Invalid NetworkTickRate: {Config.NetworkTickRate}. Must be greater than 0.");
            return;
        }

        var tickInterval = TimeSpan.FromMilliseconds(1000.0 / Config.NetworkTickRate);
        var lastTick = DateTime.UtcNow;

        while (_running)
        {
            _netManager!.PollEvents();

            var now = DateTime.UtcNow;
            if (now - lastTick >= tickInterval)
            {
                Tick();
                lastTick = now;
            }

            Thread.Sleep(1);
        }
    }

    private void Tick()
    {
        if (Config.LoggingEnableTick)
            Console.WriteLine($"Server tick at {DateTime.Now:HH:mm:ss}");

        if (Config.NetworkEnableHeartbeat)
            SendHeartbeat();
    }

    private void SendHeartbeat()
    {
        var writer = new NetDataWriter();
        writer.Put("heartbeat");
        writer.Put(DateTime.Now.ToString("HH:mm:ss"));

        foreach (var peer in _netManager!.ConnectedPeerList)
            peer.Send(writer, DeliveryMethod.Unreliable);
    }
}
