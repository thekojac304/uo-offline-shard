// =========================================================================
// PlayerBot.SpawnerRelease.cs — a deleted bot gives its spawner slot back.
//
// Mobile.OnDelete() is where ModernUO tells a spawner one of its mobiles is
// gone (Spawner.Remove), and that call is the only thing that restarts a
// spawner's refill timer once it has stopped at full. PlayerMobile.OnDelete()
// overrides it without calling base, so a deleted PlayerBot never reached
// its spawner. Every spawner that filled up once went quiet for good, and
// the world drained as sessions ended — until the next restart's respawn
// pass hid it again. BaseCreature notifies its spawner itself; this is the
// PlayerBot equivalent.
//
// Kept in its own partial file on purpose: if an update replaces
// PlayerBot.cs without the `partial` keyword, the build fails here instead
// of silently dropping the fix.
// =========================================================================

namespace Server.CustomBots
{
    public partial class PlayerBot
    {
        public override void OnDelete()
        {
            // Same order as Mobile.OnDelete. Remove() is a no-op for a bot the
            // spawner already let go of (RemoveSpawns, a respawn), so a
            // spawner-driven delete is never counted twice. A spawner that is
            // itself gone has no slot to give back.
            var spawner = Spawner;
            if (spawner is { Deleted: false })
            {
                spawner.Remove(this);
            }
            Spawner = null;

            base.OnDelete();
        }
    }
}
