using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PersistentMemoryCache.Tests
{
    public class Class1
    {
        [Fact]
        public void Foo()
        {
            IMemoryCache cache = new PersistentMemoryCache(new PersistentMemoryCacheOptions("Test", new LiteDbStore(new LiteDbOptions { FileName = "Test.db" })));
            string key = "TestKey";
            string value = "TestValue";
            cache.Set(key, value);
            cache.Dispose();
            cache = null;
            cache = new PersistentMemoryCache(new PersistentMemoryCacheOptions("Test", new LiteDbStore(new LiteDbOptions { FileName = "Test.db" })));


            var ret = cache.Get(key);
            Assert.NotNull(ret);
            Assert.Equal(ret, "TestValue");
        }
    }
}
