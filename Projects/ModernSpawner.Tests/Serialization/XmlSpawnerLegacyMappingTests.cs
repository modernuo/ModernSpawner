using Server.Engines.ModernSpawner.Serialization;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Serialization;

public class XmlSpawnerLegacyMappingTests
{
    [Theory]
    [InlineData(false, -1, SpawnCycleMode.Random)]
    [InlineData(false, 0, SpawnCycleMode.Sequential)]
    [InlineData(false, 1, SpawnCycleMode.Sequential)]
    [InlineData(false, 5, SpawnCycleMode.Sequential)]
    [InlineData(true, -1, SpawnCycleMode.AllEntries)]
    public void MapLegacyCycleMode_ReturnsExpectedMode(bool isGroup, int sequentialSpawn, SpawnCycleMode expected)
    {
        Assert.Equal(expected, XmlSpawnerImporter.MapLegacyCycleMode(isGroup, sequentialSpawn));
    }

    [Fact]
    public void MapLegacyCycleMode_IsGroup_Wins_Over_SequentialSpawn()
    {
        // IsGroup takes precedence even if SequentialSpawn is also set.
        Assert.Equal(
            SpawnCycleMode.AllEntries,
            XmlSpawnerImporter.MapLegacyCycleMode(isGroup: true, sequentialSpawn: 3));
    }
}
