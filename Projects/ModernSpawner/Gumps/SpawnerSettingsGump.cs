using System;
using Server.Gumps;
using Server.Network;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Wizard-style gump for configuring ModernSpawner basic settings.
/// Provides a user-friendly interface for timing, range, and behavior options.
/// </summary>
public class SpawnerSettingsGump : DynamicGump
{
    private readonly ModernSpawner _spawner;

    // Layout constants
    private const int Width = 400;
    private const int Height = 480;
    private const int LabelX = 20;
    private const int InputX = 150;
    private const int RowHeight = 25;

    // Text entry IDs
    private const int TextId_Name = 0;
    private const int TextId_MinDelayMins = 1;
    private const int TextId_MaxDelayMins = 2;
    private const int TextId_HomeRange = 3;
    private const int TextId_WalkingRange = 4;
    private const int TextId_MaxZDelta = 5;
    private const int TextId_Team = 6;
    private const int TextId_Notes = 7;

    // Button IDs
    private const int ButtonId_Save = 1;
    private const int ButtonId_Cancel = 0;
    private const int ButtonId_ToggleRunning = 2;
    private const int ButtonId_ToggleSmartPos = 3;
    private const int ButtonId_ToggleReturnIdle = 4;
    private const int ButtonId_ToggleGroup = 5;
    private const int ButtonId_ToggleReturnDeactivate = 6;
    private const int ButtonId_Triggers = 7;
    private const int ButtonId_Scripts = 8;
    private const int ButtonId_Export = 9;

    public override bool Singleton => true;

