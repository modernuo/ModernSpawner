namespace ModernSpawner.Benchmarks;

/// <summary>
/// Mock types that simulate the structure of actual game objects.
/// These have nested properties to test property chain access patterns.
/// </summary>
public class MockMobile
{
    public int Karma { get; set; } = 1000;
    public int Fame { get; set; } = 500;
    public int Hits { get; set; } = 100;
    public int HitsMax { get; set; } = 100;
    public int Str { get; set; } = 50;
    public int Dex { get; set; } = 50;
    public int Int { get; set; } = 50;
    public string Name { get; set; } = "TestMobile";
    public bool Alive { get; set; } = true;
    public MockLocation Location { get; set; } = new();
    public MockBackpack Backpack { get; set; } = new();
}

public class MockLocation
{
    public int X { get; set; } = 1000;
    public int Y { get; set; } = 2000;
    public int Z { get; set; } = 0;
}

public class MockBackpack
{
    public int TotalItems { get; set; } = 10;
    public int TotalWeight { get; set; } = 50;
    public int MaxItems { get; set; } = 125;
}

public class MockSpawner
{
    public int Count { get; set; } = 10;
    public int SpawnedCount { get; set; } = 5;
    public int HomeRange { get; set; } = 10;
    public bool Running { get; set; } = true;
    public MockLocation Location { get; set; } = new();
}

public class MockItem
{
    public int Hue { get; set; } = 0;
    public int Amount { get; set; } = 1;
    public double Weight { get; set; } = 1.0;
    public string Name { get; set; } = "TestItem";
    public MockLocation Location { get; set; } = new();
}

/// <summary>
/// Simulates the ScriptContext that holds all objects during script execution.
/// </summary>
public class MockScriptContext
{
    public MockMobile? TriggeringMobile { get; set; }
    public MockSpawner? Spawner { get; set; }
    public object? Target { get; set; }
    public Dictionary<string, object> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
}
