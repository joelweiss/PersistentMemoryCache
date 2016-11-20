using System;

namespace PersistentMemoryCache
{
    public class LiteDbOptions
    {
        public LiteDbOptions(string fileName)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }
            FileName = fileName;
        }

        public string FileName { get; }
    }
}