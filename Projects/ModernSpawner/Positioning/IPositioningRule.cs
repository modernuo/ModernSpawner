namespace Server.Engines.ModernSpawner.Positioning;

/// <summary>
/// Interface for named positioning rules that can be assigned to spawner entries.
/// </summary>
public interface IPositioningRule
{
    /// <summary>
    /// The unique name of this positioning rule (e.g., "near_water", "on_road", "indoor").
    /// </summary>
    string RuleName { get; }

    /// <summary>
    /// Human-readable description of this rule.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets a spawn position according to this rule.
    /// </summary>
    Point3D GetPosition(PositioningContext context);
}
