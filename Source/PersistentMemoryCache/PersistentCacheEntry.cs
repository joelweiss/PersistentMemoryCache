using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Caching.Memory;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PersistentMemoryCache
{
    public class PersistentCacheEntry : ICacheEntry, INotifyPropertyChanged
    {
        private bool _Added = false;
        private static readonly Action<object> _ExpirationCallback = ExpirationTokensExpired;
        private readonly Action<PersistentCacheEntry> _NotifyCacheOfExpiration;
        private readonly Action<PersistentCacheEntry> _NotifyCacheEntryDisposed;
        private IList<IDisposable> _ExpirationTokenRegistrations;
        private EvictionReason _EvictionReason;
        private IList<PostEvictionCallbackRegistration> _PostEvictionCallbacks;
        private bool _IsExpired;
        internal IList<IChangeToken> _ExpirationTokens;

        internal readonly object _Lock = new object();

        internal DateTimeOffset? _AbsoluteExpiration;
        internal TimeSpan? _AbsoluteExpirationRelativeToNow;
        private TimeSpan? _SlidingExpiration;
        DateTimeOffset _LastAccessed;
        object _Value;
        CacheItemPriority _Priority;

        public event PropertyChangedEventHandler PropertyChanged;

        internal PersistentCacheEntry(object key, Action<PersistentCacheEntry> notifyCacheEntryDisposed, Action<PersistentCacheEntry> notifyCacheOfExpiration)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            if (notifyCacheEntryDisposed == null)
            {
                throw new ArgumentNullException(nameof(notifyCacheEntryDisposed));
            }
            if (notifyCacheOfExpiration == null)
            {
                throw new ArgumentNullException(nameof(notifyCacheOfExpiration));
            }

            Key = key;
            _NotifyCacheEntryDisposed = notifyCacheEntryDisposed;
            _NotifyCacheOfExpiration = notifyCacheOfExpiration;

            Priority = CacheItemPriority.Normal;
        }

        private void SetValue<T>(T value, ref T field, [CallerMemberName]string propertyName = "")
        {
            if (!Equals(field, value))
            {
                field = value;
                if (_Added)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }
        }

        /// <summary>
        /// Gets or sets an absolute expiration date for the cache entry.
        /// </summary>
        public DateTimeOffset? AbsoluteExpiration
        {
            get
            {
                return _AbsoluteExpiration;
            }
            set
            {
                SetValue(value, ref _AbsoluteExpiration);
            }
        }

        /// <summary>
        /// Gets or sets an absolute expiration time, relative to now.
        /// </summary>
        public TimeSpan? AbsoluteExpirationRelativeToNow
        {
            get
            {
                return _AbsoluteExpirationRelativeToNow;
            }
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(AbsoluteExpirationRelativeToNow), value, "The relative expiration value must be positive.");
                }
                SetValue(value, ref _AbsoluteExpirationRelativeToNow);
            }
        }

        /// <summary>
        /// Gets or sets how long a cache entry can be inactive (e.g. not accessed) before it will be removed.
        /// This will not extend the entry lifetime beyond the absolute expiration (if set).
        /// </summary>
        public TimeSpan? SlidingExpiration
        {
            get
            {
                return _SlidingExpiration;
            }
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(SlidingExpiration),
                        value,
                        "The sliding expiration value must be positive.");
                }
                SetValue(value, ref _SlidingExpiration);
            }
        }

        /// <summary>
        /// Gets the <see cref="IChangeToken"/> instances which cause the cache entry to expire.
        /// </summary>
        public IList<IChangeToken> ExpirationTokens
        {
            get
            {
                if (_ExpirationTokens == null)
                {
                    _ExpirationTokens = new List<IChangeToken>();
                }
                return _ExpirationTokens;
            }
        }

        /// <summary>
        /// Gets or sets the callbacks will be fired after the cache entry is evicted from the cache.
        /// </summary>
        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks
        {
            get
            {
                if (_PostEvictionCallbacks == null)
                {
                    _PostEvictionCallbacks = new List<PostEvictionCallbackRegistration>();
                }

                return _PostEvictionCallbacks;
            }
        }

        /// <summary>
        /// Gets or sets the priority for keeping the cache entry in the cache during a
        /// memory pressure triggered cleanup. The default is <see cref="CacheItemPriority.Normal"/>.
        /// </summary>
        public CacheItemPriority Priority
        {
            get
            {
                return _Priority;
            }
            set
            {
                SetValue(value, ref _Priority);
            }
        }

        public object Key { get; private set; }

        public object Value
        {
            get
            {
                return _Value;
            }
            set
            {
                SetValue(value, ref _Value);
            }
        }

        internal DateTimeOffset LastAccessed
        {
            get
            {
                return _LastAccessed;
            }

            set
            {
                SetValue(value, ref _LastAccessed);
            }
        }

        internal int? PersistentStoreId { get; set; }

        public long? Size { get; set; }

        public void Dispose()
        {
            if (!_Added)
            {
                _Added = true;
                _NotifyCacheEntryDisposed(this);
            }
        }

        internal bool CheckExpired(DateTimeOffset now)
        {
            return _IsExpired || CheckForExpiredTime(now) || CheckForExpiredTokens();
        }

        internal void SetExpired(EvictionReason reason)
        {
            _IsExpired = true;
            if (_EvictionReason == EvictionReason.None)
            {
                _EvictionReason = reason;
            }
            DetachTokens();
        }

        private bool CheckForExpiredTime(DateTimeOffset now)
        {
            if (_AbsoluteExpiration.HasValue && _AbsoluteExpiration.Value <= now)
            {
                SetExpired(EvictionReason.Expired);
                return true;
            }

            if (_SlidingExpiration.HasValue && (now - LastAccessed) >= _SlidingExpiration)
            {
                SetExpired(EvictionReason.Expired);
                return true;
            }

            return false;
        }

        internal bool CheckForExpiredTokens()
        {
            if (_ExpirationTokens != null)
            {
                for (int i = 0; i < _ExpirationTokens.Count; i++)
                {
                    var expiredToken = _ExpirationTokens[i];
                    if (expiredToken.HasChanged)
                    {
                        SetExpired(EvictionReason.TokenExpired);
                        return true;
                    }
                }
            }
            return false;
        }

        internal void AttachTokens()
        {
            if (_ExpirationTokens != null)
            {
                lock (_Lock)
                {
                    for (int i = 0; i < _ExpirationTokens.Count; i++)
                    {
                        var expirationToken = _ExpirationTokens[i];
                        if (expirationToken.ActiveChangeCallbacks)
                        {
                            if (_ExpirationTokenRegistrations == null)
                            {
                                _ExpirationTokenRegistrations = new List<IDisposable>(1);
                            }
                            var registration = expirationToken.RegisterChangeCallback(_ExpirationCallback, this);
                            _ExpirationTokenRegistrations.Add(registration);
                        }
                    }
                }
            }
        }

        private static void ExpirationTokensExpired(object obj)
        {
            // start a new thread to avoid issues with callbacks called from RegisterChangeCallback
            Task.Factory.StartNew(state =>
            {
                var entry = (PersistentCacheEntry)state;
                entry.SetExpired(EvictionReason.TokenExpired);
                entry._NotifyCacheOfExpiration(entry);
            }, obj, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        private void DetachTokens()
        {
            lock (_Lock)
            {
                var registrations = _ExpirationTokenRegistrations;
                if (registrations != null)
                {
                    _ExpirationTokenRegistrations = null;
                    for (int i = 0; i < registrations.Count; i++)
                    {
                        var registration = registrations[i];
                        registration.Dispose();
                    }
                }
            }
        }

        internal void InvokeEvictionCallbacks()
        {
            if (_PostEvictionCallbacks != null)
            {
                Task.Factory.StartNew(state => InvokeCallbacks((PersistentCacheEntry)state), this, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            }
        }

        private static void InvokeCallbacks(PersistentCacheEntry entry)
        {
            var callbackRegistrations = Interlocked.Exchange(ref entry._PostEvictionCallbacks, null);

            if (callbackRegistrations == null)
            {
                return;
            }

            for (int i = 0; i < callbackRegistrations.Count; i++)
            {
                var registration = callbackRegistrations[i];

                try
                {
                    registration.EvictionCallback?.Invoke(entry.Key, entry.Value, entry._EvictionReason, registration.State);
                }
                catch (Exception)
                {
                    return;
                    // This will be invoked on a background thread, don't let it throw.
                    // TODO: LOG
                }
            }
        }
    }
}
