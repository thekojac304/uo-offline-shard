using System;
using System.Collections.Generic;
using System.Reflection;
using Server.Commands;
using Server.Engines.Spawners;

namespace Server.CustomBots
{
    // Observational only: never calls Defrag, CountSpawns, Respawn, Start or Remove.
    // Flags overlap: a gated owner can also be armed/due, and an off owner unarmed.
    public static class BotSpawnerStatsCommand
    {
        // The pinned engine exposes NextSpawn, but zero/negative is ambiguous for a
        // due timer. Read the actual timer without adding a new engine patch/API.
        // If the engine field changes, report Unknown rather than inventing stalls.
        private static readonly FieldInfo TimerField = typeof(BaseSpawner).GetField(
            "_timer", BindingFlags.Instance | BindingFlags.NonPublic);

        public sealed class SpawnerGroup
        {
            public int Spawners, Capacity, Filled, StaleRefs;
            public int Armed, Due, Unarmed, TimerUnknown, Stalled, Full, Off;
            public int Gated, GroupWaiting, Abandoned;
        }

        public sealed class Snapshot
        {
            public int Live, Target, Cap;
            public double Curve;
            public int Ordinary, PK, Fixed, RegularsOnline, RegularsOffline, Permanent, Internal;
            public readonly SpawnerGroup All = new();
            public readonly SortedDictionary<string, SpawnerGroup> ByBehavior = new(StringComparer.OrdinalIgnoreCase);
            public bool SessionsOn, AllowSpawn;
            public int NamesInUse, FixedRoleCount, OfflineRegulars, GateCount, HeldRegularSeats;
            public int EffectiveGateCount => GateCount + HeldRegularSeats;
            public int Empty => All.Capacity - All.Filled;
        }

        public static void Configure() =>
            CommandSystem.Register("BotSpawnerStats", AccessLevel.GameMaster, OnCommand);

        public static bool? IsTimerArmed(BaseSpawner spawner) =>
            TimerField == null ? null : (TimerField.GetValue(spawner) as Timer)?.Running == true;

        public static Snapshot Compute()
        {
            var s = new Snapshot
            {
                Target = BotSessionManager.TargetNow,
                Curve = BotSessionManager.CurveNow,
                Cap = BotPopulation.TargetCount,
                SessionsOn = BotSessionManager.Enabled,
                AllowSpawn = BotSessionManager.AllowSpawn(),
                HeldRegularSeats = BotRegulars.ReadyToHold(),
                NamesInUse = NamePool.InUseCount,
                FixedRoleCount = BotSessionManager.FixedRoleCount,
                OfflineRegulars = BotRegulars.OfflineCount
            };
            s.GateCount = s.NamesInUse - s.FixedRoleCount - s.OfflineRegulars;

            foreach (var m in World.Mobiles.Values)
            {
                if (m is not PlayerBot { Deleted: false } bot) continue;
                if (bot.Map == null || bot.Map == Map.Internal) { s.Internal++; continue; }
                s.Live++;
                if (bot.LifecycleExempt) s.Fixed++;
                else if (bot.IsPermanent) s.Permanent++;
                else if (bot.Regular) s.RegularsOnline++;
                else if (bot.Behavior is PKBehavior) s.PK++;
                else s.Ordinary++;
            }
            s.RegularsOffline = BotRegulars.OfflineCount;

            foreach (var item in World.Items.Values)
            {
                if (item is not PlayerBotSpawner { Deleted: false } sp) continue;
                var key = (sp is FixedRoleBotSpawner ? "Fixed:" : "") + (sp.BehaviorName ?? "?").Split(':')[0];
                if (!s.ByBehavior.TryGetValue(key, out var group)) s.ByBehavior[key] = group = new SpawnerGroup();
                Tally(s.All, sp, s.AllowSpawn);
                Tally(group, sp, s.AllowSpawn);
            }
            return s;
        }

        private static void Tally(SpawnerGroup g, PlayerBotSpawner sp, bool allowSpawn)
        {
            int filled = 0, stale = 0;
            if (sp.Spawned != null)
            {
                foreach (var entity in sp.Spawned.Keys)
                {
                    if (entity == null || entity.Deleted || !ReferenceEquals(entity.Spawner, sp)) stale++;
                    else filled++;
                }
            }
            g.Spawners++;
            g.Capacity += sp.Count;
            g.Filled += Math.Min(filled, sp.Count);
            g.StaleRefs += stale;

            var armed = IsTimerArmed(sp);
            bool full = filled >= sp.Count;
            bool gated = !full && sp is not FixedRoleBotSpawner && sp.BehaviorName != "PK" && !allowSpawn;
            bool groupWaiting = !full && sp.Group && filled > 0;
            bool abandoned = sp.SpawnPositionMode == SpawnPositionMode.Abandoned;
            if (!sp.Running) g.Off++;
            if (full) g.Full++;
            if (gated) g.Gated++;
            if (groupWaiting) g.GroupWaiting++;
            if (abandoned) g.Abandoned++;
            if (armed == true)
            {
                g.Armed++;
                if (sp.NextSpawn <= TimeSpan.Zero) g.Due++;
            }
            else if (armed == false) g.Unarmed++;
            else g.TimerUnknown++;

            // A potential missing refill callback, not proof of a permanent stall.
            // Placement failures can still prevent progress with a healthy timer.
            if (sp.Running && !full && armed == false && !gated && !groupWaiting && !abandoned) g.Stalled++;
        }

        private static string Health(SpawnerGroup g) =>
            $"{g.Armed} armed ({g.Due} due), {g.Unarmed} unarmed, {g.Stalled} potentially stalled, " +
            $"{g.Gated} gated, {g.GroupWaiting} group-waiting, {g.Abandoned} abandoned, " +
            $"{g.Full} full, {g.Off} off, {g.TimerUnknown} timer-unknown (flags overlap)";

        [Usage("BotSpawnerStats [detail]")]
        [Description("Read-only population, held seats, ownership and refill callback status.")]
        private static void OnCommand(CommandEventArgs e)
        {
            var s = Compute();
            e.Mobile.SendMessage($"Population: {s.Live} live, target {s.Target} (curve {s.Curve:P0}, configured {s.Cap}). " +
                $"Ordinary {s.Ordinary}, PK {s.PK}, fixed {s.Fixed}, regulars {s.RegularsOnline} on / " +
                $"{s.RegularsOffline} off, guild-bound {s.Permanent}, internal {s.Internal}.");
            e.Mobile.SendMessage($"Spawners: {s.All.Spawners}, slots {s.All.Capacity}, filled {s.All.Filled}, " +
                $"available {s.Empty}, stale refs {s.All.StaleRefs}. {Health(s.All)}.");
            e.Mobile.SendMessage($"Gate: sessions {(s.SessionsOn ? "on" : "off")}, AllowSpawn {s.AllowSpawn}; " +
                $"names {s.NamesInUse} - fixtures {s.FixedRoleCount} - offline {s.OfflineRegulars} = {s.GateCount}, " +
                $"+ held seats {s.HeldRegularSeats} = {s.EffectiveGateCount} vs target {s.Target}. " +
                "Fixed/PK spawners bypass admission; a falling target can temporarily be below live population.");
            if (e.Arguments.Length == 0 || !string.Equals(e.Arguments[0], "detail", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var (name, g) in s.ByBehavior)
                e.Mobile.SendMessage($"  {name}: {g.Spawners} owners, {g.Filled}/{g.Capacity} filled, " +
                    $"{g.StaleRefs} stale. {Health(g)}");
        }
    }
}
