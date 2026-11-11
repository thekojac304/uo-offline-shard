using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Server.CustomBots;
using Xunit;
using static Server.Tests.CustomBots.BotTestSupport;

namespace Server.Tests.CustomBots;

[Collection("Sequential UOContent Tests")]
public class OfficialPopulationTests
{
    // Test clock controls Core.Now; the official curve uses DateTime.Now. Change
    // only the test process's curve entries, restoring them at disposal. Runtime
    // source/parameters remain byte-identical to official population management.
    private sealed class Scope : IDisposable
    {
        private readonly bool _sessions = BotSessionManager.Enabled, _regulars = BotRegulars.Enabled;
        private readonly int _target = BotPopulation.TargetCount;
        private readonly double[] _curve = (double[])typeof(BotSessionManager)
            .GetField("HourCurve", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        private readonly double[] _savedCurve;
        private readonly bool _stamped = (bool)typeof(BotSessionManager)
            .GetField("_initialStampDone", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        public readonly List<IEntity> Owned = new();

        public Scope(int target)
        {
            ResetClock();
            _savedCurve = (double[])_curve.Clone();
            Curve(1);
            BotSessionManager.Enabled = BotRegulars.Enabled = true;
            BotPopulation.TargetCount = target;
            typeof(BotSessionManager).GetField("_initialStampDone", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, false);
        }

        public void Curve(double fraction) => Array.Fill(_curve, fraction);
        public PlayerBot Regular(bool eligible = true)
        {
            var bot = new PlayerBot();
            Owned.Add(bot);
            bot.MoveToWorld(BritainBank, Map.Felucca);
            CallPrivateStatic(typeof(BotRegulars), "MakeRegular", bot);
            CallPrivateStatic(typeof(BotRegulars), "GoOffline", bot);
            bot.OfflineSince = Core.Now - (eligible ? TimeSpan.FromMinutes(21) : TimeSpan.Zero);
            return bot;
        }
        public PlayerBotSpawner Spawner(int amount, string role = "Wander", bool fixture = false)
        {
            var n = Owned.Count;
            var sp = CreateSpawner(role, amount,
                new Point3D(1433 + n % 12 * 3, 1689 + n / 12 * 3, 0), fixture, radius: 18);
            Owned.Add(sp);
            return sp;
        }
        public void Dispose()
        {
            foreach (var bot in World.Mobiles.Values.OfType<PlayerBot>().Where(b => b.Regular).ToArray()) Cleanup(bot);
            foreach (var entity in Owned) Cleanup(entity);
            Array.Copy(_savedCurve, _curve, _curve.Length);
            BotPopulation.TargetCount = _target;
            BotSessionManager.Enabled = _sessions;
            BotRegulars.Enabled = _regulars;
            typeof(BotSessionManager).GetField("_initialStampDone", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, _stamped);
        }
    }

    public static IEnumerable<object[]> Matrix()
    {
        foreach (int target in new[] { 500, 640, 1600 })
            foreach (int ready in new[] { 0, 1, 3, 4, 200 }) yield return new object[] { target, ready };
    }

    private static int Counted => NamePool.InUseCount - BotSessionManager.FixedRoleCount - BotRegulars.OfflineCount;
    private static int Census => World.Mobiles.Values.OfType<PlayerBot>()
        .Count(b => !b.Deleted && b.Map != Map.Internal && b.Map != null && !b.LifecycleExempt);
    private static void Tick() => CallPrivateStatic(typeof(BotSessionManager), "OnTick");

    [Theory]
    [MemberData(nameof(Matrix))]
    public void RapidRefill_ReservesOnlyNextThreeSeats_AndDoesNotOvershoot(int target, int ready)
    {
        using var s = new Scope(target);
        Assert.Equal(target, BotSessionManager.TargetNow);
        for (int i = 0; i < ready; i++) s.Regular();
        s.Regular(eligible: false);
        var fixtures = s.Spawner(5, "BankSitter", fixture: true);
        fixtures.Respawn();
        Assert.Equal(5, BotsOf(fixtures).Count);
        int baseline = Counted;
        var owners = new List<PlayerBotSpawner>();
        for (int i = 0; i < (target + 23) / 24; i++) owners.Add(s.Spawner(24));
        foreach (var sp in owners)
        {
            // Respawn's per-slot virtual Spawn invokes the official gate each time.
            sp.Respawn();
            Assert.InRange(Counted, baseline, target);
            Assert.Equal(Census, Counted);
        }
        Assert.Equal(Math.Min(ready, 3), BotRegulars.ReadyToHold());
        Assert.Equal(target - Math.Min(ready, 3), Counted);
        Assert.False(BotSessionManager.AllowSpawn());
        var snapshot = BotSpawnerStatsCommand.Compute();
        Assert.Equal(snapshot.GateCount + snapshot.HeldRegularSeats, snapshot.EffectiveGateCount);
        Assert.Equal(0, snapshot.All.StaleRefs);

        Tick(); // boot's first tick only stamps sessions
        int offline = BotRegulars.OfflineCount;
        Tick();
        Assert.Equal(offline - Math.Min(ready, 3), BotRegulars.OfflineCount);
        Assert.Equal(target, Counted);
        Assert.Equal(Census, Counted);
        foreach (var sp in owners) sp.Spawn();
        Assert.Equal(target, Counted);
    }

    [Fact]
    public void Finite200RegularCohort_ReturnsIn67NormalTicks_WithoutChangingIdentity()
    {
        using var s = new Scope(640);
        var regulars = Enumerable.Range(0, 200).Select(_ => s.Regular()).ToArray();
        var names = regulars.Select(b => b.Name).ToArray();
        Tick();
        for (int minute = 0; minute < 67; minute++)
        {
            int before = BotRegulars.OfflineCount;
            Tick();
            Assert.Equal(Math.Min(before, 3), before - BotRegulars.OfflineCount);
            // Avoid session churn: clock stays fixed here; pacing is per call,
            // tested independently from minimum offline wall time below.
        }
        Assert.Equal(0, BotRegulars.OfflineCount);
        Assert.Equal(names, regulars.Select(b => b.Name).ToArray());
        Assert.All(regulars, b => Assert.False(b.Deleted));
    }

    [Fact]
    public void MinimumOfflineTime_AndDisabledRegulars_HoldNoPrematureSeats()
    {
        using var s = new Scope(500);
        var r = s.Regular(eligible: false);
        Assert.Equal(0, BotRegulars.ReadyToHold());
        Core._now += TimeSpan.FromMinutes(20) - TimeSpan.FromTicks(1);
        Assert.Equal(0, BotRegulars.ReadyToHold());
        Assert.Equal(0, BotRegulars.LogInSome(10));
        Core._now += TimeSpan.FromTicks(1);
        Assert.Equal(1, BotRegulars.ReadyToHold());
        BotRegulars.Enabled = false;
        Assert.Equal(0, BotRegulars.ReadyToHold());
        Assert.Equal(0, BotRegulars.LogInSome(10));
        BotRegulars.Enabled = true;
        r.Delete();
        Assert.Equal(0, BotRegulars.ReadyToHold());
    }

    [Fact]
    public void RealSessionTimer_StampsFirst_ThenReturnsAtMostThreePerMinute()
    {
        using var s = new Scope(500);
        for (int i = 0; i < 10; i++) s.Regular();
        BotSessionManager.Configure();
        try
        {
            Advance(TimeSpan.FromSeconds(59));
            Assert.Equal(10, BotRegulars.OfflineCount);
            Advance(TimeSpan.FromSeconds(2)); // initial stamp at 60 seconds
            Assert.Equal(10, BotRegulars.OfflineCount);
            Advance(TimeSpan.FromSeconds(60));
            Assert.Equal(7, BotRegulars.OfflineCount);
            Advance(TimeSpan.FromSeconds(60));
            Assert.Equal(4, BotRegulars.OfflineCount);
        }
        finally
        {
            (typeof(BotSessionManager).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null) as Timer)?.Stop();
        }
    }

