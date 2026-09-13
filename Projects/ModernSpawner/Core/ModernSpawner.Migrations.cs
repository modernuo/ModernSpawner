using System;
using System.Collections.Generic;
using Server.Engines.ModernSpawner.Triggers;

namespace Server.Engines.ModernSpawner;

public partial class ModernSpawner
{
    /// <summary>
    /// v0 -> v1. The trigger definition list gains identity: each stored string becomes a
    /// <see cref="TriggerDefinition"/> with a freshly minted id, in the order it was saved. The old
    /// <c>_triggered</c> flag is dropped - a spawner that was mid-trigger at save time comes back with
    /// an empty queue, which is what a restart means for a one-shot flag. Everything the queue, the
    /// refractory and the per-definition state need starts at its default: no pending cycles, a queue
    /// bound of one, no lockout, and one empty state per definition (bound by
    /// <see cref="EnsureTriggersActive"/> on load).
    /// </summary>
    /// <param name="content">The v0 payload.</param>
    private void MigrateFrom(V0Content content)
    {
        _spawnEntries = content.SpawnEntries;
        _onActivateScriptSerial = content.OnActivateScriptSerial;
        _onDeactivateScriptSerial = content.OnDeactivateScriptSerial;
        _onBeforeSpawnScriptSerial = content.OnBeforeSpawnScriptSerial;
        _onAfterSpawnScriptSerial = content.OnAfterSpawnScriptSerial;
        _useSmartPositioning = content.UseSmartPositioning;
        _returnToSpawnOnIdle = content.ReturnToSpawnOnIdle;
        _maxZDelta = content.MaxZDelta;

        var definitions = content.TriggerDefinitions;
        _triggerDefs = new List<TriggerDefinition>(definitions?.Count ?? 0);
        if (definitions != null)
        {
            for (var i = 0; i < definitions.Count; i++)
            {
                _triggerDefs.Add(new TriggerDefinition(this, definitions[i]));
            }
        }

        _triggerActivated = content.TriggerActivated;
        // content.Triggered is deliberately dropped: the queue replaces it.
        _notes = content.Notes;
        _cycleMode = content.CycleMode;
        _currentSubgroup = content.CurrentSubgroup;
        _sequentialResetTime = content.SequentialResetTime;
        _sequentialResetTo = content.SequentialResetTo;
        _holdSequence = content.HoldSequence;

        _pendingSlots = [];
        _maxPendingCycles = 1;
        _refractoryMin = TimeSpan.Zero;
        _refractoryMax = TimeSpan.Zero;
        _refractoryUntil = default;
        _triggerStateList = [];
    }
}
