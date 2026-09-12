using System;

namespace Server.Engines.ModernSpawner.Scripting.Ast;

/// <summary>
/// AST node that triggers another spawner to spawn.
/// Usage: SPAWN/spawnerName or SPAWN/spawnerSerial
/// </summary>
public class SpawnCommandNode : ScriptNode
{
    /// <summary>
    /// The spawner identifier (name or serial).
    /// </summary>
    public string SpawnerIdentifier { get; }

    /// <summary>
    /// Optional entry index to spawn from (-1 for any entry).
    /// </summary>
    public int EntryIndex { get; }

    public SpawnCommandNode(string spawnerIdentifier, int entryIndex = -1)
    {
        SpawnerIdentifier = spawnerIdentifier;
        EntryIndex = entryIndex;
    }

    public override void Execute(ScriptContext context)
    {
        var targetSpawner = FindSpawner(context);
        if (targetSpawner == null)
        {
            return;
        }

        if (EntryIndex >= 0 && EntryIndex < targetSpawner.Entries.Count)
        {
            // Spawn from specific entry
            var entry = targetSpawner.Entries[EntryIndex];
            targetSpawner.Spawn(entry, out _);
        }
        else
        {
            // Spawn any entry
            targetSpawner.Spawn();
        }
    }

    private ModernSpawner FindSpawner(ScriptContext context)
    {
        if (string.IsNullOrEmpty(SpawnerIdentifier))
        {
            return null;
        }

        // Try to find by serial
        if (Serial.TryParse(SpawnerIdentifier, null, out var serial))
        {
            var item = World.FindItem(serial);
            if (item is ModernSpawner spawner)
            {
                return spawner;
            }
        }

        // Try to find by name in the area
        return FindSpawnerByName(context.Spawner, SpawnerIdentifier);
    }

    private static ModernSpawner FindSpawnerByName(ModernSpawner sourceSpawner, string name)
    {
        if (sourceSpawner?.Map == null || sourceSpawner.Map == Map.Internal)
        {
            return null;
        }

        // Search in a reasonable range (256 tiles)
        foreach (var item in sourceSpawner.Map.GetItemsInRange(sourceSpawner.Location, 256))
        {
            if (item is ModernSpawner spawner &&
                spawner.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
            {
                return spawner;
            }
        }

        return null;
    }
}

/// <summary>
/// AST node that removes spawns from another spawner.
/// Usage: DESPAWN/spawnerName or DESPAWN/spawnerName/entryIndex
/// </summary>
public class DespawnCommandNode : ScriptNode
{
    /// <summary>
    /// The spawner identifier (name or serial).
    /// </summary>
    public string SpawnerIdentifier { get; }

    /// <summary>
    /// Optional entry index to despawn from (-1 for all entries).
    /// </summary>
    public int EntryIndex { get; }

    /// <summary>
    /// Number of spawns to remove (-1 for all).
    /// </summary>
    public int Count { get; }

    public DespawnCommandNode(string spawnerIdentifier, int entryIndex = -1, int count = -1)
    {
        SpawnerIdentifier = spawnerIdentifier;
        EntryIndex = entryIndex;
        Count = count;
    }

    public override void Execute(ScriptContext context)
    {
        var targetSpawner = FindSpawner(context);
        if (targetSpawner == null)
        {
            return;
        }

        if (EntryIndex >= 0 && EntryIndex < targetSpawner.ModernEntries.Count)
        {
            // Despawn from specific entry
            var entry = targetSpawner.ModernEntries[EntryIndex];
            DespawnFromEntry(targetSpawner, entry, Count);
        }
        else
        {
            // Despawn from all entries
            foreach (var entry in targetSpawner.ModernEntries)
            {
                DespawnFromEntry(targetSpawner, entry, Count);
            }
        }
    }

