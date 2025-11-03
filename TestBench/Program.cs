using System;

namespace TestBench;

public class Program {
  public static void Main(string[] args) {
    if (args.Length > 0 && args[0].ToLower() == "client") {
      TestClient.Run(args);
    } else {
      TestServer.Run(args);
    }
  }
}
