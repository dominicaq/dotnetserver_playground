using System;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GameNetworking;

public class Client {
    public event Action<PeerEvent, NetPeer?, object?>? ClientEvent;
    public bool IsConnected { get; private set; }
    public bool IsConnecting { get; private set; }

    private readonly Lock _connectionLock = new();
    private EventBasedNetListener? _listener;
    private EventBasedNatPunchListener? _natListener;
    private NetManager? _netManager;
    private NetPeer? _serverPeer;

    public void Init() {
        _listener = new EventBasedNetListener();
        _natListener = new EventBasedNatPunchListener();

        _netManager = new NetManager(_listener) {
            NatPunchEnabled = true
        };

        // Attach the NAT listener
        _netManager.NatPunchModule.Init(_natListener);

        _natListener.NatIntroductionSuccess += OnNatIntroductionSuccess;
        _natListener.NatIntroductionRequest += OnNatIntroductionRequest;

        _listener.PeerConnectedEvent += OnConnectedToServer;
        _listener.PeerDisconnectedEvent += OnDisconnectedFromServer;
        _listener.NetworkReceiveEvent += OnMessageReceived;
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessageReceived;

        _netManager.Start();
    }

    // -------------------------------------------------------------------------
    // NAT punchthrough callbacks
    // -------------------------------------------------------------------------
    private void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token) {
        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"NAT punch successful to {targetEndPoint} (token: {token})");
        _netManager?.Connect(targetEndPoint, token);
    }

    private void OnNatIntroductionRequest(System.Net.IPEndPoint localEndPoint, System.Net.IPEndPoint remoteEndPoint, string token) {
        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Received NAT introduction request from {remoteEndPoint} with token {token}");
    }

    // -------------------------------------------------------------------------
    // Default Connect (tries NAT punch automatically)
    // -------------------------------------------------------------------------
    public void Connect(string serverAddress, int serverPort, string connectionKey = "") {
        lock (_connectionLock) {
            if (IsConnected || IsConnecting || _netManager == null) {
                return;
            }

            IsConnecting = true;
            ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Attempting NAT punchthrough via {serverAddress}:{serverPort}...");

            _netManager.NatPunchModule.SendNatIntroduceRequest(
                serverAddress,
                serverPort,
                connectionKey
            );
        }
    }

    public void Update() {
        _netManager?.PollEvents();
    }

    public void Disconnect() {
        lock (_connectionLock) {
            if (!IsConnected && !IsConnecting) {
                return;
            }

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
        if (!IsValidConnection()) {
            return;
        }
        _serverPeer!.Send(writer, method);
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------
    public NetPeer? GetServerPeer() => _serverPeer;

    public int GetServerLatency() =>
        _serverPeer == null || !IsConnected ? -1 : _serverPeer.Ping;

    public bool IsValidConnection() =>
        _netManager != null && IsConnected && _serverPeer != null;
}
