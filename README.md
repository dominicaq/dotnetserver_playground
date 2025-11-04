# GameServer
A .NET 9.0 game server/client abstraction built with LiteNetLib.

## Building GameNetworking .dll for general use
```bash
./export-dll.sh
```

## Test Bench Quickstart (automatically builds .dll)
```bash
cd TestBench
# run server
dotnet run
# run client (separate terminal)
dotnet run client
```

Edit `server_config.json` to configure.
