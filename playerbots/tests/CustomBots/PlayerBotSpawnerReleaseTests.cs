using System;
using Server.CustomBots;
using Xunit;
using static Server.Tests.CustomBots.BotTestSupport;

namespace Server.Tests.CustomBots;

// Regression tests for PlayerBot.SpawnerRelease.cs: a deleted PlayerBot must
// give its slot back to its spawner, and a spawner that stopped at full must
// start refilling again. Before the fix, PlayerMobile.OnDelete() swallowed the
// Mobile.OnDelete() spawner notification and these spawners stayed dead.
[Collection("Sequential UOContent Tests")]
public class PlayerBotSpawnerReleaseTests
{
    private sealed class GateScope : IDisposable
    {
        private readonly bool _sessions = BotSessionManager.Enabled;
        private readonly bool _regulars = BotRegulars.Enabled;
        private readonly int _target = BotPopulation.TargetCount;

        public GateScope(bool sessions, bool regulars = true)
        {
            BotSessionManager.Enabled = sessions;
            BotRegulars.Enabled = regulars;
            ResetClock();
        }

        public void Dispose()
        {
            BotSessionManager.Enabled = _sessions;
            BotRegulars.Enabled = _regulars;
            BotPopulation.TargetCount = _target;
        }
    }

    [Fact]
    public void DeletingSpawnedBot_FreesSlot_AndRearmsFullSpawner()
    {
        using var _ = new GateScope(sessions: false);
        var names = NamePool.InUseCount;
        var sp = CreateSpawner("Wander", 1, BritainBank);
        try
        {
            sp.Respawn();
            var bot = Assert.Single(BotsOf(sp));
            Assert.True(sp.IsFull);
            Assert.False(TimerArmed(sp)); // a full spawner parks its timer

            bot.Delete();

            Assert.Empty(sp.Spawned);
            Assert.False(sp.IsFull);
            Assert.True(TimerArmed(sp), "spawner must reschedule after its bot is deleted");
            Assert.Null(bot.Spawner);
        }
        finally
        {
            Cleanup(sp);
        }
        Assert.Equal(names, NamePool.InUseCount);
    }

    [Fact]
    public void RepeatedFillAndDrainCycles_KeepRefillingOnItsOwnTimer()
    {
        using var _ = new GateScope(sessions: false);
        var sp = CreateSpawner("Traveler", 3, BritainBank);
        try
        {
            sp.Respawn();
            for (var cycle = 0; cycle < 5; cycle++)
            {
                Assert.Equal(3, BotsOf(sp).Count);
                Assert.False(TimerArmed(sp));

                foreach (var b in BotsOf(sp))
                {
                    b.Delete();
                }
                Assert.Empty(sp.Spawned);
                Assert.True(TimerArmed(sp), $"cycle {cycle}: spawner did not rearm");

                // Let the real refill timer run: three spawns, each 5-15 min apart.
                Advance(TimeSpan.FromMinutes(46));
                Assert.True(sp.IsFull, $"cycle {cycle}: spawner did not refill");
            }
        }
        finally
        {
            Cleanup(sp);
        }
    }

    [Fact]
    public void SessionLogout_DeletesBot_AndSpawnerRefillsTheSlot()
    {
        // Regulars off: TryKeep returns false, so the logout ends in Delete(),
        // exactly as an ordinary bot's session end does.
        using var _ = new GateScope(sessions: false, regulars: false);
        var names = NamePool.InUseCount;
        var sp = CreateSpawner("Wander", 1, BritainBank);
        try
        {
            sp.Respawn();
            var bot = Assert.Single(BotsOf(sp));

            CallPrivateStatic(typeof(BotSessionManager), "BeginLogout", bot);
            Assert.True(bot.LoggingOut);
            Advance(TimeSpan.FromSeconds(7)); // "gtg" -> vanish after 3-6s

            Assert.True(bot.Deleted);
            Assert.True(TimerArmed(sp));
            Assert.Equal(names, NamePool.InUseCount);

            Advance(TimeSpan.FromMinutes(16));
            var replacement = Assert.Single(BotsOf(sp));
            Assert.NotSame(bot, replacement);
        }
        finally
        {
            Cleanup(sp);
        }
        Assert.Equal(names, NamePool.InUseCount);
    }