    private static void DespawnFromEntry(ModernSpawner spawner, ModernSpawnerEntry entry, int count)
    {
        var toRemove = count < 0 ? entry.Spawned.Count : Math.Min(count, entry.Spawned.Count);

        for (var i = 0; i < toRemove; i++)
        {
            if (entry.Spawned.Count == 0)
            {
                break;
            }

            var spawned = entry.Spawned[^1];
            if (spawned == null)
            {
                entry.RemoveFromSpawned(spawned);
            }
            else
            {
                // Deleting routes through BaseSpawner.Remove, the single registry path.
                spawned.Delete();
            }
        }
    }

    private ModernSpawner FindSpawner(ScriptContext context)
    {
        if (string.IsNullOrEmpty(SpawnerIdentifier))
        {
            return null;
        }

        if (Serial.TryParse(SpawnerIdentifier, null, out var serial))
        {
            var item = World.FindItem(serial);
            if (item is ModernSpawner spawner)
            {
                return spawner;
            }
        }

        return FindSpawnerByName(context.Spawner, SpawnerIdentifier);
    }

    private static ModernSpawner FindSpawnerByName(ModernSpawner sourceSpawner, string name)
    {
        if (sourceSpawner?.Map == null || sourceSpawner.Map == Map.Internal)
        {
            return null;
        }

        foreach (var item in sourceSpawner.Map.GetItemsInRange(sourceSpawner.Location, 256))
        {
            if (item is ModernSpawner spawner &&
                spawner.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
            {
                return spawner;
            }
        }

        return null;
    }
}

/// <summary>
/// AST node that teleports the target to a location.
/// Usage: GOTO/x,y,z or GOTO/spawnerName or GOTO/x,y,z,mapName
/// </summary>
public class GotoCommandNode : ScriptNode
{
    /// <summary>
    /// Target location string (coordinates or spawner name).
    /// </summary>
    public string TargetLocation { get; }

    public GotoCommandNode(string targetLocation)
    {
        TargetLocation = targetLocation;
    }

    public override void Execute(ScriptContext context)
    {
        IEntity target = context.Target as Mobile;
        target ??= context.Target as Item;

        if (target == null)
        {
            return;
        }

        var (location, map) = ParseLocation(context);
        if (location == Point3D.Zero)
        {
            return;
        }

        if (target is Mobile mobile)
        {
            mobile.MoveToWorld(location, map ?? mobile.Map);
        }
        else if (target is Item item)
        {
            item.MoveToWorld(location, map ?? item.Map);
        }
    }

    private (Point3D location, Map map) ParseLocation(ScriptContext context)
    {
        if (string.IsNullOrEmpty(TargetLocation))
        {
            return (Point3D.Zero, null);
        }

        // Try to parse as coordinates: "x,y,z" or "x,y,z,mapName" or "x,y"
        var parts = TargetLocation.Split(',');
        if (parts.Length >= 2)
        {
            if (int.TryParse(parts[0].Trim(), out var x) && int.TryParse(parts[1].Trim(), out var y))
            {
                var z = 0;
                Map map = null;

                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2].Trim(), out z);
                }

                if (parts.Length >= 4)
                {
                    map = Map.Parse(parts[3].Trim());
                }

                if (z == 0 && context.Spawner?.Map != null)
                {
                    z = context.Spawner.Map.GetAverageZ(x, y);
                }

                return (new Point3D(x, y, z), map);
            }
        }

        // Try to find spawner by name and use its location
        var spawner = FindSpawnerByName(context.Spawner, TargetLocation);
        if (spawner != null)
        {
            return (spawner.Location, spawner.Map);
        }

        // Try to find by serial
        if (Serial.TryParse(TargetLocation, null, out var serial))
        {
            var item = World.FindItem(serial);
            if (item != null)
            {
                return (item.Location, item.Map);
            }

            var mobile = World.FindMobile(serial);
            if (mobile != null)
            {
                return (mobile.Location, mobile.Map);
            }
        }

        return (Point3D.Zero, null);
    }

    private static ModernSpawner FindSpawnerByName(ModernSpawner sourceSpawner, string name)
    {
        if (sourceSpawner?.Map == null || sourceSpawner.Map == Map.Internal)
        {
            return null;
        }

        foreach (var item in sourceSpawner.Map.GetItemsInRange(sourceSpawner.Location, 256))
        {
            if (item is ModernSpawner spawner &&
                spawner.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
            {
                return spawner;
            }
        }

        return null;
    }
}

