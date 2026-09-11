using System;
using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// Encodes a four-bit BsonType and a 12-bit String/Binary payload length in two bytes.
    /// </summary>
    internal static class ExtendedLengthHelper
    {
        /// <summary>
        /// Read BsonType and UShort length from 2 bytes
        /// </summary>
        public static void ReadLength(byte typeByte, byte lengthByte, out BsonType type, out ushort length)
        {
            var bsonType = (byte)(typeByte & 0b0000_1111);
            var lengthLSByte = lengthByte;
            var lengthMSByte = (byte)(typeByte & 0b1111_0000);
            type = (BsonType)bsonType;
            length = (ushort)((lengthMSByte << 4) | lengthLSByte);
        }

        /// <summary>
        /// Write BsonType and UShort length in 2 bytes
        /// </summary>
        public static void WriteLength(BsonType type, ushort length, out byte typeByte, out byte lengthByte)
        {
            if (length > MAX_INDEX_KEY_LENGTH) throw new ArgumentOutOfRangeException(nameof(length));
            var bsonType = (byte)type;
            if (bsonType > 0b0000_1111) throw new ArgumentOutOfRangeException(nameof(type));
            var lengthLSByte = unchecked((byte)length);
            var lengthMSByte = (byte)((length & 0b1111_0000_0000) >> 4);
            typeByte = (byte)(lengthMSByte | bsonType);
            lengthByte = lengthLSByte;
        }
    }
}
