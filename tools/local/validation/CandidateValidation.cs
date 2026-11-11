// Test-only assembly. Never part of the candidate's deployable source/artifact.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server;
using Server.CustomBots;
using Server.Items;
using Server.Mobiles;
using Server.Multis;

namespace UOOffline.Validation;

public static class CandidateValidation
{
    private static bool On => Environment.GetEnvironmentVariable("UO_CANDIDATE_VALIDATION") == "1";
    private static string Root => Path.GetFullPath(Core.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static readonly Stopwatch Clock = new();

    [CallPriority(0)]
    public static void Configure()
    {
        if (!On) throw new InvalidOperationException("Remove CandidateValidation.dll outside an authorized isolated trial.");
        var expected = Environment.GetEnvironmentVariable("UO_CANDIDATE_ROOT");
        if (expected == null || !string.Equals(Root, Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(Root, ".isolated-uo-candidate")))
            throw new InvalidOperationException("Candidate runtime root/marker mismatch.");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "Configuration", "modernuo.json")));
        foreach (var listener in doc.RootElement.GetProperty("listeners").EnumerateArray())
            if (listener.GetString() != "127.0.0.1:2694") throw new InvalidOperationException("Non-isolated listener.");
        foreach (var key in new[] { "world.savePath", "autoArchive.backupPath", "autoArchive.archivePath" })
        {
            var path = Path.GetFullPath(ServerConfiguration.GetSetting(key, ""), Root);
            if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"External write path: {key}");
        }
        foreach (var key in new[] { "uogateway.enabled", "world.enableAutoRestart", "crashGuard.enabled", "crashGuard.restartServer" })
            if (ServerConfiguration.GetSetting(key, true)) throw new InvalidOperationException($"External integration enabled: {key}");
        // The pinned engine's PingServer.Shutdown assumes Start populated its
        // listener array. A private ping listener avoids that existing null bug.
        if (!ServerConfiguration.GetSetting("pingServer.enabled", false) || ServerConfiguration.GetSetting("pingServer.port", 0) != 2695)
            throw new InvalidOperationException("Expected isolated loopback ping port 2695.");
    }

    [CallPriority(1)]
    public static void Initialize()
    {
        if (!On) return;
        Clock.Start();
        Sample("loaded-before-bot-startup");
        Timer.DelayCall(TimeSpan.FromSeconds(45), () => Sample("settled"));
        Timer.DelayCall(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), () => Sample("minute"));
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("UO_CANDIDATE_SECONDS"), out var value) ? Math.Max(120, value) : 180;
        Timer.DelayCall(TimeSpan.FromSeconds(seconds), () =>
        {
            Sample("before-save");
            World.Save();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                World.WaitForWriteCompletion();
                Core.Kill(false);
            });
        });
    }

    private static string Hash(System.Collections.Generic.IEnumerable<string> entries) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', entries.OrderBy(s => s, StringComparer.Ordinal)))));

    private static string Contents(Container container)
    {
        if (container == null) return "none";
        return $"{container.Serial}:" + string.Join(';', container.Items.OrderBy(i => i.Serial.Value)
            .Select(i => $"{i.Serial}:{i.GetType().FullName}:{i.Amount}" + (i is Container c ? $"[{Contents(c)}]" : "")));
    }

    private static void Sample(string phase)
    {
        var bots = World.Mobiles.Values.OfType<PlayerBot>().Where(b => !b.Deleted).ToArray();
        var regulars = bots.Where(b => b.Regular).ToArray();
        var permanent = bots.Where(b => b.IsPermanent).ToArray();
        var houses = World.Items.Values.OfType<BaseHouse>().Where(h => !h.Deleted).ToArray();
        var vendors = World.Mobiles.Values.OfType<PlayerVendor>().Where(v => !v.Deleted).ToArray();
        using var process = Process.GetCurrentProcess();
        var snapshot = new
        {
            at = DateTime.UtcNow, phase, elapsedSeconds = Clock.Elapsed.TotalSeconds,
            pid = Environment.ProcessId, cpuMs = process.TotalProcessorTime.TotalMilliseconds,
            privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64,
            gcHeap = GC.GetTotalMemory(false), allocatedBytes = GC.GetTotalAllocatedBytes(false),
            gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2),
            accounts = Server.Accounting.Accounts.Count,
            players = World.Mobiles.Values.Count(m => m is PlayerMobile && m is not PlayerBot && !m.Deleted),
            regulars = regulars.Length, permanent = permanent.Length, houses = houses.Length, vendors = vendors.Length,
            hunts = BotPartyManager.CountKind(BotPartyKind.Hunt),
            convoys = BotPartyManager.CountKind(BotPartyKind.Convoy),
            dungeonBots = phase == "loaded-before-bot-startup" ? 0 : bots.Count(b => DungeonRegistry.IsInDungeon(b)),
            regularIdentityHash = Hash(regulars.Select(b => $"{b.Serial}:{b.Name}:{b.BotGuildIndex}:{b.GuildBound}")),
            permanentIdentityHash = Hash(permanent.Select(b => $"{b.Serial}:{b.Name}:{b.BotGuildIndex}:{b.GuildBound}")),
            regularBanksHash = Hash(regulars.Select(b => $"{b.Serial}:{Contents(b.FindItemOnLayer(Layer.Bank) as Container)}")),
            playerBanksHash = Hash(World.Mobiles.Values.OfType<PlayerMobile>().Where(b => b is not PlayerBot && !b.Deleted)
                .Select(b => $"{b.Serial}:{Contents(b.FindItemOnLayer(Layer.Bank) as Container)}")),
            housesHash = Hash(houses.Select(h => $"{h.Serial}:{h.GetType().FullName}:{h.Owner?.Serial}:{h.Location}:{h.Map}")),
            vendorsHash = Hash(vendors.Select(v => $"{v.Serial}:{v.Owner?.Serial}:{v.House?.Serial}")),
            playersHash = Hash(World.Mobiles.Values.OfType<PlayerMobile>().Where(p => p is not PlayerBot && !p.Deleted)
                .Select(p => $"{p.Serial}:{p.Name}:{p.Account?.Username}:{Contents(p.Backpack)}")),
            playerDetail = World.Mobiles.Values.OfType<PlayerMobile>().Where(p => p is not PlayerBot && !p.Deleted)
                .Select(p => $"{p.Serial}|{p.Name}|{p.Account?.Username}|{p.Map}|{p.Location}|{Contents(p.Backpack)}").ToArray(),
            playerHouses = houses.Count(h => h.Owner is PlayerMobile and not PlayerBot),
            ownerlessHouses = houses.Count(h => h.Owner == null),
            botHouses = houses.Count(h => h.Owner is PlayerBot),
            houseLocationDuplicates = houses.GroupBy(h => (h.Map, h.X, h.Y)).Count(g => g.Count() > 1),
            spotHouses = BotVendorHouses.LoadSpots().ToDictionary(s => s.Name,
                s => houses.Count(h => h.Map == Map.Felucca && BotHousing.InPolygon(s.Poly, h.X, h.Y))),
            spotsFilledRecord = File.Exists(Path.Combine(Root, "Data", "Live", "house_spots_filled.txt"))
                ? File.ReadAllLines(Path.Combine(Root, "Data", "Live", "house_spots_filled.txt")).Length : -1,
            spawners = World.Items.Values.OfType<Server.Engines.Spawners.BaseSpawner>().Count(s => !s.Deleted),
            spawnersHash = Hash(World.Items.Values.OfType<Server.Engines.Spawners.BaseSpawner>().Where(s => !s.Deleted)
                .Select(s => $"{s.Serial}:{s.GetType().Name}:{s.Location}:{s.Count}")),
            stats = BotSpawnerStatsCommand.Compute()
        };
        var output = Path.Combine(Root, "Data", "Live", "candidate-validation.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.AppendAllText(output, JsonSerializer.Serialize(snapshot, Json) + Environment.NewLine);
        Console.WriteLine($"[candidate-validation] {phase}: regulars={regulars.Length}, houses={houses.Length}, vendors={vendors.Length}");
    }
}
