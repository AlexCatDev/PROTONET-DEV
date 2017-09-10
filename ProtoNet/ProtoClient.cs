using ProtoNet.Utilities;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Timers;

namespace ProtoNet
{
    public class ProtoClient : IDisposable
    {
        public delegate void EventHandler<TSender, TEventArgs>(TSender sender, TEventArgs eventArgs);
        public event EventHandler<ProtoClient, ProtoPacket> PacketReceived;
        public event EventHandler<ProtoClient, EventArgs> Connected;
        public event EventHandler<ProtoClient, string> Disconnected;
        public event EventHandler<ProtoClient, double> PingUpdated;

        private Socket socket;
        private SocketAsyncEventArgs socketAsyncEvent;

        private int totalBytesReceived;
        private int bytesExpected;
        private bool isRunning;
        private bool isReceivingHeader;

        private Timer pingTimer;
        private Stopwatch pingWatch;
        private int pingAttempts;

        private object sendLock = new object();
        private double elapsedTime => ((double)pingWatch.ElapsedTicks / Stopwatch.Frequency) * 1000.0;

        public double Ping { get; private set; }
        public object Tag { get; set; }

        public int PacketBufferSize { get; set; } = 8192;
        public int MaxPingAttempts { get; set; } = 3;
        public int PingInterval { get; set; } = 1000;

        public int MinimumPacketSize { get; set; } = 4;

        public int SocketReceiveBufferSize {
            get { return socket.ReceiveBufferSize; }
            set { socket.ReceiveBufferSize = value; }
        }

        public int SocketSendBufferSize {
            get { return socket.SendBufferSize; }
            set { socket.SendBufferSize = value; }
        }

        public bool NoDelay {
            get { return socket.NoDelay; }
            set { socket.NoDelay = true; }
        }

        public bool IsConnected {
            get {
                return !(socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0);
            }
        }

        public IPEndPoint EndPoint => (IPEndPoint)socket.RemoteEndPoint;
        public IPEndPoint LocalEndPoint => (IPEndPoint)socket.LocalEndPoint;

        public ProtoClient(Socket socket) {
            this.socket = socket;
        }

        public ProtoClient() {
            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public void Connect(string host, int port) {
            socket.Connect(host, port);
            Connected?.Invoke(this, EventArgs.Empty);
            Start();
        }

        public void BufferedSend(byte[] packet) {
            byte[] data = new byte[packet.Length + NetworkConstants.HeaderSize];
            Array.Copy(packet, 0, data, 4, packet.Length);
            FastBitConverter.WriteBytes(data, 0, packet.Length);

            lock (sendLock) {
                socket.Send(data);
            }
        }

        public void Send(byte[] packet) {
            lock (sendLock) {
                socket.Send(FastBitConverter.GetBytes(packet.Length));
                socket.Send(packet);
            }
        }

        private void SendPingRequest() {
                lock (sendLock) {
                    socket.Send(FastBitConverter.GetBytes(NetworkConstants.PingRequest));
                }
        }

        private void SendPingResponse() {
            lock (sendLock) {
                socket.Send(FastBitConverter.GetBytes(NetworkConstants.PingResponse));
            }
        }

        public void Start() {
            if (isRunning == false) {
                isRunning = true;
                NoDelay = true;

                socketAsyncEvent = new SocketAsyncEventArgs();
                socketAsyncEvent.Completed += AsyncReceiveCompleted;

                socketAsyncEvent.SetBuffer(new byte[PacketBufferSize], 0, PacketBufferSize);
                bytesExpected = NetworkConstants.HeaderSize;
                isReceivingHeader = true;

                pingTimer = new Timer(PingInterval);
                pingTimer.AutoReset = true;
                pingTimer.Elapsed += PingTimer_Elapsed;
                pingWatch = new Stopwatch();
                try {
                    ReceiveAsync();
                    pingTimer.Start();
                } catch (Exception ex) {
                    Disconnected?.Invoke(this, "?? what is this exeption?\nPrinting stacktrace..\n" + ex.StackTrace);
                }
            }
        }

        private void PingTimer_Elapsed(object sender, ElapsedEventArgs e) {
            if (pingAttempts++ > MaxPingAttempts)
                Disconnect();

            pingWatch.Restart();

            try {
                SendPingRequest();
            } catch {
                Disconnect();
            }

            pingTimer.Interval = PingInterval;
        }

        private void AsyncReceiveCompleted(object sender, SocketAsyncEventArgs e) {
            try {
                if (socketAsyncEvent.BytesTransferred <= 0)
                    throw new Exception("Disconnected");

                totalBytesReceived += socketAsyncEvent.BytesTransferred;

                if (totalBytesReceived == bytesExpected) {
                    totalBytesReceived = 0;

                    if (isReceivingHeader) {
                        bytesExpected = FastBitConverter.ToInt32(e.Buffer, 0);

                        switch (bytesExpected) {
                            case NetworkConstants.PingRequest:
                                SendPingResponse();
                                bytesExpected = NetworkConstants.HeaderSize;
                                break;
                            case NetworkConstants.PingResponse:
                                pingAttempts = 0;
                                Ping = elapsedTime;
                                PingUpdated?.Invoke(this, Ping);
                                bytesExpected = NetworkConstants.HeaderSize;
                                break;
                            default:
                                int safeBufferSize = PacketBufferSize;

                                if (bytesExpected > safeBufferSize)
                                    throw new Exception($"Packet didn't fit into buffer {bytesExpected} > {safeBufferSize}");
                                else if(bytesExpected < MinimumPacketSize)
                                    throw new Exception($"Packet was smaller than allowed {bytesExpected} < {MinimumPacketSize}");

                                if (socketAsyncEvent.Buffer.Length != safeBufferSize)
                                    socketAsyncEvent.SetBuffer(new byte[safeBufferSize], 0, safeBufferSize);

                                isReceivingHeader = false;
                                break;
                        }
                    } else {
                        PacketReceived?.Invoke(this, new ProtoPacket(socketAsyncEvent.Buffer, bytesExpected));

                        bytesExpected = NetworkConstants.HeaderSize;
                        isReceivingHeader = true;
                    }
                }

                ReceiveAsync();
            } catch (Exception ex) {
                Disconnected?.Invoke(this, ex.Message);
            }
        }

        private void ReceiveAsync() {
            socketAsyncEvent.SetBuffer(totalBytesReceived, bytesExpected - totalBytesReceived);
            socket.ReceiveAsync(socketAsyncEvent);
        }

        public void Disconnect() {
            socket.Disconnect(false);
        }

        public void Dispose() {
            Disconnect();
            socket.Close();
            socketAsyncEvent.Dispose();
            pingTimer.Dispose();
        }
    }
}
