using ProtoNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.IO;

namespace PROTONET_DEV
{
    class Program
    {
        static void Main(string[] args) {
            int size = 1024*112;
            Stopwatch sw = new Stopwatch();
            int reads = 0;
            long total = 0;
            ProtoServer server = new ProtoServer();
            server.ClientConnected += (s, e) => {
                s.PacketBufferSize = size;

                Console.WriteLine("Client connected!");
                Console.Title = $"There is now {server.ConnectedClients.Count} client(s) connected!";
                sw.Start();
            };

            server.ClientPingUpdated += (s, e) => {
                Console.WriteLine("Got ping: " + s.Ping);
            };

            server.ClientDisconnected += (s, e) => {
                Console.WriteLine("Client disconnected :( " + e);
                Console.Title = "Client disconnected, Connected: " + server.ConnectedClients.Count;
            };
            server.PacketReceived += (s, e) => {
                reads++;
                total += e.Length;
                if (sw.ElapsedMilliseconds >= 1000) {
                    sw.Restart();
                    double throughPut = Math.Round((total / 1024.0) / 1024.0, 2);
                    Console.Title = ($"Packet/s {reads} MByte/s {throughPut} MBit/s {throughPut * 8.0}");
                    reads = 0;
                    total = 0;
                }
            };
            server.Listen(8899, 10);
            Console.WriteLine("Listening");
            
            Console.ReadLine();
        }
    }
}
