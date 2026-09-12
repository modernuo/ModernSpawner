using System;
using Server.Engines.Spawners;
using Server.Gumps;
using Server.Network;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Wizard-style gump for configuring individual spawn entries with property builder.
/// Provides a user-friendly interface for entry type, properties, and scripts.
/// </summary>
public class SpawnerEntryWizardGump : DynamicGump
{
    private readonly ModernSpawner _spawner;
    private readonly ModernSpawnerEntry _entry;

    // Layout constants
    private const int Width = 480;
    private const int Height = 520;

    // Text entry IDs
    private const int TextId_TypeName = 0;
    private const int TextId_MaxCount = 1;
    private const int TextId_Probability = 2;
    private const int TextId_Properties = 3;
    private const int TextId_Parameters = 4;
    private const int TextId_OnSpawnScript = 5;
    private const int TextId_OnDespawnScript = 6;
    private const int TextId_PositioningRule = 7;
    private const int TextId_SpawnGroup = 8;
    private const int TextId_MinDelayMins = 9;
    private const int TextId_MaxDelayMins = 10;

    // Button IDs
    private const int ButtonId_Cancel = 0;
    private const int ButtonId_Save = 1;
    private const int ButtonId_Delete = 2;
    private const int ButtonId_PropertyBuilder = 3;
    private const int ButtonId_ConditionBuilder = 4;
    private const int ButtonId_HelpType = 10;
    private const int ButtonId_HelpProps = 11;
    private const int ButtonId_HelpParams = 12;
    private const int ButtonId_HelpOnSpawn = 13;
    private const int ButtonId_HelpPosRule = 14;

    public override bool Singleton => true;

