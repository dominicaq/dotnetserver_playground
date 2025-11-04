using LiteNetLib;
using LiteNetLib.Utils;

namespace GameNetworking;

public class Server(ServerConfig config) {
    public ServerConfig Config { get; private set; } = config;
    public event Action<PeerEvent, NetPeer, object?>? ServerEvent;
    public bool IsRunning { get; private set; }

    private int _tickInterval;
    private readonly Lock _runningLock = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private EventBasedNetListener? _listener;
    private NetManager? _netManager;
    private Thread? _serverThread;

    public void Start() {
        lock (_runningLock) {
            if (IsRunning) { return; }

            _listener = new();
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPlayerJoined;
            _listener.PeerDisconnectedEvent += OnPlayerLeft;
            _listener.NetworkReceiveEvent += OnMessageReceived;

            _netManager = new(_listener);
            _netManager.Start(Config.ServerPort);

            _tickInterval = 1000 / Config.NetworkTickRate;
            _serverThread = new Thread(ServerLoop) { IsBackground = true };
            _serverThread.Start();

            IsRunning = true;
        }
    }

    private void ServerLoop() {
        while (!_cancellationTokenSource.Token.IsCancellationRequested) {
            _netManager?.PollEvents();
            Thread.Sleep(_tickInterval);
        }
    }

    public void Stop() {
        lock (_runningLock) {
            if (!IsRunning) { return; }

            IsRunning = false;
            _cancellationTokenSource.Cancel();
            _serverThread?.Join(TimeSpan.FromSeconds(5));

            _netManager?.Stop();
            _netManager = null;
            _listener = null;
            _serverThread = null;
        }
    }

    // -------------------------------------------------------------------------
    // Peer events
    // -------------------------------------------------------------------------
    private void OnMessageReceived(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        // TODO: not done
        try {
            if (reader.AvailableBytes == 0) {
                return;
            }

            // Read the message - assuming you'll have some kind of message structure
            // You might want to read a message type first, then the actual data
            var messageData = reader.GetRemainingBytes();

            ServerEvent?.Invoke(PeerEvent.MessageReceived, peer, messageData);
        } catch (Exception ex) {
            // Log the exception or handle it appropriately
            // For now, just invoke an error event if you want
            ServerEvent?.Invoke(PeerEvent.NetworkError, peer, ex);
        } finally {
            reader.Recycle();
        }
    }

    private void OnConnectionRequest(ConnectionRequest request) {
        if (_netManager?.ConnectedPeersCount >= Config.ServerMaxPlayers) {
            request.Reject();
            return;
        }

        string connectionKey = request.Data?.GetString() ?? string.Empty;
        if (connectionKey.Equals(Config.ServerConnectionKey)) {
            request.Accept();
        } else {
            request.Reject();
        }
    }

    private void OnPlayerJoined(NetPeer peer) => ServerEvent?.Invoke(PeerEvent.Connected, peer, null);
    private void OnPlayerLeft(NetPeer peer, DisconnectInfo info) => ServerEvent?.Invoke(PeerEvent.Disconnected, peer, info);

    // -------------------------------------------------------------------------
    // Data sending
    // -------------------------------------------------------------------------
    // Peer sending
    public void SendToPeerReliable(NetPeer peer, byte[] data) {
        if (!ValidateServer()) { return; }
        peer.Send(data, DeliveryMethod.ReliableOrdered);
    }

    public void SendToPeerReliable(NetPeer peer, NetDataWriter writer) {
        if (!ValidateServer()) { return; }
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    public void SendToPeerUnreliable(NetPeer peer, byte[] data) {
        if (!ValidateServer()) { return; }
        peer.Send(data, DeliveryMethod.Unreliable);
    }

    public void SendToPeerUnreliable(NetPeer peer, NetDataWriter writer) {
        if (!ValidateServer()) { return; }
        peer.Send(writer, DeliveryMethod.Unreliable);
    }

    // Server broadcasting
    public void BroadcastToAllReliable(byte[] data) {
        if (!ValidateServer()) { return; }
        _netManager!.SendToAll(data, DeliveryMethod.ReliableOrdered);
    }

    public void BroadcastToAllReliable(NetDataWriter writer) {
        if (!ValidateServer()) { return; }
        _netManager!.SendToAll(writer, DeliveryMethod.ReliableOrdered);
    }

    public void BroadcastToAllUnreliable(byte[] data) {
        if (!ValidateServer()) { return; }
        _netManager!.SendToAll(data, DeliveryMethod.Unreliable);
    }

    public void BroadcastToAllUnreliable(NetDataWriter writer) {
        if (!ValidateServer()) { return; }
        _netManager!.SendToAll(writer, DeliveryMethod.Unreliable);
    }

    // -------------------------------------------------------------------------
    // Server util
    // -------------------------------------------------------------------------
    public List<NetPeer> GetConnectedPeers() {
        if (!ValidateServer()) { return []; }
        return [.. _netManager!.ConnectedPeerList];
    }

    public int GetConnectedPeerCount() {
        if (!ValidateServer()) { return 0; }
        return _netManager!.ConnectedPeersCount;
    }

    public NetPeer? GetPeerById(int peerId) {
        if (!ValidateServer()) { return null; }
        return _netManager!.GetPeerById(peerId);
    }

    public int GetPeerLatency(NetPeer peer) {
        if (!ValidateServer()) { return -1; }
        return peer.Ping;
    }

    public void DisconnectPeer(NetPeer peer, string reason = "Disconnected by server") {
        if (!ValidateServer()) { return; }
        peer.Disconnect(System.Text.Encoding.UTF8.GetBytes(reason));
    }

    // -------------------------------------------------------------------------
    // Misc util
    // -------------------------------------------------------------------------
    private bool ValidateServer() => _netManager != null && IsRunning;
}