    [Fact]
    public void RegularPromotion_DetachesOnce_KeepsIdentity_AndDeletesCleanly()
    {
        using var _ = new GateScope(sessions: false);
        var offline = BotRegulars.OfflineCount;
        var total = BotRegulars.TotalCount;
        var sp = CreateSpawner("Wander", 1, BritainBank);
        try
        {
            sp.Respawn();
            var bot = Assert.Single(BotsOf(sp));
            var name = bot.Name;
            var serial = bot.Serial;

            CallPrivateStatic(typeof(BotRegulars), "MakeRegular", bot);
            CallPrivateStatic(typeof(BotRegulars), "GoOffline", bot);

            // Promotion already hands the slot back; the bot is its own person.
            Assert.Null(bot.Spawner);
            Assert.Empty(sp.Spawned);
            Assert.True(TimerArmed(sp));
            Assert.True(bot.Regular);
            Assert.Equal(name, bot.Name);
            Assert.Equal(serial, bot.Serial);
            Assert.Equal(offline + 1, BotRegulars.OfflineCount);

            // The spawner refills its slot with a stranger; the regular is untouched.
            sp.OnTick();
            var stranger = Assert.Single(BotsOf(sp));
            Assert.NotSame(bot, stranger);

            // Deleting the (spawner-less) regular must not touch the spawner.
            bot.Delete();
            Assert.Single(BotsOf(sp));
            Assert.Equal(offline, BotRegulars.OfflineCount);
            Assert.Equal(total, BotRegulars.TotalCount);
        }
        finally
        {
            Cleanup(sp);
        }
    }

    [Fact]
    public void FixtureDeletion_Refills_AndFixedRoleCountStaysExact()
    {
        using var _ = new GateScope(sessions: true);
        var fixedBefore = BotSessionManager.FixedRoleCount;
        var sp = CreateSpawner("BankSitter", 2, BritainBank, fixedRole: true);
        try
        {
            sp.Respawn();
            Assert.Equal(2, BotsOf(sp).Count);
            Assert.Equal(fixedBefore + 2, BotSessionManager.FixedRoleCount);
            Assert.All(BotsOf(sp), b => Assert.True(b.LifecycleExempt));

            BotsOf(sp)[0].Delete();
            Assert.Equal(fixedBefore + 1, BotSessionManager.FixedRoleCount);
            Assert.True(TimerArmed(sp));

            Advance(TimeSpan.FromMinutes(6)); // fixtures refill every 2-5 min
            Assert.Equal(2, BotsOf(sp).Count);
            Assert.Equal(fixedBefore + 2, BotSessionManager.FixedRoleCount);
        }
        finally
        {
            Cleanup(sp);
        }
        Assert.Equal(fixedBefore, BotSessionManager.FixedRoleCount);
    }

    [Fact]
    public void PkSpawner_RefillsWhileTheCurveGateIsClosed()
    {
        using var _ = new GateScope(sessions: true, regulars: false);
        var occupant = new PlayerBot(); // holds a name so the gate sits at/over target
        var sp = CreateSpawner("PK", 1, BritainBank);
        try
        {
            BotPopulation.TargetCount = 1;
            Assert.False(BotSessionManager.AllowSpawn());

            sp.Respawn();
            var pk = Assert.Single(BotsOf(sp));
            pk.Delete();
            Assert.True(TimerArmed(sp));

            Advance(TimeSpan.FromMinutes(16));
            Assert.Single(BotsOf(sp));
        }
        finally
        {
            Cleanup(sp, occupant);
        }
    }

