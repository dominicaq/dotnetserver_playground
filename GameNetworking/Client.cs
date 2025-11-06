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
            NatPunchEnabled = false
        };

        _listener.PeerConnectedEvent += OnConnectedToServer;
        _listener.PeerDisconnectedEvent += OnDisconnectedFromServer;
        _listener.NetworkReceiveEvent += OnMessageReceived;
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessageReceived;
        _listener.ConnectionRequestEvent += OnConnectionRequest;

        _netManager.Start();

        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
            $"Client initialized on local port {_netManager.LocalPort}");
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

            ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
                $"Connecting to {serverEndpoint}...");

            var parts = serverEndpoint.Split(':');
            if (parts.Length != 2) {
                throw new ArgumentException("Invalid endpoint format. Expected: IP:PORT");
            }

            var serverIP = parts[0];
            var serverPort = int.Parse(parts[1]);
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);

            await StartHolePunching(remoteEndPoint);
        } catch (Exception ex) {
            lock (_connectionLock) {
                IsConnecting = false;
            }
            ClientEvent?.Invoke(PeerEvent.NetworkError, null, $"Connection failed: {ex.Message}");
        }
    }

    private async Task<string> DiscoverPublicEndpoint() {
        var localPort = _netManager!.LocalPort;
        var publicIP = await NetworkUtils.GetPublicIPAddress();
        return $"{publicIP}:{localPort}";
    }

    private async Task StartHolePunching(IPEndPoint serverEndPoint) {
        _punchCts = new CancellationTokenSource();

        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, "Starting hole punch...");

        // Start connection attempt early
        var connectionAttempted = false;

        try {
            for (int i = 0; i < 100 && !_punchCts.Token.IsCancellationRequested && _serverPeer == null; i++) {
                var writer = new NetDataWriter();
                writer.Put("PUNCH");

                _netManager!.SendUnconnectedMessage(writer, serverEndPoint);

                // ADDED: Try to connect after receiving first punch ack (around attempt 3-5)
                if (i >= 3 && !connectionAttempted) {
                    ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, "Attempting connection...");
                    _netManager.Connect(serverEndPoint, "");
                    connectionAttempted = true;
                }

                if (i % 10 == 0) {
                    ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Punch attempt {i}/100...");
                }

                _netManager.PollEvents();

                await Task.Delay(100, _punchCts.Token);
            }

            // Fallback: try to connect if we somehow didn't already
            if (!connectionAttempted && !_punchCts.Token.IsCancellationRequested && _serverPeer == null) {
                ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, "Attempting connection...");
                _netManager!.Connect(serverEndPoint, "");
            }
        } catch (OperationCanceledException) {
            ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, "Hole punching stopped");
        }
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
            if (reader.AvailableBytes > 0) {
                var message = reader.GetString();

                if (message == "PUNCH_ACK") {
                    ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
                        $"Received punch ack from {remoteEndPoint}");

                    if (_serverPeer == null && IsConnecting) {
                        var writer = new NetDataWriter();
                        writer.Put("");
                        _netManager!.Connect(remoteEndPoint, writer);
                    }
                } else {
                    ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
                        $"Unconnected message from {remoteEndPoint}");
                }
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
