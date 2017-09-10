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

        public event EventHandler<ProtoClient, Packet> PacketReceived;
        public event EventHandler<ProtoClient, EventArgs> Connected;
        public event EventHandler<ProtoClient, string> Disconnected;
        public event EventHandler<ProtoClient, double> PingUpdated;

        public bool IsConnected {
            get {
                return !(socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0);
            }
        }

        private double ElapsedPing => ((double)pingWatch.ElapsedTicks / Stopwatch.Frequency) * 1000.0;

        public double Ping { get; private set; }
        public object Tag { get; set; }
        public int BufferSize { get; set; }
        public int MaxPingAttempts { get; set; }
        public int PingInterval { get; set; }

        public int SocketReceiveBufferSize {
            get { return socket.ReceiveBufferSize; }
            set { socket.ReceiveBufferSize = value; }
        }

        public int SocketSendBufferSize {
            get { return socket.SendBufferSize; }
            set { socket.SendBufferSize = value; }
        }

        public IPEndPoint EndPoint => (IPEndPoint)socket.RemoteEndPoint;
        public IPEndPoint LocalEndPoint => (IPEndPoint)socket.LocalEndPoint;

        private Socket socket;
        private SocketAsyncEventArgs socketAsyncEventArgs;

        private int totalBytesReceived, bytesExpected;
        private int safeBufferSize;
        private bool isRunning;
        private bool isReceivingHeader;

        private Timer pingTimer;
        private Stopwatch pingWatch;
        private int pingAttempts;

        private object sendLock = new object();

        public ProtoClient(Socket socket) {
            this.socket = socket;
        }

        public ProtoClient() {
            this.socket = new Socket(AddressFamily.InterNetwork,SocketType.Stream, ProtocolType.Tcp);
        }

        public void Connect(string host, int port) {
            socket.Connect(host, port);
            Connected?.Invoke(this, EventArgs.Empty);
            Start();
        }

        public void BufferedSend(byte[] packet) {
            byte[] data = new byte[packet.Length + NetConstants.HeaderSize];
            Array.Copy(packet, 0, data, 4, packet.Length);
            data[0] = (byte)(packet.Length);
            data[1] = (byte)(packet.Length >> 8);
            data[2] = (byte)(packet.Length >> 16);
            data[3] = (byte)(packet.Length >> 24);

            lock (sendLock) {
                socket.Send(data);
            }
        }

        public void Send(byte[] packet) {
            byte[] data = new byte[4];
            data[0] = (byte)(packet.Length);
            data[1] = (byte)(packet.Length >> 8);
            data[2] = (byte)(packet.Length >> 16);
            data[3] = (byte)(packet.Length >> 24);

            lock (sendLock) {
                socket.Send(data);
                socket.Send(packet);
            }
        }

        private void SendPingRequest() {
            unchecked {
                byte[] data = new byte[4];
                data[0] = (byte)(NetConstants.PingRequest);
                data[1] = (byte)(NetConstants.PingRequest >> 8);
                data[2] = (byte)(NetConstants.PingRequest >> 16);
                data[3] = (byte)(NetConstants.PingRequest >> 24);

                lock (sendLock) {
                    socket.Send(data);
                }
            }
        }

        private void SendPingResponse() {
            unchecked {
                byte[] data = new byte[4];
                data[0] = (byte)(NetConstants.PingResponse);
                data[1] = (byte)(NetConstants.PingResponse >> 8);
                data[2] = (byte)(NetConstants.PingResponse >> 16);
                data[3] = (byte)(NetConstants.PingResponse >> 24);

                lock (sendLock) {
                    socket.Send(data);
                }
            }
        }

        public void Start() {
            if (isRunning == false) {
                isRunning = true;

                if (MaxPingAttempts == 0)
                    MaxPingAttempts = 3;

                if (BufferSize == 0)
                    BufferSize = 8192;

                if (PingInterval == 0)
                    PingInterval = 2000;

                socketAsyncEventArgs = new SocketAsyncEventArgs();
                socketAsyncEventArgs.Completed += AsyncReceiveCompleted;

                socketAsyncEventArgs.SetBuffer(new byte[NetConstants.HeaderSize], 0, NetConstants.HeaderSize);
                bytesExpected = NetConstants.HeaderSize;
                socket.NoDelay = true;
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
            pingAttempts++;
            if(pingAttempts > MaxPingAttempts) {
                Dispose();
            }

            pingWatch.Restart();
            try {
                SendPingRequest();
            } catch { Disconnect(); }

            pingTimer.Interval = PingInterval;
        }

        private void AsyncReceiveCompleted(object sender, SocketAsyncEventArgs e) {
            try {
                if (socketAsyncEventArgs.BytesTransferred <= 0)
                    throw new Exception("Remote host disconnected");

                totalBytesReceived += socketAsyncEventArgs.BytesTransferred;

                if (totalBytesReceived == bytesExpected) {
                    totalBytesReceived = 0;

                    if (isReceivingHeader) {
                        bytesExpected = e.Buffer[0] | (e.Buffer[1] << 8) | (e.Buffer[2] << 16) | (e.Buffer[3] << 24);

                        switch (bytesExpected) {
                            case NetConstants.PingRequest:
                                SendPingResponse();
                                bytesExpected = NetConstants.HeaderSize;
                                break;
                            case NetConstants.PingResponse:
                                pingAttempts = 0;
                                Ping = ElapsedPing;
                                PingUpdated?.Invoke(this, Ping);
                                bytesExpected = NetConstants.HeaderSize;
                                break;
                            default:
                                safeBufferSize = BufferSize;
                                isReceivingHeader = false;

                                if (bytesExpected > safeBufferSize)
                                    throw new Exception($"Packet didn't fit into buffer {bytesExpected} > {safeBufferSize}");
                                else if (bytesExpected < NetConstants.HeaderSize)
                                    throw new Exception($"Packet was less than {NetConstants.HeaderSize} bytes");

                                if (socketAsyncEventArgs.Buffer.Length != safeBufferSize)
                                    socketAsyncEventArgs.SetBuffer(new byte[safeBufferSize], 0, safeBufferSize);

                                break;
                        }
                    } else {
                        PacketReceived?.Invoke(this, new Packet { Buffer = socketAsyncEventArgs.Buffer, ActualLength = bytesExpected });

                        isReceivingHeader = true;
                        bytesExpected = NetConstants.HeaderSize;
                    }
                }

                ReceiveAsync();
            } catch (Exception ex) {
                Disconnected?.Invoke(this, ex.Message);
            }
        }

        private void ReceiveAsync() {
            socketAsyncEventArgs.SetBuffer(totalBytesReceived, bytesExpected - totalBytesReceived);
            socket.ReceiveAsync(socketAsyncEventArgs);
        }

        public void Disconnect() {
            socket.Disconnect(false);
        }

        public void Dispose() {
            Disconnect();
            socket.Close();
            socketAsyncEventArgs.Dispose();
            pingTimer.Dispose();
        }
    }
}
