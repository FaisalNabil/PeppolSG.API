using System.Collections.Generic;
using System.Collections.Concurrent;

namespace PeppolSG.API.Logging
{
    public static class MetricsService
    {
        private static readonly ConcurrentDictionary<string, long> _counters = new ConcurrentDictionary<string, long>();

        public static void Increment(string name, long value = 1)
        {
            _counters.AddOrUpdate(name, value, (_, old) => old + value);
        }

        public static long Get(string name) => _counters.TryGetValue(name, out var v) ? v : 0;

        public static Dictionary<string, long> GetAll() => new Dictionary<string, long>(_counters);
    }
} 