    public SpawnerEntryWizardGump(ModernSpawner spawner, ModernSpawnerEntry entry) : base(50, 50)
    {
        _spawner = spawner;
        _entry = entry;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        // Background
        builder.AddBackground(0, 0, Width, Height, 5054);
        builder.AddAlphaRegion(0, 0, Width, Height);

        // Title
        builder.AddHtml(0, 10, Width, 20, "Spawn Entry Configuration", "#FFEA00", align: TextAlignment.Center);

        var y = 40;

        // --- Basic Info Section ---
        builder.AddHtml(20, y, 200, 20, "Basic Information", "#00BFFF");
        y += 25;

        // Type Name
        builder.AddLabel(20, y, 0x384, "Type:");
        builder.AddImageTiled(80, y - 2, 280, 22, 0xA40);
        builder.AddImageTiled(81, y - 1, 278, 20, 0xBBC);
        builder.AddTextEntry(84, y - 1, 273, 20,
            (_entry.Valid & EntryFlags.InvalidType) != 0 ? 33 : 0,
            TextId_TypeName, _entry.SpawnedName ?? "");
        builder.AddButton(370, y - 2, 0x15E1, 0x15E5, ButtonId_HelpType);
        builder.AddLabel(388, y, 0x384, "?");
        y += 28;

        // Max Count and Probability
        builder.AddLabel(20, y, 0x384, "Max Count:");
        builder.AddImageTiled(90, y - 2, 50, 22, 0xA40);
        builder.AddImageTiled(91, y - 1, 48, 20, 0xBBC);
        builder.AddTextEntry(94, y - 1, 43, 20, 0, TextId_MaxCount, $"{_entry.SpawnedMaxCount}");

        builder.AddLabel(160, y, 0x384, "Probability:");
        builder.AddImageTiled(240, y - 2, 50, 22, 0xA40);
        builder.AddImageTiled(241, y - 1, 48, 20, 0xBBC);
        builder.AddTextEntry(244, y - 1, 43, 20, 0, TextId_Probability, $"{_entry.SpawnedProbability}");
        builder.AddLabel(295, y, 0x384, "%");
        y += 35;

        // --- Timing Section ---
        builder.AddHtml(20, y, 200, 20, "Entry-Specific Timing (Optional)", "#00BFFF");
        y += 25;

        // Min/Max Delay
        builder.AddLabel(20, y, 0x384, "Min Delay (mins):");
        builder.AddImageTiled(130, y - 2, 60, 22, 0xA40);
        builder.AddImageTiled(131, y - 1, 58, 20, 0xBBC);
        var minDelay = _entry.MinDelay == TimeSpan.Zero ? "" : _entry.MinDelay.TotalMinutes.ToString("0.##");
        builder.AddTextEntry(134, y - 1, 53, 20, 0, TextId_MinDelayMins, minDelay);

        builder.AddLabel(210, y, 0x384, "Max Delay:");
        builder.AddImageTiled(290, y - 2, 60, 22, 0xA40);
        builder.AddImageTiled(291, y - 1, 58, 20, 0xBBC);
        var maxDelay = _entry.MaxDelay == TimeSpan.Zero ? "" : _entry.MaxDelay.TotalMinutes.ToString("0.##");
        builder.AddTextEntry(294, y - 1, 53, 20, 0, TextId_MaxDelayMins, maxDelay);
        builder.AddLabel(355, y, 0x384, "(empty = use spawner)");
        y += 35;

        // --- Properties Section ---
        builder.AddHtml(20, y, 200, 20, "Properties", "#00BFFF");
        builder.AddButton(Width - 180, y - 2, 0xFA5, 0xFA7, ButtonId_PropertyBuilder);
        builder.AddLabel(Width - 145, y, 0x55, "Property Builder...");
        y += 25;

        // Properties field
        builder.AddLabel(20, y, 0x384, "Props:");
        builder.AddButton(65, y - 2, 0x15E1, 0x15E5, ButtonId_HelpProps);
        builder.AddLabel(82, y, 0x384, "?");
        builder.AddImageTiled(100, y - 2, Width - 120, 22, 0xA40);
        builder.AddImageTiled(101, y - 1, Width - 122, 20, 0xBBC);
        builder.AddTextEntry(104, y - 1, Width - 128, 20,
            (_entry.Valid & EntryFlags.InvalidProps) != 0 ? 33 : 0,
            TextId_Properties, _entry.Properties ?? "");
        y += 28;

        // Parameters field
        builder.AddLabel(20, y, 0x384, "Params:");
        builder.AddButton(75, y - 2, 0x15E1, 0x15E5, ButtonId_HelpParams);
        builder.AddLabel(92, y, 0x384, "?");
        builder.AddImageTiled(115, y - 2, Width - 135, 22, 0xA40);
        builder.AddImageTiled(116, y - 1, Width - 137, 20, 0xBBC);
        builder.AddTextEntry(119, y - 1, Width - 143, 20,
            (_entry.Valid & EntryFlags.InvalidParams) != 0 ? 33 : 0,
            TextId_Parameters, _entry.Parameters ?? "");
        y += 35;

        // --- Scripts Section ---
        builder.AddHtml(20, y, 200, 20, "Scripts", "#00BFFF");
        builder.AddButton(Width - 180, y - 2, 0xFA5, 0xFA7, ButtonId_ConditionBuilder);
        builder.AddLabel(Width - 145, y, 0x55, "Condition Builder...");
        y += 25;

        // OnSpawn Script
        builder.AddLabel(20, y, 0x384, "OnSpawn:");
        builder.AddButton(85, y - 2, 0x15E1, 0x15E5, ButtonId_HelpOnSpawn);
        builder.AddLabel(102, y, 0x384, "?");
        y += 20;
        builder.AddImageTiled(20, y, Width - 40, 35, 0xA40);
        builder.AddImageTiled(21, y + 1, Width - 42, 33, 0xBBC);
        builder.AddTextEntry(24, y + 1, Width - 48, 31, 0, TextId_OnSpawnScript, _entry.OnSpawnScript ?? "");
        y += 42;

        // OnDespawn Script
        builder.AddLabel(20, y, 0x384, "OnDespawn:");
        y += 20;
        builder.AddImageTiled(20, y, Width - 40, 35, 0xA40);
        builder.AddImageTiled(21, y + 1, Width - 42, 33, 0xBBC);
        builder.AddTextEntry(24, y + 1, Width - 48, 31, 0, TextId_OnDespawnScript, _entry.OnDespawnScript ?? "");
        y += 45;

        // --- Positioning Section ---
        builder.AddHtml(20, y, 200, 20, "Positioning", "#00BFFF");
        y += 25;

        // Positioning Rule
        builder.AddLabel(20, y, 0x384, "Rule:");
        builder.AddButton(55, y - 2, 0x15E1, 0x15E5, ButtonId_HelpPosRule);
        builder.AddLabel(72, y, 0x384, "?");
        builder.AddImageTiled(95, y - 2, 200, 22, 0xA40);
        builder.AddImageTiled(96, y - 1, 198, 20, 0xBBC);
        builder.AddTextEntry(99, y - 1, 193, 20, 0, TextId_PositioningRule, _entry.PositioningRule ?? "");

        // Spawn Group
        builder.AddLabel(310, y, 0x384, "Group:");
        builder.AddImageTiled(360, y - 2, 100, 22, 0xA40);
        builder.AddImageTiled(361, y - 1, 98, 20, 0xBBC);
        builder.AddTextEntry(364, y - 1, 93, 20, 0, TextId_SpawnGroup, _entry.SpawnGroup ?? "");

        // --- Footer Buttons ---
        builder.AddButton(20, Height - 35, 0xFA2, 0xFA4, ButtonId_Delete);
        builder.AddLabel(55, Height - 32, 0x20, "Delete Entry");

        builder.AddButton(Width - 160, Height - 35, 0xFB7, 0xFB9, ButtonId_Save);
        builder.AddLabel(Width - 125, Height - 32, 0x384, "Save");

        builder.AddButton(Width - 80, Height - 35, 0xFB1, 0xFB3, ButtonId_Cancel);
        builder.AddLabel(Width - 45, Height - 32, 0x384, "Cancel");
    }

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        if (_spawner.Deleted)
        {
            return;
        }

        var from = state.Mobile;

