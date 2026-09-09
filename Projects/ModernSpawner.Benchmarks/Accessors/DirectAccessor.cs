namespace ModernSpawner.Benchmarks.Accessors;

/// <summary>
/// Baseline comparison: Direct property access in C#.
/// This represents the theoretical maximum performance.
/// </summary>
public static class DirectAccessor
{
    public static int GetKarma(MockMobile mobile) => mobile.Karma;
    public static int GetFame(MockMobile mobile) => mobile.Fame;
    public static int GetHits(MockMobile mobile) => mobile.Hits;
    public static int GetLocationX(MockMobile mobile) => mobile.Location.X;
    public static int GetLocationY(MockMobile mobile) => mobile.Location.Y;

    public static void SetKarma(MockMobile mobile, int value) => mobile.Karma = value;
    public static void SetHits(MockMobile mobile, int value) => mobile.Hits = value;
}
