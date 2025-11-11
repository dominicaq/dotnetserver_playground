using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GameNetworking;

public static class NetworkUtils {
    private static readonly string[] _publicIpServices = [
        "https://checkip.amazonaws.com",
        "https://api.ipify.org",
        "https://icanhazip.com"
    ];

    // -------------------------------------------------------------------------
    // IP Address Discovery
    // -------------------------------------------------------------------------
    public static async Task<string> GetPublicIPAddress() {
        try {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            foreach (var service in _publicIpServices) {
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

    public static string GetLocalIPAddress() {
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

    public static bool IsLanIP(IPAddress ip) {
        byte[] bytes = ip.GetAddressBytes();

        // 10.0.0.0/8 - Class A private network
        if (bytes[0] == 10) {
            return true;
        }

        // 172.16.0.0/12 - Class B private network
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) {
            return true;
        }

        // 192.168.0.0/16 - Class C private network
        if (bytes[0] == 192 && bytes[1] == 168) {
            return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Connection Detection
    // -------------------------------------------------------------------------
    public enum ConnectionType {
        Localhost,
        LAN,
        Internet
    }

    public static ConnectionType DetectConnectionType(IPAddress targetIP, string? localIP = null, string? publicIP = null) {
        if (IsLoopback(targetIP)) {
            return ConnectionType.Localhost;
        }

        if (!string.IsNullOrEmpty(localIP) && targetIP.ToString() == localIP) {
            return ConnectionType.Localhost;
        }

        if (IsLanIP(targetIP)) {
            return ConnectionType.LAN;
        }

        if (!string.IsNullOrEmpty(publicIP) && targetIP.ToString() == publicIP) {
            return ConnectionType.LAN;
        }

        return ConnectionType.Internet;
    }

    private static bool IsLoopback(IPAddress ip) {
        return IPAddress.IsLoopback(ip) ||
               ip.Equals(IPAddress.Parse("127.0.0.1")) ||
               ip.Equals(IPAddress.Parse("::1"));
    }

    // -------------------------------------------------------------------------
    // Endpoint Selection
    // -------------------------------------------------------------------------
    public static async Task<string> SelectBestEndpoint(string encryptedCode) {
        var serverEndpoint = DecryptServerCode(encryptedCode);
        var parts = serverEndpoint.Split('|').Select(e => e.Trim()).ToArray();

        if (parts.Length != 3) {
            return parts.Length > 1 ? $"{parts[1]}:{parts[0]}" : parts[0];
        }

        var port = parts[0];
        var lanIP = parts[1];
        var internetIP = parts[2];

        var localIP = GetLocalIPAddress();
        var publicIP = await GetPublicIPAddress();

        // Check if same machine
        if (lanIP == localIP) {
            return $"127.0.0.1:{port}";
        }

        // Check if same network
        if (IPAddress.TryParse(internetIP, out var serverPublicIP)) {
            var connectionType = DetectConnectionType(serverPublicIP, localIP, publicIP);

            if (connectionType != ConnectionType.Internet) {
                return $"{lanIP}:{port}";
            }
        }

        return $"{internetIP}:{port}";
    }

    // -------------------------------------------------------------------------
    // Server Code Encryption/Decryption
    // -------------------------------------------------------------------------
    private const string _encryptionKey = "GameServer2024";

    public static string EncryptServerCode(string serverCode) {
        var bytes = Encoding.UTF8.GetBytes(serverCode);
        var keyBytes = Encoding.UTF8.GetBytes(_encryptionKey);

        // XOR encryption
        for (int i = 0; i < bytes.Length; i++) {
            bytes[i] ^= keyBytes[i % keyBytes.Length];
        }

        return Convert.ToBase64String(bytes);
    }

    public static string DecryptServerCode(string encryptedCode) {
        var bytes = Convert.FromBase64String(encryptedCode);
        var keyBytes = Encoding.UTF8.GetBytes(_encryptionKey);

        // XOR decryption (same as encryption)
        for (int i = 0; i < bytes.Length; i++) {
            bytes[i] ^= keyBytes[i % keyBytes.Length];
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
