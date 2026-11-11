using System;
using System.Collections.Generic;
using System.Reflection;
using Server.CustomBots;
using Server.Engines.Spawners;

namespace Server.Tests.CustomBots;

// Shared helpers for the PlayerBot regression tests. These run inside
// ModernUO's UOContent.Tests host (see playerbots/tests/README.md).
internal static class BotTestSupport
{
    public static readonly Point3D BritainBank = new(1433, 1689, 0);

    // Advance the shard clock the way the event loop does: one 8ms wheel turn
    // at a time, with Core.Now moving in step so spawner End stamps and
    // DelayCall deadlines line up with what production would see.
    public static void Advance(TimeSpan span)
    {
        var steps = (long)Math.Ceiling(span.TotalMilliseconds / Timer.TickRate);
        for (long i = 0; i < steps; i++)
        {
            Core._tickCount += Timer.TickRate;
            Core._now = Core._now.AddMilliseconds(Timer.TickRate);
            Timer.Slice(Core._tickCount);
        }
    }

    // Fresh, deterministic clock + empty timer wheel for one test.
    public static void ResetClock()
    {
        Core._now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        Timer.Init(Core._tickCount);
    }

    public static PlayerBotSpawner CreateSpawner(
        string behavior, int amount, Point3D at, bool fixedRole = false, int radius = 3
    )
    {
        PlayerBotSpawner sp = fixedRole
            ? new FixedRoleBotSpawner(behavior, amount, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5))
            : new PlayerBotSpawner(behavior, amount, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));

        sp.SpawnBounds = new Rectangle3D(
            new Point3D(at.X - radius, at.Y - radius, at.Z - 5),
            new Point3D(at.X + radius, at.Y + radius, at.Z + 20)
        );
        sp.MoveToWorld(at, Map.Felucca);
        return sp;
    }

    public static List<PlayerBot> BotsOf(BaseSpawner sp)
    {
        var list = new List<PlayerBot>();
        foreach (var e in sp.Spawned.Keys)
        {
            if (e is PlayerBot { Deleted: false } b)
            {
                list.Add(b);
            }
        }
        return list;
    }

    // Due timers can have NextSpawn <= 0; inspect the actual callback state.
    public static bool TimerArmed(BaseSpawner sp) => BotSpawnerStatsCommand.IsTimerArmed(sp) == true;

    public static object CallPrivateStatic(Type type, string method, params object[] args)
    {
        var mi = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
                 ?? throw new MissingMethodException(type.FullName, method);
        return mi.Invoke(null, args);
    }

    public static void Cleanup(params IEntity[] entities)
    {
        foreach (var e in entities)
        {
            if (e is { Deleted: false })
            {
                e.Delete();
            }
        }
    }
}
