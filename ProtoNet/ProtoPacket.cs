namespace ProtoNet
{
    public class ProtoPacket
    {
        private byte[] buffer;
        private int length;

        public byte[] Buffer => buffer;
        public int Length => length;

        public ProtoPacket(byte[] buffer, int length) {
            this.buffer = buffer;
            this.length = length;
        }
    }
}
