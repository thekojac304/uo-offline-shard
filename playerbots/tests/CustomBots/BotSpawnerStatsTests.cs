using System;
using System.Reflection;
using Server.CustomBots;
using Server.Engines.Spawners;
using Xunit;
using static Server.Tests.CustomBots.BotTestSupport;

namespace Server.Tests.CustomBots;

[Collection("Sequential UOContent Tests")]
public class BotSpawnerStatsTests
{
    [Fact]
    public void DetachedButUndeletedReference_IsReportedWithoutDefragOrDeletion()
    {
        var sessions = BotSessionManager.Enabled;
        BotSessionManager.Enabled = false;
        ResetClock();
        var before = BotSpawnerStatsCommand.Compute();
        var sp = CreateSpawner("Wander", 1, BritainBank);
        PlayerBot bot = null;
        try
        {
            sp.Respawn();
            bot = Assert.Single(BotsOf(sp));
            bot.Spawner = null; // simulate a mismatched live ownership reference
            var snapshot = BotSpawnerStatsCommand.Compute();
            Assert.Equal(before.All.StaleRefs + 1, snapshot.All.StaleRefs);
            Assert.Equal(before.All.Filled, snapshot.All.Filled);
            Assert.Contains(bot, sp.Spawned.Keys); // report must not defrag
            Assert.False(bot.Deleted);
        }
        finally { Cleanup(sp, bot); BotSessionManager.Enabled = sessions; }
    }
    // Park a spawner's refill timer without touching anything else, so the
    // "stalled" state can be built the same way with or without other fixes.
    private static void StopTimer(BaseSpawner sp)
    {
        var f = typeof(BaseSpawner).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance);
        (f!.GetValue(sp) as Timer)?.Stop();
    }

    [Fact]
    public void Compute_ClassifiesTimers_AndChangesNothing()
    {
        var sessions = BotSessionManager.Enabled;
        BotSessionManager.Enabled = false;
        ResetClock();
        var before = BotSpawnerStatsCommand.Compute();

        var full = CreateSpawner("Traveler", 2, BritainBank);
        var armed = CreateSpawner("Traveler", 2, BritainBank);
        var stalled = CreateSpawner("Traveler", 2, BritainBank);
        var off = CreateSpawner("Traveler", 1, BritainBank);
        try
        {
            full.Respawn();                        // 2/2, timer parked
            armed.Respawn();
            armed.Count = 3;                       // grows to 3: one empty slot, timer running
            stalled.Respawn();
            BotsOf(stalled)[0].Delete();
            StopTimer(stalled);                    // empty slot, no timer
            off.Running = false;

            var spawnedBefore = stalled.Spawned.Count;
            var nextBefore = stalled.NextSpawn;

            var s = BotSpawnerStatsCommand.Compute();

            Assert.Equal(before.All.Spawners + 4, s.All.Spawners);
            Assert.Equal(before.All.Full + 1, s.All.Full);
            Assert.Equal(before.All.Armed + 1, s.All.Armed);
            Assert.Equal(before.All.Stalled + 1, s.All.Stalled);
            Assert.Equal(before.All.Off + 1, s.All.Off);
            Assert.Equal(before.All.Capacity + 2 + 3 + 2 + 1, s.All.Capacity);
            Assert.Equal(before.All.Filled + 2 + 2 + 1, s.All.Filled);

            Assert.Equal(s.NamesInUse - s.FixedRoleCount - s.OfflineRegulars, s.GateCount);
            Assert.Equal(BotSessionManager.AllowSpawn(), s.AllowSpawn);

            // Read-only: the report did not defrag, rearm or respawn anything.
            Assert.Equal(spawnedBefore, stalled.Spawned.Count);
            Assert.Equal(nextBefore, stalled.NextSpawn);
            Assert.False(off.Running);
        }
        finally
        {
            Cleanup(full, armed, stalled, off);
            BotSessionManager.Enabled = sessions;
        }
    }

    [Fact]
    public void DueGatedDisabledAndGroupOwners_AreNotReportedAsMissingCallbacks()
    {
        var sessions = BotSessionManager.Enabled;
        var target = BotPopulation.TargetCount;
        ResetClock();
        BotSessionManager.Enabled = true;
        BotPopulation.TargetCount = 1;
        var occupant = new PlayerBot();
        occupant.MoveToWorld(BritainBank, Map.Felucca);
        var gated = CreateSpawner("Wander", 2, BritainBank);
        var due = CreateSpawner("PK", 2, BritainBank);
        var group = CreateSpawner("PK", 2, BritainBank);
        var off = CreateSpawner("PK", 2, BritainBank);
        try
        {
            var before = BotSpawnerStatsCommand.Compute();
            StopTimer(gated);
            due.DoTimer(TimeSpan.Zero); // active callback due now, NextSpawn == 0
            group.Spawn();
            group.Group = true;
            StopTimer(group);
            off.Running = false;
            var s = BotSpawnerStatsCommand.Compute();
            Assert.True(TimerArmed(due));
            Assert.True(due.NextSpawn <= TimeSpan.Zero);
            Assert.Equal(before.All.Due + 1, s.All.Due);
            Assert.Equal(before.All.GroupWaiting + 1, s.All.GroupWaiting);
            Assert.Equal(before.All.Off + 1, s.All.Off);
            Assert.Equal(0, s.All.Stalled);
            Assert.True(s.All.Gated >= 1);
            Assert.False(TimerArmed(gated)); // diagnostic must not repair it
            Assert.False(TimerArmed(group));
        }
        finally
        {
            Cleanup(gated, due, group, off, occupant);
            BotSessionManager.Enabled = sessions;
            BotPopulation.TargetCount = target;
        }
    }
}
