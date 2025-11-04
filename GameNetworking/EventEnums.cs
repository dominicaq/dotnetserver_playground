namespace GameNetworking;

public enum ClientEvent {
    Connected,
    Disconnected,
    MessageReceived,
    NetworkError
}

public enum PeerEvent {
    ConnectionRequested,
    Connected,
    Disconnected,
    MessageReceived,
    NetworkError
}

