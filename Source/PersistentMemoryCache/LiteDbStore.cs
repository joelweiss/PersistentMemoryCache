using System.Collections.Generic;
using System.Linq;
using PersistentMemoryCache.Internal;
using LiteDB;

namespace PersistentMemoryCache
{
    public class LiteDbStore : IPersistentStore
    {
        private const string _CollectionName = "PersistedCacheEntry";
        private readonly string _ConnectionString;

        public LiteDbStore(LiteDbOptions options)
        {
            _ConnectionString = $"filename={options.FileName};upgrade=true";
            using (var db = new PersistentLiteDatabase(_ConnectionString))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>(_CollectionName);
                collection.EnsureIndex(pce => pce.CacheName);
            }
        }

        public int AddEntry(Internal.LiteDbCacheEntry entry)
        {
            using (var db = new PersistentLiteDatabase(_ConnectionString))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>(_CollectionName);
                return collection.Insert(entry).AsInt32;
            }
        }

        public List<Internal.LiteDbCacheEntry> LoadEntries(string cacheName)
        {
            using (var db = new PersistentLiteDatabase(_ConnectionString))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>(_CollectionName);
                return collection.Find(pce => pce.CacheName == cacheName).ToList();
            }
        }

        public Internal.LiteDbCacheEntry LoadEntry(int key)
        {
            using (var db = new PersistentLiteDatabase(_ConnectionString))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>(_CollectionName);
                return collection.FindById(new BsonValue(key));
            }
        }

        public bool UpdateEntry(int key, Internal.LiteDbCacheEntry entry)
        {
            using (var db = new PersistentLiteDatabase(_ConnectionString))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>(_CollectionName);
                return collection.Update(new BsonValue(key), entry);
            }
        }

        public void RemoveEntry(int id)
        {
            using (var db = new LiteDatabase(_ConnectionString))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>(_CollectionName);
                collection.Delete(new BsonValue(id));
            }
        }
    }
}
