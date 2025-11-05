using LiteNetLib;
using LiteNetLib.Utils;
using Open.Nat;

namespace GameNetworking;

public class Server(ServerConfig config) {
    public ServerConfig Config { get; private set; } = config;
    public event Action<PeerEvent, NetPeer?, object?>? ServerEvent;
    public bool IsRunning { get; private set; }

    private int _tickInterval;
    private readonly Lock _runningLock = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private EventBasedNetListener? _listener;
    private NetManager? _netManager;
    private Thread? _serverThread;

    private bool ValidateServer() => _netManager != null && IsRunning;

    public async Task Start() {
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

        await SetupUPnP();
    }

    private void ServerLoop() {
        while (!_cancellationTokenSource.Token.IsCancellationRequested) {
            _netManager?.PollEvents();
            Thread.Sleep(_tickInterval);
        }
    }

    public async Task Stop() {
        await CleanupUPnP();

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
    public void SendToPeer(NetPeer peer, NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered) {
        if (!ValidateServer()) { return; }
        peer.Send(writer, method);
    }

    public void BroadcastToAll(NetDataWriter writer, DeliveryMethod method = DeliveryMethod.ReliableOrdered) {
        if (!ValidateServer()) { return; }
        _netManager!.SendToAll(writer, method);
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
    // UPnP
    // -------------------------------------------------------------------------
    private NatDevice? _natDevice;
    private Mapping? _portMapping;

    private async Task SetupUPnP() {
        try {
            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, "Attempting UPnP port forwarding...");

            var discoverer = new NatDiscoverer();
            var cts = new CancellationTokenSource(5000); // 5 seconds

            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, "Discovering NAT device...");
            _natDevice = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp | PortMapper.Pmp, cts);

            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Found NAT device: {_natDevice.GetType().Name}");

            _portMapping = new Mapping(
                Protocol.Udp,
                Config.ServerPort,
                Config.ServerPort,
                "Game Server"
            );

            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Creating port mapping for UDP {Config.ServerPort}...");
            await _natDevice.CreatePortMapAsync(_portMapping);

            var externalIP = await _natDevice.GetExternalIPAsync();
            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, $"UPnP Success - External IP: {externalIP}:{Config.ServerPort}");

        } catch (NatDeviceNotFoundException ex) {
            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, $"UPnP not available: {ex.Message}");
        } catch (MappingException ex) {
            ServerEvent?.Invoke(PeerEvent.NetworkError, null, $"UPnP port mapping failed: {ex.Message}");
        } catch (OperationCanceledException) {
            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, "UPnP discovery timed out");
        } catch (Exception ex) {
            ServerEvent?.Invoke(PeerEvent.NetworkError, null, $"UPnP error: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private async Task CleanupUPnP() {
        if (_natDevice != null && _portMapping != null) {
            try {
                await _natDevice.DeletePortMapAsync(_portMapping);
                ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, "UPnP port mapping removed");
            } catch {
                // Ignore cleanup errors
            }
        }
    }
}
