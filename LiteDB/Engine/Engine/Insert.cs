using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        /// <summary>
        /// Insert all documents in collection. If document has no _id, use AutoId generation.
        /// </summary>
        public int Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
            if (docs == null) throw new ArgumentNullException(nameof(docs));

            return this.AutoTransaction(transaction =>
            {
                var snapshot = transaction.CreateSnapshot(LockMode.Write, collection, true);
                var count = 0;
                var indexer = new IndexService(snapshot, _header.Pragmas.Collation, _disk.MAX_ITEMS_COUNT);
                var data = new DataService(snapshot, _disk.MAX_ITEMS_COUNT);

                LOG($"insert `{collection}`", "COMMAND");

                foreach (var doc in docs)
                {
                    _state.Validate();

                    transaction.Safepoint();

                    this.InsertDocument(snapshot, doc, autoId, indexer, data);

                    count++;
                }

                return count;
            });
        }

        /// <summary>
        /// Internal implementation of insert a document
        /// </summary>
        private void InsertDocument(Snapshot snapshot, BsonDocument doc, BsonAutoId autoId, IndexService indexer, DataService data)
        {
            // if no _id, use AutoId
            if (!doc.TryGetValue("_id", out var id))
            {
                doc["_id"] = id =
                    autoId == BsonAutoId.ObjectId ? new BsonValue(ObjectId.NewObjectId()) :
                    autoId == BsonAutoId.Guid ? new BsonValue(Guid.NewGuid()) :
                    this.GetSequence(snapshot, autoId);
            }
            else if(id.IsNumber)
            {
                // update memory sequence of numeric _id
                this.SetSequence(snapshot, id);
            }

            // test if _id is a valid type
            if (id.IsNull || id.IsMinValue || id.IsMaxValue)
            {
                throw LiteException.InvalidDataType("_id", id);
            }

            // evaluate and validate every key before storing the document
            var indexKeys = snapshot.CollectionPage.GetCollectionIndexes()
                .Select(index => new
                {
                    Index = index,
                    Keys = index.BsonExpr.GetIndexKeys(doc, _header.Pragmas.Collation).ToArray()
                })
                .ToArray();

            foreach (var item in indexKeys)
            {
                foreach (var key in item.Keys)
                {
                    IndexService.ValidateKey(key);
                }
            }

            // storage in data pages - returns dataBlock address
            var dataBlock = data.Insert(doc);

            IndexNode last = null;

            // for each index, insert all previously validated keys
            foreach (var item in indexKeys)
            {
                // do a loop with all keys (multi-key supported)
                foreach(var key in item.Keys)
                {
                    // insert node
                    var node = indexer.AddNode(item.Index, key, dataBlock, last);

                    last = node;
                }
            }
        }
    }
}
