using LiteNetLib;
using LiteNetLib.Utils;

namespace GameServer;

public class Server {
  public ServerConfig Config { get; }
  public event Action<string, NetPeer?, string>? ServerEvent;

  private bool _running;
  private readonly Lock _runningLock = new();
  private readonly Thread _thread;
  private readonly NetManager _netManager;
  private readonly EventBasedNetListener _listener;
  private readonly NetDataWriter _writer = new();

  public Server(ServerConfig config) {
    Config = config;

    _listener = new EventBasedNetListener();
    _netManager = new NetManager(_listener) { DisconnectTimeout = Config.NetworkDisconnectTimeout };
    _thread = new Thread(ServerLoop) { IsBackground = true };
  }

  public void Start() {
    lock (_runningLock) {
      if (_running) { return; }
      _running = true;
    }

    _netManager.Start(Config.ServerPort);

    _listener.ConnectionRequestEvent += OnConnectionRequest;
    _listener.PeerConnectedEvent += OnPlayerJoined;
    _listener.PeerDisconnectedEvent += OnPlayerLeft;
    _listener.NetworkReceiveEvent += OnMessageReceived;

    _thread.Start();

    Console.WriteLine($"{Config.ServerName} started on port {Config.ServerPort}");
  }

  public void Stop() {
    bool shouldNotify;
    lock (_runningLock) {
      if (!_running) { return; }
      _running = false;
      shouldNotify = _netManager.ConnectedPeersCount > 0;
    }
    if (shouldNotify) {
      BroadcastMessage("Server is shutting down");
      ServerEvent?.Invoke("ServerShutdown", null, "Server is shutting down");
      Thread.Sleep(100);
    }

    _netManager.Stop();
    Console.WriteLine("Server stopped.");
  }

  public void BanPlayer(NetPeer peer, string reason = "You have been banned") {
    bool canProceed;
    lock (_runningLock) {
      canProceed = _running;
    }

    if (!canProceed) { return; }

    SendMessage(peer, reason);
    Thread.Sleep(50);
    peer.Disconnect();
    ServerEvent?.Invoke("PlayerBanned", peer, reason);
  }

  private void OnConnectionRequest(ConnectionRequest request) {
    bool isRunning;
    lock (_runningLock) {
      isRunning = _running;
    }

    if (!isRunning) { return; }

    if (_netManager.ConnectedPeersCount >= Config.ServerMaxPlayers) {
      request.Reject();
      Log($"Connection rejected: Server full ({Config.ServerMaxPlayers} players)");
      ServerEvent?.Invoke("ConnectionRejected", null, "Server is full");
    } else if (!request.Data.GetString().Equals(Config.ServerConnectionKey)) {
      request.Reject();
      Log("Connection rejected: Invalid key");
      ServerEvent?.Invoke("ConnectionRejected", null, "Invalid connection key");
    } else {
      request.Accept();
    }
  }

  private void OnPlayerJoined(NetPeer peer) {
    bool isRunning;
    lock (_runningLock) {
      isRunning = _running;
    }

    if (!isRunning) { return; }

    if (Config.LoggingPlayerEvents) {
      Console.WriteLine($"Player joined: {peer.Address}:{peer.Port} " +
                      $"({_netManager.ConnectedPeersCount}/{Config.ServerMaxPlayers})");
    }

    ServerEvent?.Invoke("PlayerJoined", peer, $"Player joined from {peer.Address}:{peer.Port}");
  }

  private void OnPlayerLeft(NetPeer peer, DisconnectInfo info) {
    bool isRunning;
    lock (_runningLock) {
      isRunning = _running;
    }

    if (!isRunning) { return; }

    string reason = info.Reason switch {
      DisconnectReason.Timeout => "Connection timeout",
      DisconnectReason.RemoteConnectionClose => "Client disconnected",
      DisconnectReason.DisconnectPeerCalled => "Disconnected by server",
      DisconnectReason.ConnectionFailed => "Connection failed",
      DisconnectReason.NetworkUnreachable => "Network error",
      _ => info.Reason.ToString()
    };

    if (Config.LoggingPlayerEvents) {
      Console.WriteLine($"Player left: {peer.Address}:{peer.Port} - {reason} " +
                      $"({_netManager.ConnectedPeersCount}/{Config.ServerMaxPlayers})");
    }

    ServerEvent?.Invoke("PlayerLeft", peer, reason);
  }

  private void OnMessageReceived(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method) {
    try {
      string msg = reader.GetString();
      Log($"Received: {msg}");
      BroadcastMessage(msg, peer);
    } catch (Exception ex) {
      Log($"Error processing message: {ex.Message}");
    } finally {
      reader.Recycle();
    }
  }

  private void ServerLoop() {
    if (Config.NetworkTickRate <= 0) {
      Console.WriteLine($"Invalid NetworkTickRate: {Config.NetworkTickRate}");
      return;
    }

    TimeSpan tickInterval = TimeSpan.FromMilliseconds(1000.0 / Config.NetworkTickRate);
    DateTime lastTick = DateTime.UtcNow;

    while (true) {
      bool shouldRun;
      lock (_runningLock) {
        shouldRun = _running;
      }

      if (!shouldRun) { break; }
      _netManager.PollEvents();

      DateTime now = DateTime.UtcNow;
      if (now - lastTick >= tickInterval) {
        if (Config.LoggingEnableTick) {
          Console.WriteLine($"Server tick at {DateTime.Now:HH:mm:ss}");
        }

        if (Config.NetworkEnableHeartbeat) {
          SendHeartbeat();
        }

        lastTick = now;
      }

      Thread.Sleep(1);
    }
  }

  private void SendHeartbeat() {
    lock (_writer) {
      _writer.Reset();
      _writer.Put("heartbeat");
      _writer.Put(DateTime.Now.ToString("HH:mm:ss"));

      foreach (NetPeer peer in _netManager.ConnectedPeerList) {
        peer.Send(_writer, DeliveryMethod.Unreliable);
      }
    }
  }

  private void BroadcastMessage(string msg, NetPeer? exclude = null) {
    lock (_writer) {
      _writer.Reset();
      _writer.Put(msg);

      foreach (NetPeer peer in _netManager.ConnectedPeerList) {
        if (peer != exclude) {
          peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }
      }
    }
  }

  private void SendMessage(NetPeer peer, string msg) {
    lock (_writer) {
      _writer.Reset();
      _writer.Put(msg);
      peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
  }

  private void Log(string message) {
    if (Config.LoggingEnableConsole) {
      Console.WriteLine(message);
    }
  }
}
