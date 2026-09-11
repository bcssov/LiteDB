using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;

namespace LiteDB.Tests.Database
{
    public class Index_Key_Length_Tests
    {
        private class TagsDocument
        {
            public int Id { get; set; }
            public List<string> Tags { get; set; }
        }

        public static IEnumerable<object[]> SupportedPayloadLengths =>
            new[] { 0, 15, 16, 255, 256, 511, 512, 1021, 1022, 1023, 1024, 1025, 1026, 4093 }
                .Select(length => new object[] { length });

        public static IEnumerable<object[]> UnsupportedPayloadLengths =>
            new[] { 4094, 4095, 4096 }
                .Select(length => new object[] { length });

        [Theory]
        [MemberData(nameof(SupportedPayloadLengths))]
        public void String_Index_Key_RoundTrips_At_Supported_Payload_Boundaries(int payloadLength)
        {
            var value = new string('s', payloadLength);

            AssertStringRoundTrip(value);
        }

        [Theory]
        [MemberData(nameof(SupportedPayloadLengths))]
        public void Binary_Index_Key_RoundTrips_At_Supported_Payload_Boundaries(int payloadLength)
        {
            var value = Enumerable.Range(0, payloadLength).Select(x => (byte)(x % 251)).ToArray();

            AssertBinaryRoundTrip(value);
        }

        [Theory]
        [MemberData(nameof(UnsupportedPayloadLengths))]
        public void String_Index_Key_Rejects_Unsupported_Payload_Without_Corrupting_Database(int payloadLength)
        {
            AssertOversizeRejected(new BsonValue(new string('x', payloadLength)));
        }

        [Theory]
        [MemberData(nameof(UnsupportedPayloadLengths))]
        public void Binary_Index_Key_Rejects_Unsupported_Payload_Without_Corrupting_Database(int payloadLength)
        {
            AssertOversizeRejected(new BsonValue(new byte[payloadLength]));
        }

        [Fact]
        public void Long_SamePrefix_String_Keys_Remain_Distinct_After_Reopen()
        {
            var prefix = new string('p', 1025);
            var first = prefix + "a";
            var second = prefix + "b";

            using (var file = new TempFile())
            {
                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection("items");
                    col.EnsureIndex("idx_value", "$.value");
                    col.Insert(new BsonDocument { ["_id"] = 1, ["value"] = first });
                    col.Insert(new BsonDocument { ["_id"] = 2, ["value"] = second });
                }

                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection("items");
                    col.FindOne(Query.EQ("value", first))["_id"].AsInt32.Should().Be(1);
                    col.FindOne(Query.EQ("value", second))["_id"].AsInt32.Should().Be(2);
                }
            }
        }

        [Fact]
        public void Multibyte_String_Index_Uses_Utf8_Byte_Length()
        {
            var supported = new string('\u00e9', 2046) + "a";
            var unsupported = new string('\u00e9', 2047);

            Encoding.UTF8.GetByteCount(supported).Should().Be(4093);
            Encoding.UTF8.GetByteCount(unsupported).Should().Be(4094);
            AssertStringRoundTrip(supported);
            AssertOversizeRejected(new BsonValue(unsupported));
        }

        [Fact]
        public void Oversize_Update_Is_Rejected_Before_Stored_Document_Is_Changed()
        {
            var original = new string('o', 1026);

            using (var file = new TempFile())
            {
                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection("items");
                    col.EnsureIndex("idx_value", "$.value");
                    col.Insert(new BsonDocument { ["_id"] = 1, ["value"] = original });

                    Action update = () => col.Update(new BsonDocument
                    {
                        ["_id"] = 1,
                        ["value"] = new string('x', 4094)
                    });

                    update.Should().Throw<LiteException>();
                }

                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection("items");
                    col.FindOne(Query.EQ("value", original))["_id"].AsInt32.Should().Be(1);
                    col.FindById(1)["value"].AsString.Should().Be(original);
                }
            }
        }

        [Fact]
        public void TagsStyle_Multikey_Index_Preserves_Large_Values_After_Reopen()
        {
            var tag = new string('t', 4093);

            using (var file = new TempFile())
            {
                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection<TagsDocument>("definitions");
                    col.EnsureIndex(x => x.Tags);
                    col.Insert(new TagsDocument
                    {
                        Id = 1,
                        Tags = new List<string> { tag }
                    });
                }

                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection<TagsDocument>("definitions");
                    var query = col.Query().Where(x => x.Tags.Contains(tag));
                    query.GetPlan()["index"]["expr"].AsString.Should().Be("$.Tags[*]");

                    var document = query.FirstOrDefault();
                    document.Id.Should().Be(1);
                    document.Tags.Single().Should().Be(tag);
                }
            }
        }

        private static void AssertStringRoundTrip(string value)
        {
            using (var file = new TempFile())
            {
                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection("items");
                    col.EnsureIndex("idx_value", "$.value");
                    col.Insert(new BsonDocument { ["_id"] = 1, ["value"] = value });
                }

                using (var db = new LiteDatabase(file.Filename))
                {
                    var document = db.GetCollection("items").FindOne(Query.EQ("value", value));
                    document["_id"].AsInt32.Should().Be(1);
                    document["value"].AsString.Should().Be(value);
                }
            }
        }

        private static void AssertBinaryRoundTrip(byte[] value)
        {
            using (var file = new TempFile())
            {
                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection("items");
                    col.EnsureIndex("idx_value", "$.value");
                    col.Insert(new BsonDocument { ["_id"] = 1, ["value"] = value });
                }

                using (var db = new LiteDatabase(file.Filename))
                {
                    var document = db.GetCollection("items").FindOne(Query.EQ("value", new BsonValue(value)));
                    document["_id"].AsInt32.Should().Be(1);
                    Assert.Equal(value, document["value"].AsBinary);
                }
            }
        }

        private static void AssertOversizeRejected(BsonValue oversizedValue)
        {
            using (var file = new TempFile())
            {
                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection("items");
                    col.EnsureIndex("idx_value", "$.value");
                    col.Insert(new BsonDocument { ["_id"] = 1, ["value"] = "valid" });

                    Action insert = () => col.Insert(new BsonDocument { ["_id"] = 2, ["value"] = oversizedValue });
                    insert.Should().Throw<LiteException>();
                }

                using (var db = new LiteDatabase(file.Filename))
                {
                    var col = db.GetCollection("items");
                    col.Count().Should().Be(1);
                    col.FindOne(Query.EQ("value", "valid"))["_id"].AsInt32.Should().Be(1);
                    Assert.Null(col.FindById(2));
                }
            }
        }
    }
}
