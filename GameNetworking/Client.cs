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
    private NetManager? _netManager;
    private NetPeer? _serverPeer;
    private CancellationTokenSource? _connectionCts;
    private NatPunchModule? _natPunchModule;
    private EventBasedNatPunchListener? _natListener;

    public void Init() {
        _listener = new EventBasedNetListener();
        _natListener = new EventBasedNatPunchListener();

        _netManager = new NetManager(_listener) {
            UnconnectedMessagesEnabled = true,
            NatPunchEnabled = true
        };

        _listener.PeerConnectedEvent += OnConnectedToServer;
        _listener.PeerDisconnectedEvent += OnDisconnectedFromServer;
        _listener.NetworkReceiveEvent += OnMessageReceived;
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessageReceived;
        _listener.ConnectionRequestEvent += OnConnectionRequest;

        // Start client
        _netManager.Start();

        _netManager.NatPunchModule.Init(_natListener);

        // Subscribe to NAT events on the nat listener
        _natListener.NatIntroductionSuccess += OnNatIntroductionSuccess;
        _natListener.NatIntroductionRequest += OnNatIntroductionRequest;

        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Client initialized on local port {_netManager.LocalPort}");
    }

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------
    public async Task Connect(string serverCode) {
        lock (_connectionLock) {
            if (IsConnected || IsConnecting) {
                ClientEvent?.Invoke(PeerEvent.NetworkError, null, "Already connected or connecting");
                return;
            }
            IsConnecting = true;
        }

        try {
            if (_netManager == null) {
                ClientEvent?.Invoke(PeerEvent.NetworkError, null, "NetManager not initialized");
                lock (_connectionLock) { IsConnecting = false; }
                return;
            }

            string selectedEndpoint = await NetworkUtils.SelectBestEndpoint(serverCode);
            ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Connecting to {selectedEndpoint}...");

            var parts = selectedEndpoint.Split(':');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var serverIP) || !int.TryParse(parts[1], out var serverPort)) {
                ClientEvent?.Invoke(PeerEvent.NetworkError, null, "Invalid endpoint format");
                lock (_connectionLock) { IsConnecting = false; }
                return;
            }

            IPEndPoint remoteEndPoint = new(serverIP, serverPort);
            _connectionCts = new CancellationTokenSource();
            _netManager.Connect(remoteEndPoint, "");

            await WaitForConnection(10000);

            if (!IsConnected && IsConnecting) {
                lock (_connectionLock) { IsConnecting = false; }

                bool isLan = NetworkUtils.IsLanIP(serverIP);
                if (isLan) {
                    ClientEvent?.Invoke(PeerEvent.NetworkError, null, "Connection timeout");
                } else {
                    ClientEvent?.Invoke(PeerEvent.NetworkError, null,
                        "Connection timeout. If connecting over the internet, ensure the server has:\n" +
                        "1. UPnP enabled (Config.NetworkEnableUPnP = true), OR\n" +
                        "2. Manual port forwarding configured on the router");
                }
            }
        } catch (Exception ex) {
            lock (_connectionLock) { IsConnecting = false; }
            ClientEvent?.Invoke(PeerEvent.NetworkError, null, $"Connection failed: {ex.Message}");
        }
    }

    private async Task WaitForConnection(int timeoutMs) {
        if (_connectionCts == null) {
            return;
        }

        var startTime = DateTime.UtcNow;

        try {
            while (IsConnecting && !IsConnected &&
                   (DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs &&
                   !_connectionCts.Token.IsCancellationRequested) {
                _netManager?.PollEvents();
                await Task.Delay(100, _connectionCts.Token);
            }

            if (!IsConnected && IsConnecting && !_connectionCts.Token.IsCancellationRequested) {
                ClientEvent?.Invoke(PeerEvent.NetworkInfo, null, "Connection attempt timed out");
            }
        } catch (OperationCanceledException) {
            // ignore
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
            _connectionCts?.Cancel();
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
            if (reader.AvailableBytes == 0) {
                return;
            }

            var message = reader.GetString();
            ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
                $"Unconnected message from {remoteEndPoint}: {message}");
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

        _connectionCts?.Cancel();

        ClientEvent?.Invoke(PeerEvent.Connected, null,
            $"Connected to server at {peer.Address}:{peer.Port}");
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
    // NAT Punch-through events
    // -------------------------------------------------------------------------
    private void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token) {
        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
            $"NAT introduction successful! Target: {targetEndPoint}, Type: {type}, Token: {token}");

        // Attempt to connect to the target endpoint after successful NAT punch
        try {
            if (_netManager != null && !IsConnected && !IsConnecting) {
                lock (_connectionLock) {
                    IsConnecting = true;
                }

                _netManager.Connect(targetEndPoint, "");
                ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
                    $"Connecting to peer through NAT at {targetEndPoint}...");
            }
        } catch (Exception ex) {
            lock (_connectionLock) {
                IsConnecting = false;
            }
            ClientEvent?.Invoke(PeerEvent.NetworkError, null,
                $"Failed to connect after NAT introduction: {ex.Message}");
        }
    }

    private void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token) {
        ClientEvent?.Invoke(PeerEvent.NetworkInfo, null,
            $"NAT introduction request received. Local: {localEndPoint}, Remote: {remoteEndPoint}, Token: {token}");

        // This event is typically handled on a master/relay server
        // For a client, you usually just log it or ignore it
        // The actual NAT punch coordination happens on the server side
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
