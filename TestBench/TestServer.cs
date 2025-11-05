using GameNetworking;
using LiteNetLib;
using System.Net;
using System.Net.Sockets;

namespace TestBench;

public class TestServer {
    public static async Task Run(string[] args) {
        Console.WriteLine("Starting Game Server Test Host...");

        ServerConfig config = ServerConfig.LoadFromFile("server_config.json");

        // Print connection information
        Console.WriteLine("\n=== Connection Information ===");
        Console.WriteLine($"Local IP: {GetLocalIPAddress()}");
        Console.WriteLine($"Port: {config.ServerPort}");
        Console.WriteLine("==============================\n");

        Server server = new(config);
        server.ServerEvent += OnServerEvent;
        await server.Start();

        Console.WriteLine("Press 'q' to quit, 's' to show server info");

        bool running = true;
        while (running) {
            ConsoleKeyInfo key = Console.ReadKey(true);
            switch (key.KeyChar) {
                case 'q':
                case 'Q':
                    running = false;
                    break;
                case 's':
                case 'S':
                    ShowServerInfo(server);
                    break;
            }
        }

        await server.Stop();
        Console.WriteLine("Server shutdown complete.");
    }

    private static string GetLocalIPAddress() {
        try {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList) {
                if (ip.AddressFamily == AddressFamily.InterNetwork) {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        } catch {
            return "Unable to determine";
        }
    }

    private static void OnServerEvent(PeerEvent eventType, NetPeer? peer, object? data) {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string peerInfo = peer != null ? $"[{peer.Address}:{peer.Port}]" : "[SERVER]";

        ConsoleColor originalColor = Console.ForegroundColor;
        Console.ForegroundColor = eventType switch {
            PeerEvent.Connected => ConsoleColor.Green,
            PeerEvent.Disconnected => ConsoleColor.Yellow,
            PeerEvent.NetworkError => ConsoleColor.Red,
            PeerEvent.MessageReceived => ConsoleColor.Cyan,
            PeerEvent.NetworkInfo => ConsoleColor.Blue,
            _ => ConsoleColor.White
        };

        string message = data?.ToString() ?? "No data";
        Console.WriteLine($"[{timestamp}] {eventType} {peerInfo}: {message}");

        Console.ForegroundColor = originalColor;
    }

    private static void ShowServerInfo(Server server) {
        Console.WriteLine("\n=== Server Info ===");
        Console.WriteLine($"Local IP: {GetLocalIPAddress()}");
        Console.WriteLine($"Server Name: {server.Config.ServerName}");
        Console.WriteLine($"Port: {server.Config.ServerPort}");
        Console.WriteLine($"Max Players: {server.Config.ServerMaxPlayers}");
        Console.WriteLine($"Connected Players: {server.GetConnectedPeerCount()}");
        Console.WriteLine($"Tick Rate: {server.Config.NetworkTickRate}");
        Console.WriteLine($"Game Mode: {server.Config.ServerGameMode}");
        Console.WriteLine($"Allow Cheats: {server.Config.ServerAllowCheats}");
        Console.WriteLine("===================\n");
    }
}
