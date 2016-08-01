using System;
using LiteDB;
using System.IO;

namespace PersistentMemoryCache.Internal
{
    internal class PersistentLiteDatabase : LiteDB.LiteDatabase
    {
        static PersistentLiteDatabase()
        {
            BsonMapper.Global.RegisterType<TimeSpan>
            (
                serialize: (ts) => new BsonValue(ts.TotalMilliseconds),
                deserialize: (bson) => TimeSpan.FromMilliseconds(bson.AsInt32)
            );
            BsonMapper.Global.RegisterType<DateTimeOffset>
            (
                serialize: (dto) => new BsonValue(dto.UtcDateTime),
                deserialize: (bson) => bson.AsDateTime.ToUniversalTime()
            );
        }

        /// <summary>
        /// Starts LiteDB database using a connection string for filesystem database
        /// </summary>
        internal PersistentLiteDatabase(string connectionString, BsonMapper mapper = null) : base(connectionString, mapper)
        {

        }

        /// <summary>
        /// Initialize database using any read/write Stream (like MemoryStream)
        /// </summary>
        internal PersistentLiteDatabase(Stream stream, BsonMapper mapper = null) : base(stream, mapper)
        {

        }

        /// <summary>
        /// Starts LiteDB database using full parameters
        /// </summary>
        internal PersistentLiteDatabase(IDiskService diskService, BsonMapper mapper = null) : base(diskService, mapper)
        {

        }
    }
}
