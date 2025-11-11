using System;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GameNetworking;

public class Client {
    public event Action<PeerEvent, NetPeer?, object?>? ClientEvent;
    public bool IsConnected { get; private set; }
    public bool IsConnecting { get; private set; }
    public string? MyPublicEndpoint { get; private set; }

    private readonly Lock _connectionLock = new();
    private EventBasedNetListener? _listener;
    private NetManager? _netManager;
    private NetPeer? _serverPeer;
    private CancellationTokenSource? _punchCts;

    public void Init() {
        _listener = new EventBasedNetListener();

        _netManager = new NetManager(_listener) {
            UnconnectedMessagesEnabled = true,
            NatPunchEnabled = true
        };

        _listener.PeerConnectedEvent += OnConnectedToServer;
        _listener.PeerDisconnectedEvent += OnDisconnectedFromServer;
        _listener.NetworkReceiveEvent += OnMessageReceived;
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessageReceived;
        _listener.ConnectionRequestEvent += OnConnectionRequest;

        _netManager.Start();

        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Client initialized on local port {_netManager.LocalPort}");
    }

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------
    public async Task Connect(string serverEndpoint) {
        lock (_connectionLock) {
            if (IsConnected || IsConnecting) {
                ClientEvent?.Invoke(PeerEvent.NetworkError, null, "Already connected or connecting");
                return;
            }
            IsConnecting = true;
        }

        try {
            MyPublicEndpoint = await DiscoverPublicEndpoint();

            ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Connecting to {serverEndpoint}...");

            var parts = serverEndpoint.Split(':');
            if (parts.Length != 2) {
                ClientEvent?.Invoke(PeerEvent.NetworkError, null,
                    "Invalid endpoint format. Expected: IP:PORT");
                lock (_connectionLock) { IsConnecting = false; }
                return;
            }

            var serverIP = IPAddress.Parse(parts[0]);
            var serverPort = int.Parse(parts[1]);
            var remoteEndPoint = new IPEndPoint(serverIP, serverPort);

            if (NetworkUtils.IsLanIP(serverIP)) {
                ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, "Detected LAN IP — connecting directly...");
                _serverPeer = _netManager!.Connect(remoteEndPoint, "");
            } else {
                await StartHolePunching(remoteEndPoint);
            }
        } catch (Exception ex) {
            lock (_connectionLock) { IsConnecting = false; }
            ClientEvent?.Invoke(PeerEvent.NetworkError, null, $"Connection failed: {ex.Message}");
        }
    }

    private async Task<string> DiscoverPublicEndpoint() {
        var localPort = _netManager!.LocalPort;
        var publicIP = await NetworkUtils.GetPublicIPAddress();
        return $"{publicIP}:{localPort}";
    }

    public void Update() => _netManager?.PollEvents();

    public void Disconnect() {
        lock (_connectionLock) {
            if (!IsConnected && !IsConnecting) {
                return;
            }

            IsConnected = false;
            IsConnecting = false;
            _punchCts?.Cancel();
            _serverPeer?.Disconnect();
            _serverPeer = null;
        }
    }

    // -------------------------------------------------------------------------
    // NAT Hole Punching
    // -------------------------------------------------------------------------
    private async Task StartHolePunching(IPEndPoint serverEndPoint) {
        _punchCts = new CancellationTokenSource();
        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, "Starting hole punch...");

        try {
            for (int i = 0; i < 100 && !_punchCts.Token.IsCancellationRequested && _serverPeer == null; i++) {
                // Send punch packet
                var writer = new NetDataWriter();
                writer.Put("PUNCH");
                _netManager!.SendUnconnectedMessage(writer, serverEndPoint);

                if (i % 10 == 0) {
                    ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Punch attempt {i}/100...");
                }

                _netManager.PollEvents();
                await Task.Delay(100, _punchCts.Token);
            }

            // If hole punching loop exits without connection, it failed
            if (_serverPeer == null && !_punchCts.Token.IsCancellationRequested) {
                ClientEvent?.Invoke(PeerEvent.NetworkError, null, "Hole punch timeout - connection failed");
                lock (_connectionLock) { IsConnecting = false; }
            }
        } catch (OperationCanceledException) {
            ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, "Hole punching stopped");
        }
    }

    private void HandlePunchAck(IPEndPoint remoteEndPoint) {
        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
            $"Received PUNCH_ACK from {remoteEndPoint} - attempting connection...");

        // Only try to connect once when we get the first ACK
        if (_serverPeer == null && IsConnecting) {
            _netManager!.Connect(remoteEndPoint, "");
        }
    }

    // -------------------------------------------------------------------------
    // Peer events
    // -------------------------------------------------------------------------
    private void OnMessageReceived(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        if (peer != _serverPeer) { return; }

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

    private void OnUnconnectedMessageReceived(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        try {
            if (reader.AvailableBytes == 0) {
                return;
            }

            var message = reader.GetString();

            if (message == "PUNCH_ACK") {
                HandlePunchAck(remoteEndPoint);
            } else {
                ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
                    $"Unconnected message from {remoteEndPoint}: {message}");
            }
        } catch (Exception ex) {
            ClientEvent?.Invoke(PeerEvent.NetworkError, null,
                $"Unconnected message error: {ex.Message}");
        } finally {
            reader.Recycle();
        }
    }

    private void OnConnectionRequest(ConnectionRequest request) {
        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
            $"Connection request from {request.RemoteEndPoint}");
        request.Accept();
    }

    private void OnConnectedToServer(NetPeer peer) {
        if (_serverPeer != null) {
            peer.Disconnect();
            return;
        }

        lock (_connectionLock) {
            IsConnected = true;
            IsConnecting = false;
            _serverPeer = peer;
        }

        _punchCts?.Cancel();

        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
            $"Connected to server at {peer.Address}:{peer.Port}");
        ClientEvent?.Invoke(PeerEvent.Connected, peer, null);
    }

    private void OnDisconnectedFromServer(NetPeer peer, DisconnectInfo info) {
        if (peer == _serverPeer) {
            lock (_connectionLock) {
                IsConnected = false;
                IsConnecting = false;
                _serverPeer = null;
            }

            ClientEvent?.Invoke(PeerEvent.Disconnected, peer, info);
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
