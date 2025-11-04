using LiteNetLib;
using LiteNetLib.Utils;

namespace TestBench;

public class TestClient {
    private static NetManager? _netManager;
    private static NetPeer? _server;
    private static EventBasedNetListener? _listener;

    public static void Run(string[] args) {
        Console.WriteLine("Starting Test Client...");

        _listener = new EventBasedNetListener();
        _netManager = new NetManager(_listener);

        _listener.PeerConnectedEvent += OnConnected;
        _listener.PeerDisconnectedEvent += OnDisconnected;
        _listener.NetworkReceiveEvent += OnMessageReceived;

        _netManager.Start();

        ConnectToServer();

        Console.WriteLine("Press 'r' to reconnect, 'd' to disconnect, 'q' to quit");

        bool running = true;
        while (running) {
            _netManager.PollEvents();

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

        _netManager.Stop();
        Console.WriteLine("Client stopped.");
    }

    private static void ConnectToServer() {
        if (_server?.ConnectionState == ConnectionState.Connected) {
            Console.WriteLine("Already connected to server!");
            return;
        }

        NetDataWriter connectData = new();
        connectData.Put("default_key1");
        _server = _netManager?.Connect("127.0.0.1", 7777, connectData);
        Console.WriteLine("Attempting to connect to server...");
    }

    private static void DisconnectFromServer() {
        if (_server?.ConnectionState == ConnectionState.Connected) {
            _server.Disconnect();
            Console.WriteLine("Disconnecting from server...");
        } else {
            Console.WriteLine("Not connected to server!");
        }
    }

    private static void OnConnected(NetPeer peer) {
        Console.WriteLine($"Connected to server: {peer.Address}:{peer.Port}");
    }

    private static void OnDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        Console.WriteLine($"Disconnected: {disconnectInfo.Reason}");
        _server = null;
    }

    private static void OnMessageReceived(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        Console.WriteLine($"Received message from server (bytes: {reader.AvailableBytes})");
        reader.Recycle();
    }
}
