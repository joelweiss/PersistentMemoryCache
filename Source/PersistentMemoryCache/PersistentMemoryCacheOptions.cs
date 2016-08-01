using System;
using Microsoft.Extensions.Internal;

namespace PersistentMemoryCache
{
    public class PersistentMemoryCacheOptions
    {
        public PersistentMemoryCacheOptions(string cacheName, IPersistentStore persistentStore)
        {
            CacheName = cacheName;
            PersistentStore = persistentStore;
        }

        public string CacheName { get; } = "Default";
        public IPersistentStore PersistentStore { get; }
        public ISystemClock Clock { get; set; } = new SystemClock();
        public bool CompactOnMemoryPressure { get; set; } = true;
        public TimeSpan ExpirationScanFrequency { get; set; } = TimeSpan.FromMinutes(1);
        public bool IsPersistent { get; set; } = true;
    }
}