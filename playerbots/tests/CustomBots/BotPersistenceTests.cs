using System;
using Server.CustomBots;
using Xunit;
using static Server.Tests.CustomBots.BotTestSupport;

namespace Server.Tests.CustomBots;

[Collection("Sequential UOContent Tests")]
public class BotPersistenceTests
{
    [Fact]
    public void PlayerBot_CustomIdentityAndRegularGuildFields_RoundTrip()
    {
        var source = new PlayerBot();
        PlayerBot copy = null;
        try
        {
            source.Regular = true;
            source.GuildBound = true;
            source.BotGuildIndex = 2;
            source.HomeCity = "Britain";
            source.OfflineSince = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            var writer = new BufferWriter(true);
            source.Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            copy = new PlayerBot(World.NewMobile);
            var reader = new BufferReader(bytes);
            copy.Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
            Assert.Equal(source.Name, copy.Name);
            Assert.True(copy.Regular);
            Assert.True(copy.GuildBound);
            Assert.Equal(source.BotGuildIndex, copy.BotGuildIndex);
            Assert.Equal(source.HomeCity, copy.HomeCity);
            Assert.Equal(source.OfflineSince, copy.OfflineSince);
            Assert.Equal(source.Class, copy.Class);
            Assert.Equal(source.SkillTier, copy.SkillTier);
        }
        finally { Cleanup(copy, source); }
    }

    [Fact]
    public void Spawner_BehaviorBoundsAndEntries_RoundTripWithoutFloorRepair()
    {
        var source = CreateSpawner("BankSitter", 2, new Point3D(1439, 1681, 8));
        PlayerBotSpawner copy = null;
        try
        {
            var bounds = source.SpawnBounds;
            var writer = new BufferWriter(true);
            source.Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            copy = new PlayerBotSpawner(World.NewItem);
            var reader = new BufferReader(bytes);
            copy.Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
            Assert.Equal("BankSitter", copy.BehaviorName);
            Assert.Equal(2, copy.Count);
            Assert.Equal(bounds, copy.SpawnBounds);
            Assert.Equal(source.Entries.Count, copy.Entries.Count);
            Assert.Equal(27, copy.SpawnBounds.End.Z - 1); // roof may be admitted intentionally
        }
        finally { Cleanup(copy, source); }
    }
}
