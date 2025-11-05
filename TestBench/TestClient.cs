using GameNetworking;
using LiteNetLib;

namespace TestBench;

public class TestClient {
    private static Client? _client;

    public static void Run(string[] args) {
        Console.WriteLine("Starting Test Client...");

        _client = new Client();
        _client.Init();

        _client.ClientEvent += OnClientEvent;

        ConnectToServer();

        Console.WriteLine("Press 'r' to reconnect, 'd' to disconnect, 'q' to quit");
        bool running = true;
        while (running) {
            _client.Update();

            if (Console.KeyAvailable) {
                ConsoleKeyInfo key = Console.ReadKey(true);
                switch (key.KeyChar) {
                    case 'q':
                    case 'Q':
                        running = false;
                        break;
                    case 'r':
                    case 'R':
                        ConnectToServer();
                        break;
                    case 'd':
                    case 'D':
                        DisconnectFromServer();
                        break;
                }
            }
            Thread.Sleep(15);
        }

        _client.Disconnect();
        Console.WriteLine("Client stopped.");
    }

    private static void ConnectToServer() {
        if (_client?.IsConnected == true) {
            Console.WriteLine("Already connected to server!");
            return;
        }

        _client?.Connect("127.0.0.1", 7777, "key");
        Console.WriteLine("Attempting to connect to server...");
    }

    private static void DisconnectFromServer() {
        if (_client?.IsConnected == true) {
            _client.Disconnect();
            Console.WriteLine("Disconnecting from server...");
        } else {
            Console.WriteLine("Not connected to server!");
        }
    }

    private static void OnClientEvent(PeerEvent eventType, NetPeer? peer, object? data) {
        switch (eventType) {
            case PeerEvent.Connected:
                Console.WriteLine($"Connected to server: {peer?.Address}:{peer?.Port}");
                break;
            case PeerEvent.Disconnected:
                var disconnectInfo = (DisconnectInfo?)data;
                Console.WriteLine($"Disconnected: {disconnectInfo?.Reason}");
                break;
            case PeerEvent.MessageReceived:
                var messageData = (byte[]?)data;
                Console.WriteLine($"Received message from server (bytes: {messageData?.Length})");
                break;
            case PeerEvent.NetworkError:
                var exception = (Exception?)data;
                Console.WriteLine($"Network error: {exception?.Message}");
                break;
        }
    }
}
