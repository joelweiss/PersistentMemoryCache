//Based on https://github.com/aspnet/Caching/blob/dev/src/Microsoft.Extensions.Caching.Memory/MemoryCache.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Caching.Memory;
using PersistentMemoryCache.Internal;

namespace PersistentMemoryCache
{
    /// <summary>
    /// An implementation of <see cref="IMemoryCache"/> using a dictionary and IPersistentStore to
    /// store its entries.
    /// </summary>
    public class PersistentMemoryCache : IMemoryCache
    {
        private Dictionary<object, PersistentCacheEntry> _InMemoryEntries;
        private readonly ReaderWriterLockSlim _EntryLock;
        private bool _Disposed;

        // We store the delegates locally to prevent allocations
        // every time a new CacheEntry is created.
        private readonly Action<PersistentCacheEntry> _SetEntry;
        private readonly Action<PersistentCacheEntry> _EntryExpirationNotification;

        private DateTimeOffset _LastExpirationScan;
        private bool _IsReloadingFromStore;
        private PersistentMemoryCacheOptions _Options;

        /// <summary>
        /// Creates a new <see cref="PersistentMemoryCache"/> instance.
        /// </summary>
        /// <param name="options">The options of the cache.</param>
        public PersistentMemoryCache(PersistentMemoryCacheOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            _Options = options;

            _InMemoryEntries = new Dictionary<object, PersistentCacheEntry>();
            _EntryLock = new ReaderWriterLockSlim();
            _SetEntry = SetEntry;
            _EntryExpirationNotification = EntryExpired;

            _LastExpirationScan = _Options.Clock.UtcNow;
            if (_Options.CompactOnMemoryPressure)
            {
                GcNotification.Register(DoMemoryPreassureCollection, state: null);
            }
            if (_Options.IsPersistent)
            {
                ReloadDataFromStore();
            }
        }

        private void ReloadDataFromStore()
        {
            try
            {
                _IsReloadingFromStore = true;
                List<Internal.LiteDbCacheEntry> persistentCacheEntries = _Options.PersistentStore.LoadEntries(_Options.CacheName);
                foreach (Internal.LiteDbCacheEntry persistentCacheEntry in persistentCacheEntries)
                {
                    using (PersistentCacheEntry cacheEntry = (PersistentCacheEntry)CreateEntry(persistentCacheEntry.Key))
                    {
                        cacheEntry.PersistentStoreId = persistentCacheEntry.Id;
                        cacheEntry.Priority = persistentCacheEntry.Priority;
                        cacheEntry.Value = persistentCacheEntry.GetValue();
                        cacheEntry.LastAccessed = persistentCacheEntry.LastAccessed;
                        cacheEntry.AbsoluteExpiration = persistentCacheEntry.AbsoluteExpiration;
                        cacheEntry.SlidingExpiration = persistentCacheEntry.SlidingExpiration;
                    }
                }
            }
            finally
            {
                _IsReloadingFromStore = false;
            }
        }

        /// <summary>
        /// Cleans up the background collection events.
        /// </summary>
        ~PersistentMemoryCache()
        {
            Dispose(false);
        }

        /// <summary>
        /// Gets the count of the current entries for diagnostic purposes.
        /// </summary>
        public int Count
        {
            get
            {
                return _InMemoryEntries.Count;
            }
        }

        public ICacheEntry CreateEntry(object key)
        {
            CheckDisposed();
            return new PersistentCacheEntry(key: key, notifyCacheEntryDisposed: _SetEntry, notifyCacheOfExpiration: _EntryExpirationNotification);
        }

