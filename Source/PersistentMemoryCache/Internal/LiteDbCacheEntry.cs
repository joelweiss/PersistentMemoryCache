using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace PersistentMemoryCache.Internal
{
    public abstract class LiteDbCacheEntry
    {
        private static ConcurrentDictionary<Type, Delegate> _Constructors = new ConcurrentDictionary<Type, Delegate>();
        private static Type _LiteDbCacheEntryOpenType = typeof(LiteDbCacheEntry<>);
        private static Type[] _EmptyTypesArray = new Type[0];

        private TimeSpan? _SlidingExpiration;

        public int Id { get; set; }
        public string CacheName { get; set; }
        public CacheItemPriority Priority { get; set; }
        public object Key { get; set; }
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

        public static LiteDbCacheEntry ConstructCacheEntry(Type type) => (LiteDbCacheEntry)_Constructors.GetOrAdd(type, cacheType =>
        {
            Type cacheEntryClosedType = _LiteDbCacheEntryOpenType.MakeGenericType(type);
            ConstructorInfo constructor = cacheEntryClosedType.GetConstructor(_EmptyTypesArray);
            Type delegateType = typeof(Func<>).MakeGenericType(cacheEntryClosedType);
            LambdaExpression lambda = Expression.Lambda(delegateType, Expression.New(constructor));
            return lambda.Compile();
        }).DynamicInvoke();
        
        public abstract object GetValue();
        public abstract void SetValue(object value);
    }

    public class LiteDbCacheEntry<T> : LiteDbCacheEntry
    {
        public T Value { get; set; }

        public override object GetValue() => Value;

        public override void SetValue(object value) => Value = (T)value;
    }
}