    [Fact]
    public void BotWithoutSpawner_DeletesCleanly()
    {
        var names = NamePool.InUseCount;
        var bot = new PlayerBot();
        bot.MoveToWorld(BritainBank, Map.Felucca);
        Assert.Null(bot.Spawner);

        bot.Delete();

        Assert.True(bot.Deleted);
        Assert.Equal(names, NamePool.InUseCount);
    }

    [Fact]
    public void DeletingDeadBot_ReleasesTheStoppedFullOwner()
    {
        using var _ = new GateScope(sessions: false);
        var sp = CreateSpawner("Wander", 1, BritainBank);
        try
        {
            sp.Respawn();
            var bot = Assert.Single(BotsOf(sp));
            bot.Kill();
            Assert.False(bot.Alive);
            Assert.False(bot.Deleted);
            Assert.Contains(bot, sp.Spawned.Keys);
            Assert.False(TimerArmed(sp));
            bot.Delete();
            Assert.Empty(sp.Spawned);
            Assert.True(TimerArmed(sp));
            Assert.Null(bot.Spawner);
        }
        finally { Cleanup(sp); }
    }

    [Fact]
    public void ExplicitDetachedBot_DeleteDoesNotDisturbItsReplacement()
    {
        using var _ = new GateScope(sessions: false);
        var sp = CreateSpawner("Wander", 1, BritainBank);
        PlayerBot detached = null;
        try
        {
            sp.Respawn();
            detached = Assert.Single(BotsOf(sp));
            sp.Remove(detached);
            detached.Spawner = null;
            sp.Spawn();
            var replacement = Assert.Single(BotsOf(sp));
            var armed = TimerArmed(sp); // direct Spawn does not park an existing timer
            detached.Delete();
            Assert.Same(replacement, Assert.Single(BotsOf(sp)));
            Assert.Equal(armed, TimerArmed(sp));
        }
        finally { Cleanup(sp, detached); }
    }

    [Fact]
    public void BotWhoseSpawnerIsAlreadyDeleted_DeletesCleanly()
    {
        using var _ = new GateScope(sessions: false);
        var names = NamePool.InUseCount;
        var gone = CreateSpawner("Wander", 1, BritainBank);
        gone.Delete();

        var bot = new PlayerBot();
        bot.MoveToWorld(BritainBank, Map.Felucca);
        bot.Spawner = gone;

        bot.Delete();

        Assert.True(bot.Deleted);
        Assert.Null(bot.Spawner);
        Assert.Equal(names, NamePool.InUseCount);
    }

    [Fact]
    public void DeletingTheSpawner_DeletesItsBots_WithoutReentrancyErrors()
    {
        using var _ = new GateScope(sessions: false);
        var names = NamePool.InUseCount;
        var sp = CreateSpawner("Traveler", 4, BritainBank);
        sp.Respawn();
        var bots = BotsOf(sp);
        Assert.Equal(4, bots.Count);

        sp.Delete();

        Assert.All(bots, b => Assert.True(b.Deleted));
        Assert.Equal(names, NamePool.InUseCount);
    }

    [Fact]
    public void Respawn_ReplacesBots_WithoutDuplicating()
    {
        // BotStartupManager's boot pass calls Respawn() on every spawner; with
        // the delete path now notifying the spawner it must still end at
        // exactly Count bots, not Count plus the ones it just removed.
        using var _ = new GateScope(sessions: false);
        var names = NamePool.InUseCount;
        var sp = CreateSpawner("Traveler", 3, BritainBank);
        try
        {
            sp.Respawn();
            var first = BotsOf(sp);
            sp.Respawn();

            Assert.Equal(3, BotsOf(sp).Count);
            Assert.Equal(3, sp.Spawned.Count);
            Assert.All(first, b => Assert.True(b.Deleted));
            Assert.False(TimerArmed(sp));
        }
        finally
        {
            Cleanup(sp);
        }
        Assert.Equal(names, NamePool.InUseCount);
    }
}
