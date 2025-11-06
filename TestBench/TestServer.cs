using GameNetworking;
using LiteNetLib;
using LiteNetLib.Utils;

namespace TestBench;

public class TestServer {
    public static async Task Run(string[] args) {
        ServerConfig config = ServerConfig.LoadFromFile("server_config.json");
        Server server = new(config);
        server.ServerEvent += OnServerEvent;

        await server.Start();

        Console.WriteLine("Controls: 'q' = quit | 'b' = broadcast test message");

        bool running = true;
        while (running) {
            ConsoleKeyInfo key = Console.ReadKey(true);
            switch (key.KeyChar) {
                case 'q':
                case 'Q':
                    running = false;
                    break;
                case 'b':
                case 'B':
                    BroadcastTestMessage(server);
                    break;
            }
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
            default:
                string message = data?.ToString() ?? "No data";
                Console.WriteLine($"[{timestamp}] {eventType} {peerInfo}: {message}");
                break;
        }

        Console.ForegroundColor = originalColor;
    }
}
