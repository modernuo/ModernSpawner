using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.ModernSpawner.Triggers;
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
            Entries = _spawnEntries ?? [],
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
            Notes = _notes,
            Triggers = ExportTriggerDefinitions(),
            CycleMode = _cycleMode,
            CurrentSubgroup = _currentSubgroup,
            SequentialResetTime = _sequentialResetTime,
            SequentialResetTo = _sequentialResetTo,
            HoldSequence = _holdSequence,
            MaxPendingCycles = _maxPendingCycles,
            RefractoryMin = _refractoryMin,
            RefractoryMax = _refractoryMax
        };
    }

    // Definitions export as { id, text }. Runtime state - queued cycles, cooldowns, kill counters,
    // per-entry deadlines and the refractory deadline - is world-save only and never exported.
    private List<TriggerDefinitionDto> ExportTriggerDefinitions()
    {
        var definitions = _triggerDefs;
        if (definitions == null || definitions.Count == 0)
        {
            return [];
        }

        var exported = new List<TriggerDefinitionDto>(definitions.Count);
        for (var i = 0; i < definitions.Count; i++)
        {
            exported.Add(new TriggerDefinitionDto { Id = definitions[i].Id, Text = definitions[i].Text });
        }

        return exported;
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

        // Ids come across so per-definition state written by a later save still lines up; a DTO
        // authored by hand may omit them, and TriggerDefinition mints one in that case.
        _triggerDefs = [];
        var triggers = dto.Triggers;
        if (triggers != null)
        {
            for (var i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                if (trigger != null)
                {
                    _triggerDefs.Add(new TriggerDefinition(this, UniqueDefinitionId(trigger.Id), trigger.Text));
                }
            }
        }

        _cycleMode = dto.CycleMode;
        _currentSubgroup = dto.CurrentSubgroup;
        _sequentialResetTime = dto.SequentialResetTime;
        _sequentialResetTo = dto.SequentialResetTo;
        _holdSequence = dto.HoldSequence;
        MaxPendingCycles = dto.MaxPendingCycles;
        _refractoryMin = dto.RefractoryMin;
        _refractoryMax = dto.RefractoryMax;
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

    [JsonPropertyName("entries")]
    [JsonPropertyOrder(10)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public List<ModernSpawnerEntry> Entries { get; init; }

    [JsonIgnore]
    public override IReadOnlyList<SpawnerEntry> EntryView => Entries;

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

    [JsonPropertyName("notes")]
    [JsonPropertyOrder(29)]
    public string Notes { get; init; }

    [JsonPropertyName("triggers")]
    [JsonPropertyOrder(30)]
    public List<TriggerDefinitionDto> Triggers { get; init; }

    [JsonPropertyName("cycleMode")]
    [JsonPropertyOrder(31)]
    public SpawnCycleMode CycleMode { get; init; }

    [JsonPropertyName("currentSubgroup")]
    [JsonPropertyOrder(32)]
    public int CurrentSubgroup { get; init; }

    [JsonPropertyName("sequentialResetTime")]
    [JsonPropertyOrder(33)]
    public TimeSpan SequentialResetTime { get; init; }

    [JsonPropertyName("sequentialResetTo")]
    [JsonPropertyOrder(34)]
    public int SequentialResetTo { get; init; }

    [JsonPropertyName("holdSequence")]
    [JsonPropertyOrder(35)]
    public bool HoldSequence { get; init; }

    /// <summary>Queue bound for trigger-bought cycles; <c>0</c> means run-now-or-drop.</summary>
    [JsonPropertyName("maxPendingCycles")]
    [JsonPropertyOrder(36)]
    public int MaxPendingCycles { get; init; } = 1;

    /// <summary>Low end of the spawner-wide lockout applied after an accepted event.</summary>
    [JsonPropertyName("refractoryMin")]
    [JsonPropertyOrder(37)]
    public TimeSpan RefractoryMin { get; init; }

    /// <summary>High end of the spawner-wide lockout applied after an accepted event.</summary>
    [JsonPropertyName("refractoryMax")]
    [JsonPropertyOrder(38)]
    public TimeSpan RefractoryMax { get; init; }

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

            // The spawner comes back from ApplyDto already running, so OnStarted never ran for the
            // definitions ApplyModernDto just set.
            spawner.EnsureTriggersActive();
            return spawner;
        }
        catch
        {
            spawner.Delete();
            throw;
        }
    }
}

/// <summary>
/// JSON carrier for one <see cref="TriggerDefinition"/>: its stable id and its parse text. Runtime
/// state that hangs off the id - cooldowns, kill counters, queued cycles - is world-save only.
/// Reads the pre-id shape (a bare definition string) as well as <c>{ id, text }</c>; see
/// <see cref="TriggerDefinitionDtoConverter"/>.
/// </summary>
[JsonConverter(typeof(TriggerDefinitionDtoConverter))]
public sealed record TriggerDefinitionDto
{
    /// <summary>Stable id of the definition. Omitted or empty asks the importer to mint one.</summary>
    [JsonPropertyName("id")]
    [JsonPropertyOrder(0)]
    public Guid Id { get; init; }

    /// <summary>The definition text the trigger system parses, e.g. <c>proximity:8:true</c>.</summary>
    [JsonPropertyName("text")]
    [JsonPropertyOrder(1)]
    public string Text { get; init; }
}

/// <summary>
/// Reads a trigger definition written either as <c>{ "id": …, "text": … }</c> or, for files exported
/// before definitions had ids, as a bare string. A string yields an empty id, which the import path
/// replaces with a freshly minted one. Always writes the object form.
/// </summary>
public sealed class TriggerDefinitionDtoConverter : JsonConverter<TriggerDefinitionDto>
{
    private const string IdPropertyName = "id";
    private const string TextPropertyName = "text";

    /// <inheritdoc />
    public override TriggerDefinitionDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        // Pre-id shape: "triggers": [ "proximity:8:true", ... ]
        if (reader.TokenType == JsonTokenType.String)
        {
            return new TriggerDefinitionDto { Text = reader.GetString() };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a trigger definition string or object, found {reader.TokenType}.");
        }

        var id = Guid.Empty;
        string text = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new TriggerDefinitionDto { Id = id, Text = text };
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected a trigger definition property name, found {reader.TokenType}.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (IdPropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                id = reader.TokenType == JsonTokenType.Null ? Guid.Empty : JsonSerializer.Deserialize<Guid>(ref reader, options);
            }
            else if (TextPropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                text = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Unterminated trigger definition object.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TriggerDefinitionDto value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(IdPropertyName);
        JsonSerializer.Serialize(writer, value.Id, options);
        writer.WriteString(TextPropertyName, value.Text);
        writer.WriteEndObject();
    }
}
