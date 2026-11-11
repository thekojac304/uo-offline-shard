using System;
using Server.CustomBots;
using Xunit;
using static Server.Tests.CustomBots.BotTestSupport;

namespace Server.Tests.CustomBots;

// Roof placement is accepted policy. Test ownership/refill, not a cosmetic Z.
[Collection("Sequential UOContent Tests")]
public class OfficialBankSpawnTests
{
    [Theory]
    [InlineData(1439, 1681, 8, false)]
    [InlineData(1433, 1689, 0, true)]
    [InlineData(1816, 2828, 0, true)]
    [InlineData(660, 820, 0, true)]
    [InlineData(4471, 1157, 0, true)]
    [InlineData(1651, 1608, 20, true)]
    public void OfficialBankBounds_MayIncludeRoofs_ButDoNotLoseCapacity(int x, int y, int z, bool fixture)
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        var enabled = BotSessionManager.Enabled;
        BotSessionManager.Enabled = false;
        ResetClock();
        var sp = CreateSpawner("BankSitter", 2, new Point3D(x, y, z), fixture);
        try
        {
            var bounds = sp.SpawnBounds;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                sp.Respawn();
                Assert.Equal(2, BotsOf(sp).Count);
                foreach (var bot in BotsOf(sp)) bot.Delete();
                Assert.Empty(sp.Spawned);
                Assert.True(TimerArmed(sp));
                Advance(TimeSpan.FromMinutes(31));
                Assert.Equal(2, BotsOf(sp).Count);
                Assert.Equal(0, BotSpawnerStatsCommand.Compute().All.StaleRefs);
                Assert.Equal(bounds, sp.SpawnBounds); // no hidden floor repair
            }
        }
        finally { Cleanup(sp); BotSessionManager.Enabled = enabled; }
    }
}
