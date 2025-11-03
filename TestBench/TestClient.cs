using System;
using LiteNetLib;
using LiteNetLib.Utils;

namespace TestBench;

public class TestClient {
  private static NetManager? _netManager;
  private static NetPeer? _server;
  private static readonly NetDataWriter _writer = new();

  public static void Run(string[] args) {
    Console.WriteLine("Starting Test Client...");

    EventBasedNetListener listener = new();
    _netManager = new NetManager(listener);

    listener.PeerConnectedEvent += OnConnected;
    listener.PeerDisconnectedEvent += OnDisconnected;
    listener.NetworkReceiveEvent += OnMessageReceived;

    _netManager.Start();

    NetDataWriter connectData = new();
    connectData.Put("default_key"); // Match your server's connection key

    _server = _netManager.Connect("127.0.0.1", 7777, connectData);

    Console.WriteLine("Press 'm' to send message, 'q' to quit");

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
          case 'm':
          case 'M':
            SendTestMessage();
            break;
        }
      }

      System.Threading.Thread.Sleep(15);
    }

    _netManager.Stop();
    Console.WriteLine("Client stopped.");
  }

  private static void OnConnected(NetPeer peer) {
    Console.WriteLine($"Connected to server: {peer.Address}:{peer.Port}");
  }

  private static void OnDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
    Console.WriteLine($"Disconnected: {disconnectInfo.Reason}");
  }

  private static void OnMessageReceived(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
    string message = reader.GetString();
    Console.WriteLine($"Received: {message}");
    reader.Recycle();
  }

  private static void SendTestMessage() {
    if (_server?.ConnectionState == ConnectionState.Connected) {
      _writer.Reset();
      _writer.Put($"Test message from client at {DateTime.Now:HH:mm:ss}");
      _server.Send(_writer, DeliveryMethod.ReliableOrdered);
      Console.WriteLine("Message sent!");
    } else {
      Console.WriteLine("Not connected to server!");
    }
  }
}
