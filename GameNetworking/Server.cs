using LiteNetLib;
using LiteNetLib.Utils;
using Open.Nat;
using System.Net;
using System.Net.Sockets;

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
            if (IsRunning) return;

            _listener = new EventBasedNetListener();
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPlayerJoined;
            _listener.PeerDisconnectedEvent += OnPlayerLeft;
            _listener.NetworkReceiveEvent += OnMessageReceived;
            _listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessageReceived;

            _netManager = new NetManager(_listener) {
                NatPunchEnabled = true
            };

            _netManager.Start(Config.ServerPort);
            _tickInterval = 1000 / Config.NetworkTickRate;

            _serverThread = new Thread(ServerLoop) { IsBackground = true };
            _serverThread.Start();

            IsRunning = true;
        }

        // Step 1: Try hole punching first
        bool punchSuccess = await TryHolePunch();

        // Step 2: If hole punching fails, fallback to UPnP
        if (!punchSuccess) {
            await SetupUPnP();
        }
    }

    private async Task<bool> TryHolePunch() {
        try {
            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, "Attempting NAT hole punch test...");

            string publicIP = await GetPublicIPAddress();
            string privateIP = GetLocalIPAddress();

            if (publicIP == "Unable to determine") {
                ServerEvent?.Invoke(PeerEvent.NetworkError, null, "Could not determine public IP. Hole punching not possible.");
                return false;
            }

            using var udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = 3000;

            var target = new IPEndPoint(IPAddress.Parse(publicIP), Config.ServerPort);
            byte[] testData = System.Text.Encoding.ASCII.GetBytes("NAT_TEST");
            await udpClient.SendAsync(testData, testData.Length, target);

            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Hole punch test packet sent to {publicIP}:{Config.ServerPort}");
            return true;
        } catch (Exception ex) {
            ServerEvent?.Invoke(PeerEvent.NetworkError, null, $"Hole punch test failed: {ex.Message}");
            return false;
        }
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
        try {
            if (reader.AvailableBytes == 0) {
                return;
            }

            var messageData = reader.GetRemainingBytes();
            ServerEvent?.Invoke(PeerEvent.MessageReceived, peer, messageData);
        } catch (Exception ex) {
            ServerEvent?.Invoke(PeerEvent.NetworkError, peer, ex);
        } finally {
            reader.Recycle();
        }
    }

    private void OnUnconnectedMessageReceived(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        try {
            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null, $"Unconnected message from {remoteEndPoint}");
        } catch (Exception ex) {
            ServerEvent?.Invoke(PeerEvent.NetworkError, null, $"Unconnected message error: {ex.Message}");
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

    private void OnPlayerJoined(NetPeer peer) {
        string clientIP = peer.Address.ToString();
        bool isLan = IsLanIP(peer.Address);

        ServerEvent?.Invoke(PeerEvent.Connected, peer,
            $"Client connected from {clientIP} ({(isLan ? "LAN" : "External")})");
    }

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
            using var cts = new CancellationTokenSource(5000);

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

            string publicIP = await GetPublicIPAddress();
            string privateIP = GetLocalIPAddress();
            ServerEvent?.Invoke(PeerEvent.NetworkInfo, null,
                $"UPnP Success — Private IP: {privateIP}:{Config.ServerPort}, Public IP: {publicIP}:{Config.ServerPort}");
        } catch (Exception ex) {
            ServerEvent?.Invoke(PeerEvent.NetworkError, null, $"UPnP setup failed: {ex.Message}");
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

    // -------------------------------------------------------------------------
    // IP helpers
    // -------------------------------------------------------------------------
    public string LocalIP => GetLocalIPAddress();
    public Task<string> PublicIP => GetPublicIPAddress();

    private static string GetLocalIPAddress() {
        try {
            foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) {
                    continue;
                }


                var props = iface.GetIPProperties();
                foreach (var addr in props.UnicastAddresses) {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address)) {
                        return addr.Address.ToString();
                    }
                }
            }
            return "127.0.0.1";
        } catch {
            return "Unable to determine";
        }
    }

    private static async Task<string> GetPublicIPAddress() {
        try {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            string[] services = {
                "https://checkip.amazonaws.com",
                "https://api.ipify.org",
                "https://icanhazip.com"
            };

            foreach (var service in services) {
                try {
                    string ip = await httpClient.GetStringAsync(service);
                    if (!string.IsNullOrWhiteSpace(ip)) {
                        return ip.Trim();
                    }
                } catch {
                    continue;
                }
            }

            return "Unable to determine";
        } catch {
            return "Unable to determine";
        }
    }

    private static bool IsLanIP(IPAddress ip) {
        byte[] bytes = ip.GetAddressBytes();

        if (bytes[0] == 10) {
            return true;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) {
            return true;
        }

        if (bytes[0] == 192 && bytes[1] == 168) {
            return true;
        }

        return false;
    }
}
