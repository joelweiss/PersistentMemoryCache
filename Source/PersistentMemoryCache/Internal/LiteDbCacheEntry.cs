using Microsoft.Extensions.Caching.Memory;
using System;

namespace PersistentMemoryCache.Internal
{
    public class LiteDbCacheEntry
    {
        private TimeSpan? _SlidingExpiration;

        public int Id { get; set; }
        public string CacheName { get; set; }
        public CacheItemPriority Priority { get; set; }
        public object Key { get; set; }
        public object Value { get; set; }
        internal DateTimeOffset LastAccessed { get; set; }
        public DateTimeOffset? AbsoluteExpiration { get; set; }
        public TimeSpan? SlidingExpiration
        {
            get
            {
                if (_SlidingExpiration <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(SlidingExpiration), _SlidingExpiration, "The sliding expiration value must be positive.");
                }
                return _SlidingExpiration;
            }
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(SlidingExpiration), value, "The sliding expiration value must be positive.");
                }
                _SlidingExpiration = value;
            }
        }
    }
}
