using GameNetworking;
using LiteNetLib;

namespace TestBench;

public class TestClient {
    private static Client? _client;
    private static string _serverAddress = "127.0.0.1";
    private static int _serverPort = 7777;
    private static string _connectionKey = "key";

    public static void Run(string[] args) {
        Console.WriteLine("Starting Test Client...");

        // Parse command line arguments: dotnet run client <address> <port> <key>
        if (args.Length > 1) { _serverAddress = args[1]; }
        if (args.Length > 2) { _ = int.TryParse(args[2], out _serverPort); }
        if (args.Length > 3) { _connectionKey = args[3]; }

        Console.WriteLine($"Target server: {_serverAddress}:{_serverPort}");

        _client = new Client();
        _client.Init();
        _client.ClientEvent += OnClientEvent;

        ConnectToServer();

        Console.WriteLine("Press 'r' to reconnect, 'd' to disconnect, 'n' for NAT punch, 'q' to quit");

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
                    case 'n':
                    case 'N':
                        ConnectThroughNAT();
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

        _client?.Connect(_serverAddress, _serverPort, _connectionKey);
        Console.WriteLine($"Attempting to connect to {_serverAddress}:{_serverPort}...");
    }

    private static void ConnectThroughNAT() {
        if (_client == null) {
            Console.WriteLine("Client not initialized!");
            return;
        }

        Console.Write($"Enter facilitator address (default: {_serverAddress}): ");
        string? facilitatorAddress = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(facilitatorAddress)) {
            facilitatorAddress = _serverAddress;
        }

        Console.Write($"Enter facilitator port (default: {_serverPort}): ");
        string? portInput = Console.ReadLine();
        int facilitatorPort = int.TryParse(portInput, out int port) ? port : _serverPort;

        Console.Write("Enter target player token: ");
        string? token = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(token)) {
            Console.WriteLine("Token cannot be empty!");
            return;
        }

        // _client.ConnectThroughNAT(facilitatorAddress, facilitatorPort, token);
        Console.WriteLine($"NAT punch request sent to {facilitatorAddress}:{facilitatorPort} for token: {token}");
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
        string timestamp = DateTime.Now.ToString("HH:mm:ss");

        switch (eventType) {
            case PeerEvent.Connected:
                Console.WriteLine($"[{timestamp}] Connected to server: {peer?.Address}:{peer?.Port}");
                break;
            case PeerEvent.Disconnected:
                var disconnectInfo = (DisconnectInfo?)data;
                Console.WriteLine($"[{timestamp}] Disconnected: {disconnectInfo?.Reason}");
                break;
            case PeerEvent.MessageReceived:
                var messageData = (byte[]?)data;
                Console.WriteLine($"[{timestamp}] Received message from server (bytes: {messageData?.Length})");
                break;
            case PeerEvent.NetworkError:
                var exception = (Exception?)data;
                Console.WriteLine($"[{timestamp}] Network error: {exception?.Message}");
                break;
            case PeerEvent.NetworkInfo:
                Console.WriteLine($"[{timestamp}] Network info: {data}");
                break;
        }
    }
}
