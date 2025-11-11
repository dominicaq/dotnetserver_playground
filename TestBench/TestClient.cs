using GameNetworking;
using LiteNetLib;
using LiteNetLib.Utils;

namespace TestBench;

public class TestClient {
    private static Client? _client;

    public static async Task Run(string[] args) {
        _client = new Client();
        _client.Init();
        _client.ClientEvent += OnClientEvent;

        Console.Write("Enter server code: ");
        string? serverCode = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(serverCode)) {
            return;
        }

        await _client.Connect(serverCode);

        Console.WriteLine("Controls: 'q' = quit | 's' = send test message");

        bool running = true;
        while (running) {
            _client.Update();

            if (Console.KeyAvailable) {
                var key = Console.ReadKey(true);
                switch (key.KeyChar) {
                    case 'q':
                    case 'Q':
                        running = false;
                        break;
                    case 's':
                    case 'S':
                        SendTestMessage();
                        break;
                }
            }

            Thread.Sleep(15);
        }

        _client.Disconnect();
    }

    private static void SendTestMessage() {
        if (_client?.IsConnected != true) {
            return;
        }

        var writer = new NetDataWriter();
        writer.Put("Hello from client!");
        _client.SendToServer(writer);

        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"[{timestamp}] Sent message to server");
    }

    private static void OnClientEvent(PeerEvent eventType, NetPeer? peer, object? data) {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

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
            case PeerEvent.Connected:
                Console.WriteLine($"[{timestamp}] Connected to server");
                break;

            case PeerEvent.Disconnected:
                Console.WriteLine($"[{timestamp}] Disconnected from server");
                break;

            case PeerEvent.MessageReceived:
                var messageData = (byte[]?)data;
                if (messageData != null && messageData.Length > 0) {
                    var reader = new NetDataReader(messageData);
                    var message = reader.GetString();
                    Console.WriteLine($"[{timestamp}] Received: {message}");
                }
                break;

            case PeerEvent.NetworkError:
                Console.WriteLine($"[{timestamp}] {data}");
                break;

            case PeerEvent.NetworkInfo:
                Console.WriteLine($"[{timestamp}] {data}");
                break;
        }

        Console.ForegroundColor = originalColor;
    }
}
