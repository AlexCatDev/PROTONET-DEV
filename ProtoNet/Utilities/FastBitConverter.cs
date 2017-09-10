using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProtoNet.Utilities
{
    public static class FastBitConverter
    {
        public static int ToInt32(byte[] data, int offset) {
            return data[offset] | (data[++offset] << 8) | (data[++offset] << 16) | (data[++offset] << 24);
        }

        public static void WriteBytes(byte[] buffer, int offset, int value) {
            buffer[offset] = (byte)(value);
            buffer[++offset] = (byte)(value >> 8);
            buffer[++offset] = (byte)(value >> 16);
            buffer[++offset] = (byte)(value >> 24);
        }

        public static byte[] GetBytes(int value) {
            byte[] data = new byte[4];

            data[0] = (byte)(value);
            data[1] = (byte)(value >> 8);
            data[2] = (byte)(value >> 16);
            data[3] = (byte)(value >> 24);

            return data;
        }
    }
}