/// <summary>
/// AST node that activates/deactivates another spawner.
/// Usage: ACTIVATE/spawnerName or DEACTIVATE/spawnerName
/// </summary>
public class ActivateSpawnerNode : ScriptNode
{
    public string SpawnerIdentifier { get; }
    public bool Activate { get; }

    public ActivateSpawnerNode(string spawnerIdentifier, bool activate)
    {
        SpawnerIdentifier = spawnerIdentifier;
        Activate = activate;
    }

    public override void Execute(ScriptContext context)
    {
        var targetSpawner = FindSpawner(context);
        if (targetSpawner == null)
        {
            return;
        }

        if (Activate)
        {
            targetSpawner.Running = true;
        }
        else
        {
            targetSpawner.Running = false;
        }
    }

    private ModernSpawner FindSpawner(ScriptContext context)
    {
        if (string.IsNullOrEmpty(SpawnerIdentifier))
        {
            return null;
        }

        if (Serial.TryParse(SpawnerIdentifier, null, out var serial))
        {
            var item = World.FindItem(serial);
            if (item is ModernSpawner spawner)
            {
                return spawner;
            }
        }

        return FindSpawnerByName(context.Spawner, SpawnerIdentifier);
    }

    private static ModernSpawner FindSpawnerByName(ModernSpawner sourceSpawner, string name)
    {
        if (sourceSpawner?.Map == null || sourceSpawner.Map == Map.Internal)
        {
            return null;
        }

        foreach (var item in sourceSpawner.Map.GetItemsInRange(sourceSpawner.Location, 256))
        {
            if (item is ModernSpawner spawner &&
                spawner.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
            {
                return spawner;
            }
        }

        return null;
    }
}

/// <summary>
/// AST node that broadcasts a message to nearby players.
/// Usage: BROADCAST/message or BROADCAST/message/range
/// </summary>
public class BroadcastNode : ScriptNode
{
    public string Message { get; }
    public int Range { get; }
    public int Hue { get; }

    public BroadcastNode(string message, int range = 10, int hue = 0x3B2)
    {
        Message = message;
        Range = range;
        Hue = hue;
    }

    public override void Execute(ScriptContext context)
    {
        if (string.IsNullOrEmpty(Message))
        {
            return;
        }

        var spawner = context.Spawner;
        if (spawner?.Map == null || spawner.Map == Map.Internal)
        {
            return;
        }

        foreach (var ns in spawner.Map.GetClientsInRange(spawner.Location, Range))
        {
            ns.Mobile?.SendMessage(Hue, Message);
        }
    }
}

/// <summary>
/// AST node that plays a sound effect.
/// Usage: SOUND/soundId or SOUND/soundId/range
/// </summary>
public class PlaySoundNode : ScriptNode
{
    public int SoundId { get; }
    public int Range { get; }

    public PlaySoundNode(int soundId, int range = 10)
    {
        SoundId = soundId;
        Range = range;
    }

    public override void Execute(ScriptContext context)
    {
        var spawner = context.Spawner;
        if (spawner?.Map == null || spawner.Map == Map.Internal)
        {
            return;
        }

        Effects.PlaySound(spawner.Location, spawner.Map, SoundId);
    }
}

/// <summary>
/// AST node that plays a visual effect.
/// Usage: EFFECT/effectId or EFFECT/effectId/speed/duration
/// </summary>
public class PlayEffectNode : ScriptNode
{
    public int EffectId { get; }
    public int Speed { get; }
    public int Duration { get; }

    public PlayEffectNode(int effectId, int speed = 10, int duration = 30)
    {
        EffectId = effectId;
        Speed = speed;
        Duration = duration;
    }

    public override void Execute(ScriptContext context)
    {
        IEntity target = context.Target as Mobile;
        target ??= context.Target as Item;
        target ??= context.Spawner;

        if (target?.Map == null || target.Map == Map.Internal)
        {
            return;
        }

        Effects.SendLocationEffect(target.Location, target.Map, EffectId, Duration, Speed);
    }
}
