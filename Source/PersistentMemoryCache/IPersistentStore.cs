using PersistentMemoryCache.Internal;
using System.Collections.Generic;

namespace PersistentMemoryCache
{
    public interface IPersistentStore
    {
        int AddEntry(Internal.LiteDbCacheEntry entry);
        Internal.LiteDbCacheEntry LoadEntry(int key);
        List<Internal.LiteDbCacheEntry> LoadEntrys(string cacheName);
        void RemoveEntry(int id);
        bool UpdateEntry(int key, Internal.LiteDbCacheEntry entry);
    }
}