using System.Net;
using System.Net.Sockets;

namespace GameNetworking;

public static class NetworkUtils
{
    private static readonly string[] _publicIpServices = [
        "https://checkip.amazonaws.com",
        "https://api.ipify.org",
        "https://icanhazip.com"
    ];

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
