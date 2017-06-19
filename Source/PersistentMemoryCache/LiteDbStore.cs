using System.Collections.Generic;
using System.Linq;
using PersistentMemoryCache.Internal;
using LiteDB;

namespace PersistentMemoryCache
{
    public class LiteDbStore : IPersistentStore
    {
        private readonly string _FileName;

        public LiteDbStore(LiteDbOptions options)
        {
            _FileName = options.FileName;
            using (var db = new PersistentLiteDatabase(_FileName))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>("PersistedCacheEntry");
                collection.EnsureIndex(pce => pce.CacheName);
            }
        }

        public int AddEntry(Internal.LiteDbCacheEntry entry)
        {
            using (var db = new PersistentLiteDatabase(_FileName))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>("PersistedCacheEntry");
                return collection.Insert(entry).AsInt32;
            }
        }

        public List<Internal.LiteDbCacheEntry> LoadEntries(string cacheName)
        {
            using (var db = new PersistentLiteDatabase(_FileName))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>("PersistedCacheEntry");
                return collection.Find(pce => pce.CacheName == cacheName).ToList();
            }
        }

        public Internal.LiteDbCacheEntry LoadEntry(int key)
        {
            using (var db = new PersistentLiteDatabase(_FileName))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>("PersistedCacheEntry");
                return collection.FindById(new BsonValue(key));
            }
        }

        public bool UpdateEntry(int key, Internal.LiteDbCacheEntry entry)
        {
            using (var db = new PersistentLiteDatabase(_FileName))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>("PersistedCacheEntry");
                return collection.Update(new BsonValue(key), entry);
            }
        }

        public void RemoveEntry(int id)
        {
            using (var db = new LiteDatabase(_FileName))
            {
                var collection = db.GetCollection<Internal.LiteDbCacheEntry>("PersistedCacheEntry");
                collection.Delete(new BsonValue(id));
            }
        }
    }
}
