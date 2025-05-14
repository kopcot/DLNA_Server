using DLNAServer.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Helpers.Database.Conversions
{
    public class StringCacheConverter : ValueConverter<string?, string?>
    {
        private static readonly MemoryCache _cache = new(
            new MemoryCacheOptions()
            {
                Clock = new Microsoft.Extensions.Internal.SystemClock(),
                ExpirationScanFrequency = TimeSpanValues.TimeMin1,
            });
        public StringCacheConverter()
            : base(
                  convertToProviderExpression: static (value) => value,
                  convertFromProviderExpression: static (value) => StringCache(value))
        {
        }
        private static string? StringCache(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return _cache.GetOrCreate(
                GetFnv1aHash(value),
                GetCacheEntry(value));
        }

        private static Func<ICacheEntry, string> GetCacheEntry(string value)
        {
            return entry =>
            {
                entry.Value = value;
                entry.SlidingExpiration = TimeSpanValues.TimeHours1;
                entry.AbsoluteExpirationRelativeToNow = TimeSpanValues.TimeDays1;
                entry.Size = value.Length;

                return value;
            };
        }

        private static string GetFnv1aHash(string value)
        {
            ulong hash = 2166136261;  // FNV-1a initial value
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 16777619;  // FNV-1a prime
            }
            return hash.ToString("X");
        }
    }
}