    public SpawnerSettingsGump(ModernSpawner spawner) : base(50, 50)
    {
        _spawner = spawner;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        // Background
        builder.AddBackground(0, 0, Width, Height, 5054);
        builder.AddAlphaRegion(0, 0, Width, Height);

        // Title
        builder.AddHtml(0, 10, Width, 20, "Spawner Settings", "#FFEA00", align: TextAlignment.Center);

        var y = 40;

        // --- Basic Info Section ---
        builder.AddHtml(LabelX, y, 200, 20, "Basic Information", "#00BFFF");
        y += RowHeight;

        // Name
        builder.AddLabel(LabelX, y, 0x384, "Name:");
        builder.AddImageTiled(InputX, y - 2, 220, 22, 0xA40);
        builder.AddImageTiled(InputX + 1, y - 1, 218, 20, 0xBBC);
        builder.AddTextEntry(InputX + 4, y - 1, 213, 20, 0, TextId_Name, _spawner.Name ?? "");
        y += RowHeight;

        // Running status with toggle button
        builder.AddLabel(LabelX, y, 0x384, "Status:");
        if (_spawner.Running)
        {
            builder.AddButton(InputX, y - 3, 0x2A4E, 0x2A3A, ButtonId_ToggleRunning);
            builder.AddLabel(InputX + 35, y, 0x55, "Running");
        }
        else
        {
            builder.AddButton(InputX, y - 3, 0x2A62, 0x2A3A, ButtonId_ToggleRunning);
            builder.AddLabel(InputX + 35, y, 0x20, "Stopped");
        }
        y += RowHeight + 5;

        // --- Timing Section ---
        builder.AddHtml(LabelX, y, 200, 20, "Spawn Timing", "#00BFFF");
        y += RowHeight;

        // Min Delay
        builder.AddLabel(LabelX, y, 0x384, "Min Delay (mins):");
        builder.AddImageTiled(InputX, y - 2, 80, 22, 0xA40);
        builder.AddImageTiled(InputX + 1, y - 1, 78, 20, 0xBBC);
        builder.AddTextEntry(InputX + 4, y - 1, 73, 20, 0, TextId_MinDelayMins, _spawner.MinDelay.TotalMinutes.ToString("0.##"));
        y += RowHeight;

        // Max Delay
        builder.AddLabel(LabelX, y, 0x384, "Max Delay (mins):");
        builder.AddImageTiled(InputX, y - 2, 80, 22, 0xA40);
        builder.AddImageTiled(InputX + 1, y - 1, 78, 20, 0xBBC);
        builder.AddTextEntry(InputX + 4, y - 1, 73, 20, 0, TextId_MaxDelayMins, _spawner.MaxDelay.TotalMinutes.ToString("0.##"));
        y += RowHeight;

        // Group Spawn toggle
        builder.AddLabel(LabelX, y, 0x384, "Group Spawn:");
        builder.AddButton(InputX, y - 3, _spawner.Group ? 0xD3 : 0xD2, _spawner.Group ? 0xD2 : 0xD3, ButtonId_ToggleGroup);
        builder.AddLabel(InputX + 25, y, 0x384, _spawner.Group ? "All at once" : "One at a time");
        y += RowHeight + 5;

        // --- Range Section ---
        builder.AddHtml(LabelX, y, 200, 20, "Spawn Range", "#00BFFF");
        y += RowHeight;

        // Home Range
        builder.AddLabel(LabelX, y, 0x384, "Home Range:");
        builder.AddImageTiled(InputX, y - 2, 60, 22, 0xA40);
        builder.AddImageTiled(InputX + 1, y - 1, 58, 20, 0xBBC);
        builder.AddTextEntry(InputX + 4, y - 1, 53, 20, 0, TextId_HomeRange, $"{_spawner.HomeRange}");
        builder.AddLabel(InputX + 70, y, 0x384, "tiles");
        y += RowHeight;

        // Walking Range
        builder.AddLabel(LabelX, y, 0x384, "Walking Range:");
        builder.AddImageTiled(InputX, y - 2, 60, 22, 0xA40);
        builder.AddImageTiled(InputX + 1, y - 1, 58, 20, 0xBBC);
        builder.AddTextEntry(
            InputX + 4,
            y - 1,
            53,
            20,
            0,
            TextId_WalkingRange,
            $"{(_spawner.WalkingRange < 0 ? -1 : _spawner.WalkingRange)}"
        );
        builder.AddLabel(InputX + 70, y, 0x384, "(-1 = same as home)");
        y += RowHeight;

        // Max Z Delta
        builder.AddLabel(LabelX, y, 0x384, "Max Z Delta:");
        builder.AddImageTiled(InputX, y - 2, 60, 22, 0xA40);
        builder.AddImageTiled(InputX + 1, y - 1, 58, 20, 0xBBC);
        builder.AddTextEntry(InputX + 4, y - 1, 53, 20, 0, TextId_MaxZDelta, $"{_spawner.MaxZDelta}");
        y += RowHeight + 5;

        // --- Behavior Section ---
        builder.AddHtml(LabelX, y, 200, 20, "Behavior Options", "#00BFFF");
        y += RowHeight;

        // Smart Positioning toggle
        builder.AddButton(LabelX, y - 3, _spawner.UseSmartPositioning ? 0xD3 : 0xD2,
            _spawner.UseSmartPositioning ? 0xD2 : 0xD3, ButtonId_ToggleSmartPos);
        builder.AddLabel(LabelX + 25, y, 0x384, "Smart Positioning (avoid obstacles)");
        y += RowHeight;

        // Return to Spawn on Idle toggle
        builder.AddButton(LabelX, y - 3, _spawner.ReturnToSpawnOnIdle ? 0xD3 : 0xD2,
            _spawner.ReturnToSpawnOnIdle ? 0xD2 : 0xD3, ButtonId_ToggleReturnIdle);
        builder.AddLabel(LabelX + 25, y, 0x384, "Return to spawn point when idle");
        y += RowHeight;

        // Return on Deactivate toggle
        builder.AddButton(LabelX, y - 3, _spawner.ReturnOnDeactivate ? 0xD3 : 0xD2,
            _spawner.ReturnOnDeactivate ? 0xD2 : 0xD3, ButtonId_ToggleReturnDeactivate);
        builder.AddLabel(LabelX + 25, y, 0x384, "Return spawns when deactivated");
        y += RowHeight + 5;

        // --- Team Section ---
        builder.AddHtml(LabelX, y, 200, 20, "Team Assignment", "#00BFFF");
        y += RowHeight;

        builder.AddLabel(LabelX, y, 0x384, "Team ID:");
        builder.AddImageTiled(InputX, y - 2, 60, 22, 0xA40);
        builder.AddImageTiled(InputX + 1, y - 1, 58, 20, 0xBBC);
        builder.AddTextEntry(InputX + 4, y - 1, 53, 20, 0, TextId_Team, $"{_spawner.Team}");
        y += RowHeight + 5;

        // --- Notes Section ---
        builder.AddHtml(LabelX, y, 200, 20, "Notes", "#00BFFF");
        y += RowHeight;

        builder.AddImageTiled(LabelX, y - 2, Width - 40, 50, 0xA40);
        builder.AddImageTiled(LabelX + 1, y - 1, Width - 42, 48, 0xBBC);
        builder.AddTextEntry(LabelX + 4, y - 1, Width - 48, 46, 0, TextId_Notes, _spawner.Notes ?? "");
        y += 55;

        // --- Quick Actions ---
        builder.AddHtml(LabelX, y, 200, 20, "Quick Actions", "#00BFFF");
        y += RowHeight;

        // Configure Triggers button
        builder.AddButton(LabelX, y, 0xFA5, 0xFA7, ButtonId_Triggers);
        builder.AddLabel(LabelX + 35, y + 3, 0x384, "Configure Triggers...");

        // Configure Scripts button
        builder.AddButton(InputX + 70, y, 0xFA5, 0xFA7, ButtonId_Scripts);
        builder.AddLabel(InputX + 105, y + 3, 0x384, "Configure Scripts...");
        y += RowHeight;

        // Export button
        builder.AddButton(LabelX, y, 0xFA5, 0xFA7, ButtonId_Export);
        builder.AddLabel(LabelX + 35, y + 3, 0x384, "Export to File...");

        // --- Footer Buttons ---
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
                return;

            case ButtonId_Save:
                SaveSettings(info);
                from.SendMessage("Spawner settings saved.");
                return;

            case ButtonId_ToggleRunning:
                _spawner.Running = !_spawner.Running;
                break;

            case ButtonId_ToggleSmartPos:
                _spawner.UseSmartPositioning = !_spawner.UseSmartPositioning;
                break;

            case ButtonId_ToggleReturnIdle:
                _spawner.ReturnToSpawnOnIdle = !_spawner.ReturnToSpawnOnIdle;
                break;

            case ButtonId_ToggleGroup:
                _spawner.Group = !_spawner.Group;
                break;

            case ButtonId_ToggleReturnDeactivate:
                _spawner.ReturnOnDeactivate = !_spawner.ReturnOnDeactivate;
                break;

            case ButtonId_Triggers:
                SaveSettings(info);
                from.SendGump(new TriggerConfigGump(_spawner));
                return;

            case ButtonId_Scripts:
                SaveSettings(info);
                from.SendGump(new ScriptsConfigGump(_spawner));
                return;

            case ButtonId_Export:
                SaveSettings(info);
                from.SendGump(new SpawnerExportGump(_spawner));
                return;
        }