        switch (info.ButtonID)
        {
            case ButtonId_Cancel:
                from.SendGump(new ModernSpawnerGump(_spawner));
                return;

            case ButtonId_Save:
                SaveEntry(info);
                from.SendMessage("Entry saved.");
                from.SendGump(new ModernSpawnerGump(_spawner, _entry));
                return;

            case ButtonId_Delete:
                _spawner.RemoveEntry(_entry);
                from.SendMessage("Entry deleted.");
                from.SendGump(new ModernSpawnerGump(_spawner));
                return;

            case ButtonId_PropertyBuilder:
                SaveEntry(info);
                from.SendGump(new PropertyBuilderGump(_spawner, _entry));
                return;

            case ButtonId_ConditionBuilder:
                SaveEntry(info);
                from.SendGump(new ConditionBuilderGump(_spawner, _entry, ConditionBuilderTarget.Entry));
                return;

            case ButtonId_HelpType:
                from.SendMessage("Type: The type name of the creature/item to spawn. Examples: Orc, Dragon, Gold");
                break;

            case ButtonId_HelpProps:
                from.SendMessage("Props: Set spawn properties. Format: PropName=Value/PropName2={min-max}. Example: Hue=0x8000/Str={80-120}");
                break;

            case ButtonId_HelpParams:
                from.SendMessage("Params: Constructor parameters. Format: param1/param2. Example: 100/500 for Gold(100, 500)");
                break;

            case ButtonId_HelpOnSpawn:
                from.SendMessage("OnSpawn: Script executed when this entity spawns. Use 'target' to reference the spawned entity.");
                break;

            case ButtonId_HelpPosRule:
                from.SendMessage("Positioning Rule: Special positioning. Examples: nearwater, avoidplayers, atspawner");
                break;
        }

        from.SendGump(new SpawnerEntryWizardGump(_spawner, _entry));
    }

    private void SaveEntry(RelayInfo info)
    {
        // Type Name
        var typeEntry = info.GetTextEntry(TextId_TypeName);
        if (typeEntry != null)
        {
            _entry.SpawnedName = typeEntry.Trim();
        }

        // Max Count
        var maxCountEntry = info.GetTextEntry(TextId_MaxCount);
        if (maxCountEntry != null && int.TryParse(maxCountEntry.Trim(), out var maxCount))
        {
            _entry.SpawnedMaxCount = Math.Max(1, maxCount);
        }

        // Probability
        var probEntry = info.GetTextEntry(TextId_Probability);
        if (probEntry != null && int.TryParse(probEntry.Trim(), out var prob))
        {
            _entry.SpawnedProbability = Math.Clamp(prob, 0, 100);
        }

        // Properties
        var propsEntry = info.GetTextEntry(TextId_Properties);
        if (propsEntry != null)
        {
            var props = propsEntry.Trim();
            _entry.Properties = string.IsNullOrEmpty(props) ? null : props;
        }

        // Parameters
        var paramsEntry = info.GetTextEntry(TextId_Parameters);
        if (paramsEntry != null)
        {
            var parms = paramsEntry.Trim();
            _entry.Parameters = string.IsNullOrEmpty(parms) ? null : parms;
        }

        // OnSpawn Script
        var onSpawnEntry = info.GetTextEntry(TextId_OnSpawnScript);
        if (onSpawnEntry != null)
        {
            var script = onSpawnEntry.Trim();
            _entry.OnSpawnScript = string.IsNullOrEmpty(script) ? null : script;
        }

        // OnDespawn Script
        var onDespawnEntry = info.GetTextEntry(TextId_OnDespawnScript);
        if (onDespawnEntry != null)
        {
            var script = onDespawnEntry.Trim();
            _entry.OnDespawnScript = string.IsNullOrEmpty(script) ? null : script;
        }

        // Positioning Rule
        var posRuleEntry = info.GetTextEntry(TextId_PositioningRule);
        if (posRuleEntry != null)
        {
            var rule = posRuleEntry.Trim();
            _entry.PositioningRule = string.IsNullOrEmpty(rule) ? null : rule;
        }

        // Spawn Group
        var groupEntry = info.GetTextEntry(TextId_SpawnGroup);
        if (groupEntry != null)
        {
            var group = groupEntry.Trim();
            _entry.SpawnGroup = string.IsNullOrEmpty(group) ? null : group;
        }

        // Min Delay
        var minDelayEntry = info.GetTextEntry(TextId_MinDelayMins);
        if (minDelayEntry != null)
        {
            var minDelayText = minDelayEntry.Trim();
            if (string.IsNullOrEmpty(minDelayText))
            {
                _entry.MinDelay = TimeSpan.Zero;
            }
            else if (double.TryParse(minDelayText, out var minMins))
            {
                _entry.MinDelay = TimeSpan.FromMinutes(Math.Max(0, minMins));
            }
        }

        // Max Delay
        var maxDelayEntry = info.GetTextEntry(TextId_MaxDelayMins);
        if (maxDelayEntry != null)
        {
            var maxDelayText = maxDelayEntry.Trim();
            if (string.IsNullOrEmpty(maxDelayText))
            {
                _entry.MaxDelay = TimeSpan.Zero;
            }
            else if (double.TryParse(maxDelayText, out var maxMins))
            {
                _entry.MaxDelay = TimeSpan.FromMinutes(Math.Max(0, maxMins));
            }
        }
    }
}
