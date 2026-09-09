using Server.Engines.Spawners;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Scripting;

public class PropertyAccessorCacheTests
{
    /// <summary>
    /// Regression test: <c>Server.Skills</c> has multiple indexer overloads that all
    /// share the reflection name <c>"Item"</c>. A naive
    /// <c>type.GetProperty("Item", flags)</c> throws <see cref="System.Reflection.AmbiguousMatchException"/>.
    /// <c>PrewarmCache</c> must skip indexers and the internal lookup must filter
    /// to non-indexed properties. See <c>Docs/QA-Test-Plan.md</c> guidance on reflection.
    /// </summary>
    [Fact]
    public void PrewarmCache_SkillsType_DoesNotThrowOnIndexerOverloads()
    {
        var ex = Record.Exception(() => PropertyAccessorCache.PrewarmCache(typeof(Skills)));

        Assert.Null(ex);
    }

    [Fact]
    public void PrewarmCache_MultipleTypes_DoesNotThrow()
    {
        // Cover a representative mix: a class with indexers (Skills), plain structs
        // (Point3D), and a shallow class (Skill).
        var ex = Record.Exception(() =>
            PropertyAccessorCache.PrewarmCache(typeof(Skills), typeof(Skill), typeof(Point3D)));

        Assert.Null(ex);
    }

    [Fact]
    public void GetOrCreateAccessor_IndexerName_ReturnsNull_NotAmbiguity()
    {
        // "Item" is the reflection name for indexers. Asking for it by name on a
        // type with multiple indexer overloads must not throw — it should return
        // null because the accessor cache doesn't model indexed access.
        var accessor = PropertyAccessorCache.GetOrCreateAccessor(typeof(Skills), "Item");

        Assert.Null(accessor);
    }

    [Fact]
    public void GetOrCreateAccessor_KnownProperty_ReturnsAccessor()
    {
        // Skills.Length is a plain non-indexed property — should resolve cleanly.
        var accessor = PropertyAccessorCache.GetOrCreateAccessor(typeof(Skills), "Length");

        Assert.NotNull(accessor);
    }
}
