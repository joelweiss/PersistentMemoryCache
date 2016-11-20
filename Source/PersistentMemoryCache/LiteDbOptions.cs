using System;

namespace PersistentMemoryCache
{
    public class LiteDbOptions
    {
        public LiteDbOptions(string fileName)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }

        public string FileName { get; }
    }
}