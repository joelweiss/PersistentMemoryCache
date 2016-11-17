[![Build status](https://ci.appveyor.com/api/projects/status/0sfvtqnag0x50pax?svg=true)](https://ci.appveyor.com/project/joelweiss/persistentmemorycache)
[![NuGet Badge](https://buildstats.info/nuget/PersistentMemoryCache?includePreReleases=true)](https://www.nuget.org/packages/PersistentMemoryCache/)

# Persistent Memory Cache

Caches in Memory but also to Disk, so cache is persistent across restarts, build on Top of [Microsoft.Extensions.Caching.Abstractions](https://github.com/aspnet/Caching) and [LiteDb](https://github.com/mbdavid/LiteDB)

# Installation
```powershell
PM> Install-Package PersistentMemoryCache -Pre
```
# Example
```csharp
IMemoryCache cache = new PersistentMemoryCache(new PersistentMemoryCacheOptions("Test", new LiteDbStore(new LiteDbOptions { FileName = "Test.db" })));

string key = "TestKey";
string value = "TestValue";
cache.Set(key, value);

var retrieve = cache.Get(key);
```