    [Fact]
    public void FreedNearCapPlaces_GoToRegularsBeforeOrdinaryRefills()
    {
        using var s = new Scope(500);
        for (int i = 0; i < 20; i++) s.Regular();
        var owner = s.Spawner(600);
        owner.Respawn();
        Assert.Equal(497, Counted);
        Tick();
        Tick();
        Assert.Equal(500, Counted);
        foreach (var bot in BotsOf(owner).Take(12)) bot.Delete();
        for (int i = 0; i < 50; i++) owner.Spawn();
        Assert.Equal(497, Counted);
        int before = BotRegulars.OfflineCount;
        Tick();
        Assert.Equal(3, before - BotRegulars.OfflineCount);
        Assert.Equal(500, Counted);
    }

    [Fact]
    public void CurveDropAndPkFixtures_AreExplicitExcursions_NotGatedOvershoot()
    {
        using var s = new Scope(1600);
        var ordinary = s.Spawner(700);
        ordinary.Respawn();
        Assert.Equal(700, Counted);
        s.Curve(0.4);
        Assert.Equal(640, BotSessionManager.TargetNow);
        Assert.False(BotSessionManager.AllowSpawn());
        ordinary.Spawn();
        Assert.Equal(700, Counted); // existing excess is not immediately deleted
        var pk = s.Spawner(2, "PK");
        pk.Respawn();
        Assert.Equal(702, Counted); // upstream's explicit PK exemption
        var fixtures = s.Spawner(3, "BankSitter", fixture: true);
        fixtures.Respawn();
        Assert.Equal(702, Counted); // fixtures excluded
        Assert.Equal(Census, Counted);
        s.Curve(0.15);
        Assert.Equal(500, BotSessionManager.TargetNow);
        s.Curve(1);
        Assert.True(BotSessionManager.AllowSpawn());
    }
}