        // Re-send the gump to show updated toggle states
        from.SendGump(new SpawnerSettingsGump(_spawner));
    }

    private void SaveSettings(RelayInfo info)
    {
        // Name
        var nameEntry = info.GetTextEntry(TextId_Name);
        if (nameEntry != null)
        {
            var name = nameEntry.Trim();
            _spawner.Name = string.IsNullOrEmpty(name) ? null : name;
        }

        // Min Delay
        var minDelayEntry = info.GetTextEntry(TextId_MinDelayMins);
        if (minDelayEntry != null && double.TryParse(minDelayEntry.Trim(), out var minMins))
        {
            _spawner.MinDelay = TimeSpan.FromMinutes(Math.Max(0, minMins));
        }

        // Max Delay
        var maxDelayEntry = info.GetTextEntry(TextId_MaxDelayMins);
        if (maxDelayEntry != null && double.TryParse(maxDelayEntry.Trim(), out var maxMins))
        {
            _spawner.MaxDelay = TimeSpan.FromMinutes(Math.Max(0, maxMins));
        }

        // Home Range
        var homeRangeEntry = info.GetTextEntry(TextId_HomeRange);
        if (homeRangeEntry != null && int.TryParse(homeRangeEntry.Trim(), out var homeRange))
        {
            _spawner.HomeRange = Math.Max(0, homeRange);
        }

        // Walking Range
        var walkRangeEntry = info.GetTextEntry(TextId_WalkingRange);
        if (walkRangeEntry != null && int.TryParse(walkRangeEntry.Trim(), out var walkRange))
        {
            _spawner.WalkingRange = walkRange;
        }

        // Max Z Delta
        var maxZEntry = info.GetTextEntry(TextId_MaxZDelta);
        if (maxZEntry != null && int.TryParse(maxZEntry.Trim(), out var maxZ))
        {
            _spawner.MaxZDelta = Math.Max(0, maxZ);
        }

        // Team
        var teamEntry = info.GetTextEntry(TextId_Team);
        if (teamEntry != null && int.TryParse(teamEntry.Trim(), out var team))
        {
            _spawner.Team = team;
        }

        // Notes
        var notesEntry = info.GetTextEntry(TextId_Notes);
        if (notesEntry != null)
        {
            var notes = notesEntry.Trim();
            _spawner.Notes = string.IsNullOrEmpty(notes) ? null : notes;
        }
    }
}
