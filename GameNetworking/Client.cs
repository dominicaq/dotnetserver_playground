using LiteNetLib;
using LiteNetLib.Utils;

namespace GameNetworking;

public class Client {
    public event Action<PeerEvent, NetPeer?, object?>? ClientEvent;
    public bool IsConnected { get; private set; }
    public bool IsConnecting { get; private set; }

    private readonly Lock _connectionLock = new();
    private EventBasedNetListener? _listener;
    private NetManager? _netManager;
    private NetPeer? _serverPeer;

    public void Init() {
        _listener = new();
        _listener.PeerConnectedEvent += OnConnectedToServer;
        _listener.PeerDisconnectedEvent += OnDisconnectedFromServer;
        _listener.NetworkReceiveEvent += OnMessageReceived;
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessageReceived;

        _netManager = new(_listener) {
            NatPunchEnabled = true
        };
        _netManager.Start();
    }

    public void Connect(string serverAddress, int serverPort, string connectionKey = "") {
        lock (_connectionLock) {
            if (IsConnected || IsConnecting || _netManager == null) { return; }

            var connectionData = new NetDataWriter();
            connectionData.Put(connectionKey);

            _netManager.Connect(serverAddress, serverPort, connectionData);
            IsConnecting = true;
        }
    }

    public void Update() {
        _netManager?.PollEvents();
    }

    public void Disconnect() {
        lock (_connectionLock) {
            if (!IsConnected && !IsConnecting) { return; }
            IsConnected = false;
            IsConnecting = false;
            _serverPeer?.Disconnect();
            _serverPeer = null;
        }
    }

    // -------------------------------------------------------------------------
    // Connection events
    // -------------------------------------------------------------------------
    private void OnConnectedToServer(NetPeer peer) {
        lock (_connectionLock) {
            IsConnected = true;
            IsConnecting = false;
            _serverPeer = peer;
        }
        ClientEvent?.Invoke(PeerEvent.Connected, peer, null);
    }

    private void OnDisconnectedFromServer(NetPeer peer, DisconnectInfo info) {
        lock (_connectionLock) {
            IsConnected = false;
            IsConnecting = false;
            _serverPeer = null;
        }
        ClientEvent?.Invoke(PeerEvent.Disconnected, peer, info);
    }

    private void OnMessageReceived(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        try {
            if (reader.AvailableBytes == 0) {
                return;
            }
            var messageData = reader.GetRemainingBytes();
            ClientEvent?.Invoke(PeerEvent.MessageReceived, peer, messageData);
        } catch (Exception ex) {
            ClientEvent?.Invoke(PeerEvent.NetworkError, peer, ex);
        } finally {
            reader.Recycle();
        }
    }

    private void OnUnconnectedMessageReceived(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        try {
            ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Unconnected message from {remoteEndPoint}");
        } catch (Exception ex) {
            ClientEvent?.Invoke(PeerEvent.NetworkError, null, $"Unconnected message error: {ex.Message}");
        } finally {
            reader.Recycle();
        }
    }

    // -------------------------------------------------------------------------
    // Data sending
    // -------------------------------------------------------------------------
    public void SendToServer(NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered) {
        if (!IsValidConnection()) { return; }
        _serverPeer!.Send(writer, method);
    }

    // -------------------------------------------------------------------------
    // NAT Hole Punching
    // -------------------------------------------------------------------------
    public void ConnectThroughNAT(string facilitatorAddress, int facilitatorPort, string targetToken) {
        if (_netManager == null) { return; }

        // Send NAT punch request through the facilitator server
        _netManager.NatPunchModule.SendNatIntroduceRequest(
            facilitatorAddress,
            facilitatorPort,
            targetToken
        );

        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"NAT punch request sent for token: {targetToken}");
    }

    // -------------------------------------------------------------------------
    // Client util
    // -------------------------------------------------------------------------
    public NetPeer? GetServerPeer() => _serverPeer;

    public int GetServerLatency() {
        if (_serverPeer == null || !IsConnected) { return -1; }
        return _serverPeer.Ping;
    }

    public bool IsValidConnection() => _netManager != null && IsConnected && _serverPeer != null;
}
