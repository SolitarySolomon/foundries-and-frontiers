using System;
using System.Collections.Generic;
using System.Text;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Counters for the things this mod intends to drive toward zero.
    ///
    /// The design calls for villagers that walk rather than teleport, and builds that
    /// finish rather than stall. Neither is verifiable by watching, so they need a number
    /// that trends. Instrumented from the first day rather than bolted on once something
    /// already feels wrong.
    /// </summary>
    public static class DevStats
    {
        private static readonly Dictionary<string, long> counters = new Dictionary<string, long>();
        private static DateTime since = DateTime.UtcNow;

        // Names are declared here rather than passed as loose strings, so a typo
        // can't silently create a second counter that nobody ever reads.
        public const string RecoveryTeleports = "path.recovery_teleports";
        public const string PathsFailed = "path.failed";
        public const string PathsFound = "path.found";
        public const string TasksStarted = "ai.tasks_started";
        public const string BuildsStalled = "build.stalled_no_materials";
        public const string DepositsMade = "work.deposits";
        public const string VillagersSpawned = "village.villagers_spawned";
        public const string VillagersDied = "village.villagers_died";

        public static void Bump(string counter, long by = 1)
        {
            lock (counters)
            {
                counters.TryGetValue(counter, out long current);
                counters[counter] = current + by;
            }
        }

        public static long Get(string counter)
        {
            lock (counters)
            {
                counters.TryGetValue(counter, out long current);
                return current;
            }
        }

        public static void Reset()
        {
            lock (counters) { counters.Clear(); }
            since = DateTime.UtcNow;
        }

        public static string Report()
        {
            lock (counters)
            {
                if (counters.Count == 0) return "No counters recorded yet.";

                var sb = new StringBuilder();
                sb.AppendLine("Counters (since " + since.ToString("HH:mm:ss") + " UTC, "
                              + (int)(DateTime.UtcNow - since).TotalMinutes + " min):");

                var keys = new List<string>(counters.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string k in keys)
                {
                    sb.AppendLine("  " + k.PadRight(34) + counters[k]);
                }

                // The ratio that actually matters: how often pathfinding gives up.
                long found = Get(PathsFound), failed = Get(PathsFailed);
                if (found + failed > 0)
                {
                    float rate = 100f * failed / (found + failed);
                    sb.AppendLine("  path failure rate               " + rate.ToString("0.0") + "%");
                }
                return sb.ToString().TrimEnd();
            }
        }
    }
}
