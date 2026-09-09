using System.Text.Json.Serialization;
using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.Spawners;
using Server.Json;

namespace Server.Engines.ModernSpawner;

public partial class ModernSpawner
{
    public override SpawnerDto ToDto()
    {
        var homeRange = DtoHomeRange;
        return new ModernSpawnerDto
        {
            Guid = Guid,
            Name = DtoName,
            Location = Location,
            Map = Map,
            Count = Count,
            MinDelay = MinDelay,
            MaxDelay = MaxDelay,
            Team = Team,
            WalkingRange = DtoWalkingRange,
            Entries = Entries,
            SpawnLocationIsHome = SpawnLocationIsHome,
            SpawnPositionMode = DtoSpawnPositionMode,
            MaxSpawnAttempts = DtoMaxSpawnAttempts,
            HomeRange = homeRange,
            SpawnBounds = homeRange >= 0 ? default : SpawnBounds,
            OnActivateScript = OnActivateScript?.Source,
            OnDeactivateScript = OnDeactivateScript?.Source,
            OnBeforeSpawnScript = OnBeforeSpawnScript?.Source,
            OnAfterSpawnScript = OnAfterSpawnScript?.Source,
            UseSmartPositioning = _useSmartPositioning,
            ReturnToSpawnOnIdle = _returnToSpawnOnIdle,
            MaxZDelta = _maxZDelta,
            TriggerActivated = _triggerActivated,
            SpawnArea = _spawnArea is { Width: > 0, Height: > 0 } ? _spawnArea : default,
            Notes = _notes
        };
    }

    /// <summary>Applies the ModernSpawner-specific DTO fields (import path).</summary>
    internal void ApplyModernDto(ModernSpawnerDto dto)
    {
        if (!string.IsNullOrEmpty(dto.OnActivateScript))
        {
            _onActivateScriptSerial = ScriptRegistry.GetOrRegister($"spawner_{Serial}_activate", dto.OnActivateScript);
        }

        if (!string.IsNullOrEmpty(dto.OnDeactivateScript))
        {
            _onDeactivateScriptSerial = ScriptRegistry.GetOrRegister($"spawner_{Serial}_deactivate", dto.OnDeactivateScript);
        }

        if (!string.IsNullOrEmpty(dto.OnBeforeSpawnScript))
        {
            _onBeforeSpawnScriptSerial = ScriptRegistry.GetOrRegister($"spawner_{Serial}_before", dto.OnBeforeSpawnScript);
        }

        if (!string.IsNullOrEmpty(dto.OnAfterSpawnScript))
        {
            _onAfterSpawnScriptSerial = ScriptRegistry.GetOrRegister($"spawner_{Serial}_after", dto.OnAfterSpawnScript);
        }

        _useSmartPositioning = dto.UseSmartPositioning;
        _returnToSpawnOnIdle = dto.ReturnToSpawnOnIdle;
        _maxZDelta = dto.MaxZDelta;
        _triggerActivated = dto.TriggerActivated;
        _notes = dto.Notes;

        if (dto.SpawnArea != default)
        {
            _spawnArea = dto.SpawnArea;
        }
    }
}

/// <summary>
/// JSON data carrier for <see cref="ModernSpawner"/> used by ModernUO's [ExportSpawners / [ImportSpawners
/// commands. Discovered via <see cref="JsonDiscoverableTypeAttribute"/> at Configure time.
/// </summary>
[JsonDiscoverableType("ModernSpawner")]
public sealed record ModernSpawnerDto : SpawnerDto
{
    [JsonPropertyName("spawnBounds")]
    [JsonPropertyOrder(8)]
    public Rectangle3D SpawnBounds { get; init; }

    [JsonPropertyName("onActivateScript")]
    [JsonPropertyOrder(20)]
    public string OnActivateScript { get; init; }

    [JsonPropertyName("onDeactivateScript")]
    [JsonPropertyOrder(21)]
    public string OnDeactivateScript { get; init; }

    [JsonPropertyName("onBeforeSpawnScript")]
    [JsonPropertyOrder(22)]
    public string OnBeforeSpawnScript { get; init; }

    [JsonPropertyName("onAfterSpawnScript")]
    [JsonPropertyOrder(23)]
    public string OnAfterSpawnScript { get; init; }

    [JsonPropertyName("useSmartPositioning")]
    [JsonPropertyOrder(24)]
    public bool UseSmartPositioning { get; init; } = true;

    [JsonPropertyName("returnToSpawnOnIdle")]
    [JsonPropertyOrder(25)]
    public bool ReturnToSpawnOnIdle { get; init; }

    [JsonPropertyName("maxZDelta")]
    [JsonPropertyOrder(26)]
    public int MaxZDelta { get; init; } = 20;

    [JsonPropertyName("triggerActivated")]
    [JsonPropertyOrder(27)]
    public bool TriggerActivated { get; init; }

    [JsonPropertyName("spawnArea")]
    [JsonPropertyOrder(28)]
    public Rectangle3D SpawnArea { get; init; }

    [JsonPropertyName("notes")]
    [JsonPropertyOrder(29)]
    public string Notes { get; init; }

    protected override BaseSpawner CreateEmpty() => new ModernSpawner();

    public override BaseSpawner ToSpawner()
    {
        var spawner = (ModernSpawner)base.ToSpawner();
        try
        {
            if (SpawnBounds != default)
            {
                spawner.SpawnBounds = SpawnBounds;
            }

            spawner.ApplyModernDto(this);
            return spawner;
        }
        catch
        {
            spawner.Delete();
            throw;
        }
    }
}
