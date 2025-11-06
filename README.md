# GameServer
A .NET 9.0 game server/client abstraction built with LiteNetLib.

## Building GameNetworking.dll for general use
```bash
./export-dll.sh
```

## Test Bench Quickstart (automatically builds .dll)
```bash
cd TestBench

# Start the server
dotnet run

# Start the client (on another machine or in a separate terminal)
dotnet run client
```

When starting client, you have to type the server code. Here's the special cases.
`8473` is the default port.
```bash
# Same machine
127.0.0.1:8473

# Different machine, same router
192.168.x.x:8473
```

Edit `server_config.json` to configure.
