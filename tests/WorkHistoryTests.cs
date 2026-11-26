using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MinaDesk;

static class WorkHistoryTests {
    static void Require(bool condition, string message) {
        if (!condition) throw new Exception(message);
    }

    static void Main() {
        var root = Path.Combine(Path.GetTempPath(), "Hub-history-test-" + Guid.NewGuid().ToString("N"));
        var pcA = Path.Combine(root, "a");
        var pcB = Path.Combine(root, "b");
        var share = Path.Combine(root, "share");
        var a = new WorkHistory(pcA);
        var b = new WorkHistory(pcB);
        a.Add("매크로", "업무 시작");
        b.Add("PC 명령", "PC 잠금");
        a = new WorkHistory(pcA);
        Require(a.Snapshot().Single().Detail == "업무 시작", "History did not survive restart.");
        Parallel.Invoke(() => a.Sync(share, a.Snapshot(), null), () => b.Sync(share, b.Snapshot(), null));
        var fromA = WorkHistory.Recent(a.Snapshot().Concat(a.Sync(share, a.Snapshot(), null)));
        var fromB = WorkHistory.Recent(b.Snapshot().Concat(b.Sync(share, b.Snapshot(), null)));
        Require(fromA.Length == 2 && fromA.Select(row => row.Id).SequenceEqual(fromB.Select(row => row.Id)), "Both views must agree.");
        var blocked = Path.Combine(root, "offline");
        File.WriteAllText(blocked, "unavailable");
        var published = a.Snapshot().Last().Id;
        a.Add("매크로", "퇴근");
        try { a.Sync(blocked, a.Snapshot(), published); throw new Exception("Expected share failure."); }
        catch (IOException) {}
        a = new WorkHistory(pcA);
        Require(a.Snapshot().Last().Detail == "퇴근", "Offline entry did not survive.");
        a.Sync(share, a.Snapshot(), published);
        Require(b.Sync(share, b.Snapshot(), null).Any(row => row.Detail == "퇴근"), "Reconnected PC did not publish offline history.");
        Console.WriteLine("PASS: restart, concurrent publication, identical views, offline recovery");
    }
}