        private void SetEntry(PersistentCacheEntry entry)
        {
            var utcNow = _Options.Clock.UtcNow;

            DateTimeOffset? absoluteExpiration = null;
            if (entry._AbsoluteExpirationRelativeToNow.HasValue)
            {
                absoluteExpiration = utcNow + entry._AbsoluteExpirationRelativeToNow;
            }
            else if (entry._AbsoluteExpiration.HasValue)
            {
                absoluteExpiration = entry._AbsoluteExpiration;
            }

            // Applying the option's absolute expiration only if it's not already smaller.
            // This can be the case if a dependent cache entry has a smaller value, and
            // it was set by cascading it to its parent.
            if (absoluteExpiration.HasValue)
            {
                if (!entry._AbsoluteExpiration.HasValue || absoluteExpiration.Value < entry._AbsoluteExpiration.Value)
                {
                    entry._AbsoluteExpiration = absoluteExpiration;
                }
            }

            // Initialize the last access timestamp at the time the entry is added
            entry.LastAccessed = utcNow;

            var added = false;
            PersistentCacheEntry priorEntry;

            _EntryLock.EnterWriteLock();
            try
            {

                if (_InMemoryEntries.TryGetValue(entry.Key, out priorEntry))
                {
                    RemoveEntryFromMemoryAndStore(priorEntry);
                    priorEntry.SetExpired(EvictionReason.Replaced);
                }

                if (!entry.CheckExpired(utcNow))
                {
                    AddEntryToMemoryAndStore(entry);
                    entry.AttachTokens();
                    if (_Options.IsPersistent)
                    {
                        entry.PropertyChanged += Entry_PropertyChanged;
                    }
                    added = true;
                }
            }
            finally
            {
                _EntryLock.ExitWriteLock();
            }

            if (priorEntry != null)
            {
                priorEntry.InvokeEvictionCallbacks();
            }

            if (!added)
            {
                entry.InvokeEvictionCallbacks();
            }
            StartScanForExpiredItems();
        }

        private void Entry_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var entry = (PersistentCacheEntry)sender;
            var liteDbEntry = CreateLiteDbEntryFromPersistentEntry(entry);
            _Options.PersistentStore.UpdateEntry(entry.PersistentStoreId.Value, liteDbEntry);
        }

        private void AddEntryToMemoryAndStore(PersistentCacheEntry entry)
        {
            _InMemoryEntries[entry.Key] = entry;
            if (_Options.IsPersistent && !_IsReloadingFromStore)
            {
                LiteDbCacheEntry liteDbEntry = CreateLiteDbEntryFromPersistentEntry(entry);
                entry.PersistentStoreId = _Options.PersistentStore.AddEntry(liteDbEntry);
            }
        }

        private LiteDbCacheEntry CreateLiteDbEntryFromPersistentEntry(PersistentCacheEntry entry)
        {
            Type cacheValueType = entry.Value?.GetType() ?? typeof(object);
            LiteDbCacheEntry liteDbEntry = LiteDbCacheEntry.ConstructCacheEntry(cacheValueType);
            liteDbEntry.CacheName = _Options.CacheName;
            liteDbEntry.Priority = entry.Priority;
            liteDbEntry.Key = entry.Key;
            liteDbEntry.LastAccessed = entry.LastAccessed;
            liteDbEntry.AbsoluteExpiration = entry.AbsoluteExpiration;
            liteDbEntry.SlidingExpiration = entry.SlidingExpiration;
            liteDbEntry.SetValue(entry.Value);
            return liteDbEntry;
        }

        public bool TryGetValue(object key, out object result)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var utcNow = _Options.Clock.UtcNow;
            result = null;
            bool found = false;
            PersistentCacheEntry expiredEntry = null;
            CheckDisposed();
            _EntryLock.EnterReadLock();
            try
            {
                PersistentCacheEntry entry;
                if (_InMemoryEntries.TryGetValue(key, out entry))
                {
                    // Check if expired due to expiration tokens, timers, etc. and if so, remove it.
                    if (entry.CheckExpired(utcNow))
                    {
                        expiredEntry = entry;
                    }
                    else
                    {
                        found = true;
                        entry.LastAccessed = utcNow;
                        result = entry.Value;
                    }
                }
            }
            finally
            {
                _EntryLock.ExitReadLock();
            }

            if (expiredEntry != null)
            {
                // TODO: For efficiency queue this up for batch removal
                RemoveEntry(expiredEntry);
            }

