using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;

namespace PersistentMemoryCache.Tests
{
    public class Tests
    {
        private static PersistentMemoryCache GetCache() => new PersistentMemoryCache(new PersistentMemoryCacheOptions("Test", new LiteDbStore(new LiteDbOptions("Test.db"))));

        [Fact]
        public void InsertAndRetrieveString()
        {
            IMemoryCache cache = GetCache();
            string key = "TestKey";
            string value = "TestValue";
            cache.Set(key, value);
            cache.Dispose();
            cache = null;
            cache = GetCache();

            string result = cache.Get<string>(key);
            result.Should().NotBeNull();
            result.Should().Equals("TestValue");
        }


        [Fact]
        public void InsertAndRetrieveListOfStrings()
        {
            IMemoryCache cache = GetCache();
            string key = "TestListKey";
            List<string> value = new List<string> { "Value1", "Value2" };
            cache.Set(key, value);
            cache.Dispose();
            cache = null;
            cache = GetCache();
            
            var result = cache.Get<List<string>>(key);
            result.Should().NotBeNull();
            result.ShouldBeEquivalentTo(value);
        }

        [Fact]
        public void InsertAndRetrieveEmptyList()
        {
            IMemoryCache cache = GetCache();
            string key = "TestEmptyListKey";
            List<string> value = new List<string>();
            cache.Set(key, value);
            cache.Dispose();
            cache = null;
            cache = GetCache();

            var result = cache.Get<List<string>>(key);
            result.Should().NotBeNull();
            result.ShouldBeEquivalentTo(value);
        }

        [Fact]
        public void InsertAndRetrieveCustomType()
        {
            IMemoryCache cache = GetCache();
            string key = "TestCustomTypeKey";
            Customer value = new Customer { CustomerId = 1, Name = "Foo" };
            cache.Set(key, value);
            cache.Dispose();
            cache = null;
            cache = GetCache();

            var result = cache.Get(key);
            result.Should().NotBeNull();
            result.ShouldBeEquivalentTo(value);
        }

        public class Customer
        {
            public int CustomerId { get; set; }
            public string Name { get; set; }
        }
    }
}
