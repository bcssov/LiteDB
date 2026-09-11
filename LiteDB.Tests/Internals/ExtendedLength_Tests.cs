using System;
using System.Linq;
using Xunit;

namespace LiteDB.Internals
{
    public class ExtendedLength_Tests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(511)]
        [InlineData(512)]
        [InlineData(1021)]
        [InlineData(1022)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(1025)]
        [InlineData(1026)]
        [InlineData(4093)]
        [InlineData(4094)]
        [InlineData(4095)]
        public void ExtendedLengthHelper_RoundTrips_12Bit_Lengths(int expectedLength)
        {
            ExtendedLengthHelper.WriteLength(BsonType.String, (ushort)expectedLength, out var typeByte, out var lengthByte);
            ExtendedLengthHelper.ReadLength(typeByte, lengthByte, out var type, out var length);

            Assert.Equal(BsonType.String, type);
            Assert.Equal((ushort)expectedLength, length);
        }

        [Theory]
        [InlineData(BsonType.String, 0, 0x06, 0x00)]
        [InlineData(BsonType.String, 255, 0x06, 0xFF)]
        [InlineData(BsonType.String, 256, 0x16, 0x00)]
        [InlineData(BsonType.String, 4095, 0xF6, 0xFF)]
        [InlineData(BsonType.Binary, 0, 0x09, 0x00)]
        [InlineData(BsonType.Binary, 255, 0x09, 0xFF)]
        [InlineData(BsonType.Binary, 256, 0x19, 0x00)]
        [InlineData(BsonType.Binary, 4095, 0xF9, 0xFF)]
        public void ExtendedLengthHelper_Writes_Expected_Metadata(
            BsonType type,
            int length,
            byte expectedTypeByte,
            byte expectedLengthByte)
        {
            ExtendedLengthHelper.WriteLength(type, (ushort)length, out var typeByte, out var lengthByte);

            Assert.Equal(expectedTypeByte, typeByte);
            Assert.Equal(expectedLengthByte, lengthByte);
        }

        [Theory]
        [InlineData(1024, 0)]
        [InlineData(1025, 1)]
        [InlineData(1026, 2)]
        [InlineData(4093, 1021)]
        public void Legacy_TwoBit_Encoding_Wrapped_Large_Lengths(int originalLength, int decodedLength)
        {
            var typeByte = (byte)(((originalLength & 0b11_0000_0000) >> 2) | (byte)BsonType.String);
            var lengthByte = unchecked((byte)originalLength);
            var legacyDecodedLength = (ushort)(((typeByte & 0b1100_0000) << 2) | lengthByte);

            Assert.Equal((ushort)decodedLength, legacyDecodedLength);
            Assert.NotEqual((ushort)originalLength, legacyDecodedLength);
        }

        [Fact]
        public void ExtendedLengthHelper_Rejects_Length_Above_12Bit_Range()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ExtendedLengthHelper.WriteLength(BsonType.String, 4096, out _, out _));
        }

        [Fact]
        public void BsonTypes_Fit_In_Four_Bits()
        {
            Assert.All(Enum.GetValues(typeof(BsonType)).Cast<BsonType>(), type =>
                Assert.InRange((byte)type, (byte)0, (byte)15));
        }
    }
}
