using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Fixtures;

/// <summary>
/// Collection fixture for every test that needs a live ModernUO world. All process-global
/// initialization lives in <see cref="ModernSpawnerTestServer" /> and runs exactly once.
/// Tearing down global state is intentionally omitted: the world and the serialization
/// workers are initialized once and reused for the whole test host.
/// </summary>
[CollectionDefinition("Sequential ModernSpawner Tests", DisableParallelization = true)]
public class ModernSpawnerFixture : ICollectionFixture<ModernSpawnerFixture>
{
    public ModernSpawnerFixture() => ModernSpawnerTestServer.Initialize();
}
