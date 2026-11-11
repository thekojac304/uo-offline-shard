using System;
using System.Collections.Generic;
using Server.CustomBots;
using Xunit;
using Xunit.Abstractions;
using static Server.Tests.CustomBots.BotTestSupport;

namespace Server.Tests.CustomBots;

// Accelerated soak: ten game-hours of the real session layer (1-4h sessions,
// 4 logouts a minute, regulars keeping their identity, the curve gate) over
// ordinary, PK and fixed-role spawners, run on the virtual clock in seconds.
//
// This is the scenario that drained the live shard: every spawner fills,
// sessions end, and only a spawner that hears about the deleted bot can
// refill. The checks run every ten game-minutes.
[Collection("Sequential UOContent Tests")]
public class PopulationSimulationTests
{
    private readonly ITestOutputHelper _out;

    public PopulationSimulationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TenGameHours_PopulationHoldsAndEverySpawnerKeepsRefilling()
    {
        var sessions = BotSessionManager.Enabled;
        var regulars = BotRegulars.Enabled;
        var target = BotPopulation.TargetCount;
        BotSessionManager.Enabled = true;
        BotRegulars.Enabled = true;
        BotPopulation.TargetCount = 150; // MaxRegulars = 50; the gate allows 150 named bots online
        ResetClock();

        var at = BritainBank;
        var ordinary = new List<PlayerBotSpawner>();
        var pks = new List<PlayerBotSpawner>();
        PlayerBotSpawner fixtures = null;
        var names0 = NamePool.InUseCount;
        var fixed0 = BotSessionManager.FixedRoleCount;
        var regularNames = new Dictionary<Serial, string>();
        var regularBots = new HashSet<PlayerBot>();
        try
        {
            for (var i = 0; i < 20; i++)
            {
                var sp = CreateSpawner("Traveler", 5, new Point3D(at.X + i % 5, at.Y + i / 5, at.Z));
                ordinary.Add(sp);
            }
            for (var i = 0; i < 2; i++)
            {
                pks.Add(CreateSpawner("PK", 4, new Point3D(at.X - 10, at.Y - 10 + i, at.Z)));
            }
            fixtures = CreateSpawner("BankSitter", 5, at, fixedRole: true);

            // Boot: the startup respawn pass, then the session manager's clock.
            foreach (var sp in ordinary) { sp.Respawn(); }
            foreach (var sp in pks) { sp.Respawn(); }
            fixtures.Respawn();
            BotSessionManager.Configure();

            int minLive = int.MaxValue, maxStalled = 0, samples = 0;
            for (var minute = 1; minute <= 600; minute++)
            {
                Advance(TimeSpan.FromMinutes(1));

                // Regulars must stay the same person every time they come back.
                foreach (var m in World.Mobiles.Values)
                {
                    if (m is PlayerBot { Deleted: false, Regular: true } r)
                    {
                        regularBots.Add(r);
                        if (regularNames.TryGetValue(r.Serial, out var n))
                        {
                            Assert.Equal(n, r.Name);
                        }
                        else
                        {
                            regularNames[r.Serial] = r.Name;
                        }
                    }
                }

                if (minute % 10 != 0)
                {
                    continue;
                }

                int live = 0, onInternal = 0;
                foreach (var m in World.Mobiles.Values)
                {
                    if (m is PlayerBot { Deleted: false } b)
                    {
                        if (b.Map == Map.Internal) { onInternal++; } else { live++; }
                    }
                }

                int stalled = 0;
                foreach (var sp in ordinary)
                {
                    if (BotsOf(sp).Count < sp.Count && !TimerArmed(sp) && BotSessionManager.AllowSpawn())
                    {
                        stalled++;
                    }
                }
                foreach (var sp in pks)
                {
                    if (BotsOf(sp).Count < sp.Count && !TimerArmed(sp))
                    {
                        stalled++;
                    }
                }

                samples++;
                maxStalled = Math.Max(maxStalled, stalled);
                if (minute >= 60)
                {
                    minLive = Math.Min(minLive, live);
                }

                // Names: one claim per bot that exists, online or parked.
                Assert.Equal(names0 + live + onInternal, NamePool.InUseCount);
                // Fixtures never log out and never miscount.
                Assert.Equal(5, BotsOf(fixtures).Count);
                Assert.Equal(fixed0 + 5, BotSessionManager.FixedRoleCount);

                if (minute % 60 == 0)
                {
                    _out.WriteLine(
                        $"t={minute / 60,2}h live={live} offline-regulars={BotRegulars.OfflineCount} " +
                        $"regulars={BotRegulars.TotalCount} stalled={stalled} gate={(BotSessionManager.AllowSpawn() ? "open" : "closed")}"
                    );
                }
            }

            // Ten hours of 1-4h sessions is several full turnovers of every slot.
            Assert.Equal(0, maxStalled);
            _out.WriteLine($"lowest population after the first hour: {minLive}");
            Assert.True(minLive >= 90, $"population fell to {minLive} (113 spawner slots + regulars)");
            Assert.True(regularNames.Count >= 20, $"only {regularNames.Count} regulars formed");
            foreach (var sp in pks)
            {
                Assert.NotEmpty(BotsOf(sp));
            }
        }
        finally
        {
            foreach (var r in regularBots)
            {
                Cleanup(r);
            }
            foreach (var sp in ordinary) { Cleanup(sp); }
            foreach (var sp in pks) { Cleanup(sp); }
            Cleanup(fixtures);
            // Park the session manager's clock again; later tests slice the same wheel.
            (typeof(BotSessionManager)
                .GetField("_timer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null) as Timer)?.Stop();
            BotSessionManager.Enabled = sessions;
            BotRegulars.Enabled = regulars;
            BotPopulation.TargetCount = target;
        }
    }
}
