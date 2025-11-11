using GameNetworking;
using LiteNetLib;
using LiteNetLib.Utils;

namespace TestBench;

public class TestServer {
    private static bool _running = true;

    public static async Task Run(string[] args) {
        ServerConfig config = ServerConfig.LoadFromFile("server_config.json");
        Server server = new(config);
        server.ServerEvent += OnServerEvent;

        await server.Start();

        Console.WriteLine("Controls: 'q' = quit | 'b' = broadcast test message");

        while (_running) {
            if (Console.KeyAvailable) {
                ConsoleKeyInfo key = Console.ReadKey(true);
                switch (key.KeyChar) {
                    case 'q':
                    case 'Q':
                        _running = false;
                        break;
                    case 'b':
                    case 'B':
                        BroadcastTestMessage(server);
                        break;
                }
            }
            // Prevent CPU spinning
            await Task.Delay(100);
        }

        await server.Stop();
    }

    private static void BroadcastTestMessage(Server server) {
        if (server.GetConnectedPeerCount() == 0) {
            return;
        }

        var writer = new NetDataWriter();
        writer.Put("Hello from server!");
        server.BroadcastToAll(writer);

        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"[{timestamp}] Broadcasted to {server.GetConnectedPeerCount()} client(s)");
    }

    private static void OnServerEvent(PeerEvent eventType, NetPeer? peer, object? data) {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
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

        switch (eventType) {
            case PeerEvent.MessageReceived:
                var messageData = (byte[]?)data;
                if (messageData != null && messageData.Length > 0) {
                    var reader = new NetDataReader(messageData);
                    var msg = reader.GetString();
                    Console.WriteLine($"[{timestamp}] {eventType} {peerInfo}: {msg}");
                }
                break;

            case PeerEvent.NetworkError:
                string errorMessage = data?.ToString() ?? "Unknown error";
                Console.WriteLine($"[{timestamp}] {eventType} {peerInfo}: {errorMessage}");
                if (IsCriticalError(errorMessage)) {
                    Console.WriteLine($"[{timestamp}] Critical error detected - shutting down server...");
                    _running = false;
                }
                break;

            default:
                string message = data?.ToString() ?? "No data";
                Console.WriteLine($"[{timestamp}] {eventType} {peerInfo}: {message}");
                break;
        }

        Console.ForegroundColor = originalColor;
    }

    private static bool IsCriticalError(string errorMessage) {
        // Define which errors should stop the server
        string[] criticalErrors = [
            "UPnP setup failed",
            "Failed to start",
            "Port already in use",
            "Cannot bind to port"
        ];

        return criticalErrors.Any(error =>
            errorMessage.Contains(error, StringComparison.OrdinalIgnoreCase));
    }
}
