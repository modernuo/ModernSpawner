using Server.Engines.ModernSpawner.Serialization;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Serialization;

public class XmlSpawnerLegacyMappingTests
{
    // IsGroup maps to base Group only, never to the AllEntries cycle mode (design §7): the two flags used
    // to be conflated, which made an imported group spawner run one attempt per entry per cycle on top of
    // its bulk respawn instead of the plain random/sequential draw XmlSpawner's own "group" spawner used.
    // SequentialSpawn is the only flag that still drives the cycle mode.
    [Theory]
    [InlineData(-1, SpawnCycleMode.Random)]
    [InlineData(0, SpawnCycleMode.Sequential)]
    [InlineData(1, SpawnCycleMode.Sequential)]
    [InlineData(5, SpawnCycleMode.Sequential)]
    public void MapLegacyCycleMode_ReturnsExpectedMode(int sequentialSpawn, SpawnCycleMode expected)
    {
        Assert.Equal(expected, XmlSpawnerImporter.MapLegacyCycleMode(sequentialSpawn));
    }
}
