using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Server.CustomBots;
using Xunit;
using Xunit.Abstractions;

namespace Server.Tests.CustomBots;

[Collection("Sequential UOContent Tests")]
public class NavigationDataTests
{
    private readonly ITestOutputHelper _out;
    public NavigationDataTests(ITestOutputHelper output) => _out = output;
    private static JsonDocument Read(string sub, string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", sub, file)));

    [Theory]
    // Counts of the official data at upstream f90e419 (Nov 8 2026).
    [InlineData("Destinations", "destinations.json", "Destinations", 496)]
    [InlineData("Waypoints", "waypoints.json", "Waypoints", 4014)]
    [InlineData("Zones", "zones.json", "Zones", 136)]
    public void OfficialDatasets_ParseWithUniqueNamesAndExpectedRecordCounts(string sub, string file, string key, int count)
    {
        using var doc = Read(sub, file);
        var entries = doc.RootElement.GetProperty(key).EnumerateArray().ToArray();
        Assert.Equal(count, entries.Length);
        Assert.Equal(count, entries.Select(e => e.GetProperty("Name").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OfficialHouseSpots_LoadThroughRuntimeParser()
    {
        using var doc = Read("CustomSpawns", "house_spots.json");
        var raw = doc.RootElement.GetProperty("Spots").EnumerateArray().ToArray();
        var spots = BotVendorHouses.LoadSpots();
        Assert.Equal(7, raw.Length);
        Assert.Equal(raw.Length, spots.Count); // none dropped as malformed
        Assert.Equal(spots.Count, spots.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(spots, s =>
        {
            Assert.True(s.Poly.Length >= 3);
            Assert.True(s.Plots > 0, s.Name);
            Assert.True(s.X0 <= s.X1 && s.Y0 <= s.Y1);
        });
        _out.WriteLine(string.Join("; ", spots.Select(s => $"{s.Name}: {s.Plots} plots")));
    }

    [Theory]
    [InlineData("Deceit lvl2 Room 2", 5220, 755, -20)]
    [InlineData("Deceit lvl2 Room 3", 5221, 673, -20)]
    [InlineData("Deceit lvl2 Room 5", 5218, 716, -20)]
    [InlineData("Despise lvl2 Room 2", 5384, 614, 53)]
    [InlineData("Despise lvl2 Room 21", 5450, 520, 63)]
    [InlineData("Despise lvl2 Room 3", 5398, 520, 65)]
    [InlineData("Despise lvl2 Room 5", 5442, 576, 60)]
    [InlineData("Despise lvl2b Room", 5499, 565, 60)]
    [InlineData("Despise lvl2b Room 21", 5504, 520, 60)]
    [InlineData("Despise lvl2b Room B", 5485, 631, 25)]
    [InlineData("Shame lvl3 Room 1", 5514, 142, 20)]
    [InlineData("Wrong lvl2 Room 1 C", 5723, 562, 20)]
    public void ChangedDungeonRoom_UsesCorrectElevationAndHasNearbyStandableGround(string name, int x, int y, int z)
    {
        Assert.True(TestServerInitializer.TileDataLoaded, "Map data is mandatory for candidate validation.");
        using var doc = Read("Destinations", "destinations.json");
        var entry = Assert.Single(doc.RootElement.GetProperty("Destinations").EnumerateArray(), e => e.GetProperty("Name").GetString() == name);
        Assert.Equal(x, entry.GetProperty("X").GetInt32());
        Assert.Equal(y, entry.GetProperty("Y").GetInt32());
        Assert.Equal(z, entry.GetProperty("Z").GetInt32());
        // Destination Z anchors a floor window, not necessarily a perfectly
        // standable center tile. Check the same +/-10 tolerance used by OnFloor.
        var levels = new System.Collections.Generic.SortedSet<int>();
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
                for (int dz = -PaintedZone.FloorWindow; dz <= PaintedZone.FloorWindow; dz++)
                    if (Map.Felucca.CanFit(x + dx, y + dy, z + dz, 16, false, false)) levels.Add(z + dz);
        _out.WriteLine($"{name}: reference={z}, standable-nearby={string.Join(',', levels)}");
        Assert.NotEmpty(levels);
    }

    [Fact]
    public void ZoneMesh_BuildsFloorAwareLinks_FromCompleteOfficialData()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        ZoneRegistry.Reload(); // Configure only registers commands; Reload loads data.
        Assert.True(ZoneRegistry.MeshBuilt);
        Assert.True(ZoneRegistry.FloorLock);
        Assert.True(ZoneRegistry.StrictLinks);
        Assert.NotEmpty(ZoneRegistry.Links);
        Assert.Contains(ZoneRegistry.All, z => z.IsDungeon && z.FloorKnown);
        Assert.All(ZoneRegistry.Links, link =>
        {
            Assert.Contains(link.A, ZoneRegistry.All);
            Assert.Contains(link.B, ZoneRegistry.All);
        });
        _out.WriteLine($"zones={ZoneRegistry.All.Count}, links={ZoneRegistry.Links.Count}, " +
            $"floorFallbacks={ZoneRegistry.All.Count(z => z.FloorIgnored)}, isolated={ZoneRegistry.All.Count(z => z.IsMesh && z.Links.Count == 0)}");
        // Disconnected zones are reported for route trials, not silently deleted
        // or assumed to be regressions without an equivalent baseline/map probe.
    }
}
