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
            int pings = 0;
            ProtoServer server = new ProtoServer();
            server.ClientConnected += (s, e) => {
                s.BufferSize = size;
                s.PingUpdated += (se, ex) => {
                    pings++;
                    Console.WriteLine("Got ping: " + s.Ping);
                };
                //Console.WriteLine("Client connected!");
                Console.Title = $"There is now {server.ConnectedClients.Count} client(s) connected!";
                sw.Start();
            };


            server.ClientDisconnected += (s, e) => {
                Console.WriteLine("Client disconnected :( " + e);
                Console.Title = "Client disconnected, Connected: " + server.ConnectedClients.Count;
            };
            server.PacketReceived += (s, e) => {
                reads++;
                total += e.ActualLength;

                if (sw.ElapsedMilliseconds >= 1000) {
                    sw.Restart();
                    Console.Title = ($"Packets/s {reads} Mb/s {(total / 1024.0) / 1024.0} Pings/s {pings}");
                    reads = 0;
                    total = 0;
                    pings = 0;
                }
            };
            server.Listen(9090, 10);
            
            byte[] bytes = new byte[size];

            ProtoClient c = new ProtoClient();
            c.Connect("127.0.0.1", 9090);
            while (true) {
                c.Send(bytes);
            }
            Console.ReadLine();
        }
    }
}
