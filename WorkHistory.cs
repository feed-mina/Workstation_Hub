using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MinaDesk {
    public sealed class WorkEntry {
        public string Id { get; set; }
        public DateTime At { get; set; }
        public string Pc { get; set; }
        public string Source { get; set; }
        public string Detail { get; set; }
    }

    internal sealed class WorkHistory {
        internal const int Retention = 5000;
        readonly string file;
        readonly string device;
        readonly List<WorkEntry> entries;

        internal WorkHistory(string directory) {
            file = Path.Combine(directory, "work-history.json");
            var identity = Path.Combine(directory, "work-history-device.json");
            device = LocalData.Read(identity, () => Guid.NewGuid().ToString("N"));
            if (!File.Exists(identity)) LocalData.Write(identity, device);
            entries = LocalData.Read(file, () => new List<WorkEntry>());
        }

        internal WorkEntry[] Snapshot() { return entries.ToArray(); }

        internal void Add(string source, string detail) {
            var entry = new WorkEntry { Id = Guid.NewGuid().ToString("N"), At = DateTime.UtcNow,
                Pc = Environment.MachineName, Source = source, Detail = detail };
            var next = entries.Concat(new[] { entry }).Reverse().Take(Retention).Reverse().ToList();
            LocalData.Write(file, next);
            entries.Clear();
            entries.AddRange(next);
        }

        internal WorkEntry[] Sync(string directory, WorkEntry[] local, string publishedId) {
            Directory.CreateDirectory(directory);
            var ownFile = Path.Combine(directory, "hub-history-" + device + ".json");
            var latest = local.LastOrDefault();
            if (latest != null && (latest.Id != publishedId || !File.Exists(ownFile)))
                LocalData.Write(ownFile, local);
            var all = new List<WorkEntry>();
            foreach (var path in Directory.EnumerateFiles(directory, "hub-history-*.json")) {
                if (String.Equals(path, ownFile, StringComparison.OrdinalIgnoreCase)) continue;
                var remote = LocalData.Read(path, () => new WorkEntry[0]);
                all.AddRange(remote.Where(row => row != null && !String.IsNullOrEmpty(row.Id)));
            }
            return Recent(all);
        }

        internal static WorkEntry[] Recent(IEnumerable<WorkEntry> rows) {
            return rows.GroupBy(row => row.Id).Select(group => group.First())
                .OrderByDescending(row => row.At).ThenByDescending(row => row.Id, StringComparer.Ordinal)
                .Take(60).Reverse().ToArray();
        }
    }
}

