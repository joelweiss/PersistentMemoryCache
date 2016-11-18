namespace PersistentMemoryCache
{
    public class LiteDbOptions
    {
        public LiteDbOptions(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; }
    }
}