            StartScanForExpiredItems();

            return found;
        }

        public void Remove(object key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            CheckDisposed();
            PersistentCacheEntry entry;
            _EntryLock.EnterReadLock();
            try
            {
                if (_InMemoryEntries.TryGetValue(key, out entry))
                {
                    entry.SetExpired(EvictionReason.Removed);
                }
            }
            finally
            {
                _EntryLock.ExitReadLock();
            }

            if (entry != null)
            {
                // TODO: For efficiency consider processing these removals in batches.
                RemoveEntry(entry);
            }

            StartScanForExpiredItems();
        }

        private void RemoveEntry(PersistentCacheEntry entry)
        {
            _EntryLock.EnterWriteLock();
            try
            {
                // Only remove it if someone hasn't modified it since our lookup
                PersistentCacheEntry currentEntry;
                if (_InMemoryEntries.TryGetValue(entry.Key, out currentEntry)
                    && object.ReferenceEquals(currentEntry, entry))
                {
                    RemoveEntryFromMemoryAndStore(entry);
                    if (_Options.IsPersistent)
                    {
                        entry.PropertyChanged -= Entry_PropertyChanged;
                    }
                }
            }
            finally
            {
                _EntryLock.ExitWriteLock();
            }
            entry.InvokeEvictionCallbacks();
        }

        private void RemoveEntryFromMemoryAndStore(PersistentCacheEntry entry)
        {
            _InMemoryEntries.Remove(entry.Key);
            if (_Options.IsPersistent)
            {
                _Options.PersistentStore.RemoveEntry(entry.PersistentStoreId.Value);
            }
        }

        private void RemoveEntries(List<PersistentCacheEntry> entries)
        {
            _EntryLock.EnterWriteLock();
            try
            {
                foreach (var entry in entries)
                {
                    // Only remove it if someone hasn't modified it since our lookup
                    PersistentCacheEntry currentEntry;
                    if (_InMemoryEntries.TryGetValue(entry.Key, out currentEntry) && object.ReferenceEquals(currentEntry, entry))
                    {
                        RemoveEntryFromMemoryAndStore(entry);
                    }
                }
            }
            finally
            {
                _EntryLock.ExitWriteLock();
            }

            foreach (var entry in entries)
            {
                entry.InvokeEvictionCallbacks();
            }
        }

        internal void Clear()
        {
            RemoveEntries(_InMemoryEntries.Values.ToList());
        }

        private void EntryExpired(PersistentCacheEntry entry)
        {
            // TODO: For efficiency consider processing these expirations in batches.
            RemoveEntry(entry);
            StartScanForExpiredItems();
        }

        // Called by multiple actions to see how long it's been since we last checked for expired items.
        // If sufficient time has elapsed then a scan is initiated on a background task.
        private void StartScanForExpiredItems()
        {
            var now = _Options.Clock.UtcNow;
            if (_Options.ExpirationScanFrequency < now - _LastExpirationScan)
            {
                _LastExpirationScan = now;
                Task.Factory.StartNew(state => ScanForExpiredItems((PersistentMemoryCache)state), this, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            }
        }

        private static void ScanForExpiredItems(PersistentMemoryCache cache)
        {
            List<PersistentCacheEntry> expiredEntries = new List<PersistentCacheEntry>();

            cache._EntryLock.EnterReadLock();
            try
            {
                var now = cache._Options.Clock.UtcNow;
                foreach (var entry in cache._InMemoryEntries.Values)
                {
                    if (entry.CheckExpired(now))
                    {
                        expiredEntries.Add(entry);
                    }
                }
            }
            finally
            {
                cache._EntryLock.ExitReadLock();
            }

            cache.RemoveEntries(expiredEntries);
        }

        /// This is called after a Gen2 garbage collection. We assume this means there was memory pressure.
        /// Remove at least 10% of the total entries (or estimated memory?).
        private bool DoMemoryPreassureCollection(object state)
        {
            if (_Disposed)
            {
                return false;
            }

            Compact(0.10);

            return true;
        }

        /// Remove at least the given percentage (0.10 for 10%) of the total entries (or estimated memory?), according to the following policy:
        /// 1. Remove all expired items.
        /// 2. Bucket by CacheItemPriority.
        /// ?. Least recently used objects.
        /// ?. Items with the soonest absolute expiration.
        /// ?. Items with the soonest sliding expiration.
        /// ?. Larger objects - estimated by object graph size, inaccurate.
        public void Compact(double percentage)
        {
            List<PersistentCacheEntry> expiredEntries = new List<PersistentCacheEntry>();
            List<PersistentCacheEntry> lowPriEntries = new List<PersistentCacheEntry>();
            List<PersistentCacheEntry> normalPriEntries = new List<PersistentCacheEntry>();
            List<PersistentCacheEntry> highPriEntries = new List<PersistentCacheEntry>();
            List<PersistentCacheEntry> neverRemovePriEntries = new List<PersistentCacheEntry>();

            _EntryLock.EnterReadLock();
            try
            {
                // Sort items by expired & priority status
                var now = _Options.Clock.UtcNow;
                foreach (var entry in _InMemoryEntries.Values)
                {
                    if (entry.CheckExpired(now))
                    {
                        expiredEntries.Add(entry);
                    }
                    else
                    {
                        switch (entry.Priority)
                        {
                            case CacheItemPriority.Low:
                                lowPriEntries.Add(entry);
                                break;
                            case CacheItemPriority.Normal:
                                normalPriEntries.Add(entry);
                                break;
                            case CacheItemPriority.High:
                                highPriEntries.Add(entry);
                                break;
                            case CacheItemPriority.NeverRemove:
                                neverRemovePriEntries.Add(entry);
                                break;
                            default:
                                System.Diagnostics.Debug.Assert(false, "Not implemented: " + entry.Priority);
                                break;
                        }
                    }
                }

                int totalEntries = expiredEntries.Count + lowPriEntries.Count + normalPriEntries.Count + highPriEntries.Count + neverRemovePriEntries.Count;
                int removalCountTarget = (int)(totalEntries * percentage);

                ExpirePriorityBucket(removalCountTarget, expiredEntries, lowPriEntries);
                ExpirePriorityBucket(removalCountTarget, expiredEntries, normalPriEntries);
                ExpirePriorityBucket(removalCountTarget, expiredEntries, highPriEntries);
            }
            finally
            {
                _EntryLock.ExitReadLock();
            }

            RemoveEntries(expiredEntries);
        }

        /// Policy:
        /// ?. Least recently used objects.
        /// ?. Items with the soonest absolute expiration.
        /// ?. Items with the soonest sliding expiration.
        /// ?. Larger objects - estimated by object graph size, inaccurate.
        private void ExpirePriorityBucket(int removalCountTarget, List<PersistentCacheEntry> expiredEntries, List<PersistentCacheEntry> priorityEntries)
        {
            // Do we meet our quota by just removing expired entries?
            if (removalCountTarget <= expiredEntries.Count)
            {
                // No-op, we've met quota
                return;
            }
            if (expiredEntries.Count + priorityEntries.Count <= removalCountTarget)
            {
                // Expire all of the entries in this bucket
                foreach (var entry in priorityEntries)
                {
                    entry.SetExpired(EvictionReason.Capacity);
                }
                expiredEntries.AddRange(priorityEntries);
                return;
            }

            // Expire enough entries to reach our goal
            // TODO: Refine policy

            // LRU
            foreach (var entry in priorityEntries.OrderBy(entry => entry.LastAccessed))
            {
                entry.SetExpired(EvictionReason.Capacity);
                expiredEntries.Add(entry);
                if (removalCountTarget <= expiredEntries.Count)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }

                _Disposed = true;
            }
        }

        private void CheckDisposed()
        {
            if (_Disposed)
            {
                throw new ObjectDisposedException(typeof(PersistentMemoryCache).FullName);
            }
        }
    }
}