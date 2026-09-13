using System;
using System.Collections.Generic;
using Server.Engines.ModernSpawner.Triggers;
using Server.Gumps;
using Server.Network;
using Server.Text;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Wizard-style gump for configuring spawner triggers.
/// Allows users to set up proximity triggers, time-based triggers, and scheduled spawning.
/// </summary>
public class TriggerConfigGump : DynamicGump
{
    private readonly ModernSpawner _spawner;
    private readonly int _page;

    // Layout constants
    private const int Width = 450;
    private const int Height = 400;
    private const int TriggersPerPage = 5;

    // Text entry IDs
    private const int TextId_ProximityRange = 100;
    private const int TextId_StartHour = 101;
    private const int TextId_EndHour = 102;

    // Button IDs
    private const int ButtonId_Cancel = 0;
    private const int ButtonId_Save = 1;
    private const int ButtonId_Back = 2;
    private const int ButtonId_PrevPage = 3;
    private const int ButtonId_NextPage = 4;
    private const int ButtonId_ToggleTriggerActivated = 5;
    private const int ButtonId_AddProximity = 10;
    private const int ButtonId_AddTimeWindow = 11;
    private const int ButtonId_AddGameTime = 12;
    private const int ButtonId_DeleteBase = 100;

    public override bool Singleton => true;

    public TriggerConfigGump(ModernSpawner spawner, int page = 0) : base(50, 50)
    {
        _spawner = spawner;
        _page = page;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        // Background
        builder.AddBackground(0, 0, Width, Height, 5054);
        builder.AddAlphaRegion(0, 0, Width, Height);

        // Title
        builder.AddHtml(0, 10, Width, 20, "Trigger Configuration", "#FFEA00", align: TextAlignment.Center);

        var y = 40;

        // Trigger Activated toggle
        builder.AddLabel(20, y, 0x384, "Trigger Mode:");
        if (_spawner.TriggerActivated)
        {
            builder.AddButton(120, y - 3, 0xD3, 0xD2, ButtonId_ToggleTriggerActivated);
            builder.AddLabel(145, y, 0x55, "Enabled - Spawner waits for triggers");
        }
        else
        {
            builder.AddButton(120, y - 3, 0xD2, 0xD3, ButtonId_ToggleTriggerActivated);
            builder.AddLabel(145, y, 0x20, "Disabled - Standard timer-based spawning");
        }
        y += 30;

        // Info text
        builder.AddHtml(20, y, Width - 40, 40,
            "Configure triggers that control when spawning occurs. Multiple triggers use OR logic (any trigger activates spawning).",
            "#C0C0C0");
        y += 50;

        // --- Current Triggers ---
        builder.AddHtml(20, y, 200, 20, "Active Triggers", "#00BFFF");
        y += 25;

        var triggers = GetTriggerList();
        var startIndex = _page * TriggersPerPage;
        var endIndex = Math.Min(startIndex + TriggersPerPage, triggers.Count);

        if (triggers.Count == 0)
        {
            builder.AddHtml(20, y, Width - 40, 20, "No triggers configured. Add triggers below.", "#808080");
            y += 25;
        }
        else
        {
            for (var i = startIndex; i < endIndex; i++)
            {
                var trigger = triggers[i];

                // Delete button
                builder.AddButton(20, y, 0xFA2, 0xFA4, ButtonId_DeleteBase + i);

                // Trigger info
                var sb = ValueStringBuilder.Create();
                FormatTriggerDisplay(trigger.Text, ref sb);
                builder.AddHtml(55, y + 3, Width - 100, 20, sb.AsSpan(), "#F4F4F4");
                y += 25;
                sb.Dispose();
            }
        }

        // Pagination
        if (triggers.Count > TriggersPerPage)
        {
            y += 5;
            if (_page > 0)
            {
                builder.AddButton(150, y, 0x15E3, 0x15E7, ButtonId_PrevPage);
            }

            builder.AddLabel(180, y + 3, 0x384, $"Page {_page + 1}/{(triggers.Count - 1) / TriggersPerPage + 1}");

            if (endIndex < triggers.Count)
            {
                builder.AddButton(250, y, 0x15E1, 0x15E5, ButtonId_NextPage);
            }

            y += 25;
        }

        y += 10;

        // --- Add New Trigger Section ---
        builder.AddHtml(20, y, 200, 20, "Add New Trigger", "#00BFFF");
        y += 25;

        // Proximity Trigger
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_AddProximity);
        builder.AddLabel(55, y + 3, 0x384, "Proximity Trigger");
        builder.AddLabel(180, y + 3, 0x384, "Range:");
        builder.AddImageTiled(220, y, 50, 22, 0xA40);
        builder.AddImageTiled(221, y + 1, 48, 20, 0xBBC);
        builder.AddTextEntry(224, y + 1, 43, 20, 0, TextId_ProximityRange, "15");
        builder.AddLabel(275, y + 3, 0x384, "tiles");
        y += 30;

        // Time Window Trigger (real time)
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_AddTimeWindow);
        builder.AddLabel(55, y + 3, 0x384, "Time Window (Real)");
        builder.AddLabel(180, y + 3, 0x384, "Hours:");
        builder.AddImageTiled(220, y, 35, 22, 0xA40);
        builder.AddImageTiled(221, y + 1, 33, 20, 0xBBC);
        builder.AddTextEntry(224, y + 1, 28, 20, 0, TextId_StartHour, "18");
        builder.AddLabel(260, y + 3, 0x384, "to");
        builder.AddImageTiled(280, y, 35, 22, 0xA40);
        builder.AddImageTiled(281, y + 1, 33, 20, 0xBBC);
        builder.AddTextEntry(284, y + 1, 28, 20, 0, TextId_EndHour, "6");
        y += 30;

        // Game Time Trigger
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_AddGameTime);
        builder.AddLabel(55, y + 3, 0x384, "Game Time (Night/Day)");
        builder.AddLabel(220, y + 3, 0x384, "Spawns during night hours");

        // --- Footer Buttons ---
        builder.AddButton(20, Height - 35, 0xFAE, 0xFAF, ButtonId_Back);
        builder.AddLabel(55, Height - 32, 0x384, "Back");

        builder.AddButton(Width - 160, Height - 35, 0xFB7, 0xFB9, ButtonId_Save);
        builder.AddLabel(Width - 125, Height - 32, 0x384, "Save");

        builder.AddButton(Width - 80, Height - 35, 0xFB1, 0xFB3, ButtonId_Cancel);
        builder.AddLabel(Width - 45, Height - 32, 0x384, "Cancel");
    }

    private IReadOnlyList<TriggerDefinition> GetTriggerList() => _spawner.TriggerDefinitions;

    /// <summary>Appends a minute field, zero-padding single digits so 18:0 renders as 18:00.</summary>
    private static void AppendMinutes(scoped ref ValueStringBuilder sb, ReadOnlySpan<char> minutes)
    {
        if (minutes.Length == 1)
        {
            sb.Append('0');
        }

        sb.Append(minutes);
    }

    private static void FormatTriggerDisplay(string definition, scoped ref ValueStringBuilder sb)
    {
        if (string.IsNullOrEmpty(definition))
        {
            sb.Append("Unknown trigger");
        }

        var span = definition.AsSpan();
        var colonIndex = span.IndexOf(':');
        var triggerType = colonIndex > 0 ? span[..colonIndex] : span;

        if (triggerType.InsensitiveEquals("proximity"))
        {
            // Format: proximity:range:playerOnly
            Span<Range> parts = stackalloc Range[4];
            var count = span.Split(parts, ':');
            var range = count > 1 ? span[parts[1]] : "15";
            var playerOnly = count > 2 && span[parts[2]].InsensitiveEquals("true");
            sb.Append("Proximity: ");
            sb.Append(range);
            sb.Append(" tiles");
            if (playerOnly)
            {
                sb.Append(" (players only)");
            }
            return;
        }

        if (triggerType.InsensitiveEquals("wall_time_window"))
        {
            // Format: wall_time_window:startHour:startMin:endHour:endMin:allowedDays:allowedMonths:timezone
            Span<Range> parts = stackalloc Range[8];
            var count = span.Split(parts, ':');
            var startHour = count > 1 ? span[parts[1]] : "0";
            var startMinute = count > 2 ? span[parts[2]] : "00";
            var endHour = count > 3 ? span[parts[3]] : "23";
            var endMinute = count > 4 ? span[parts[4]] : "59";
            sb.Append("Real Time: ");
            sb.Append(startHour);
            sb.Append(':');
            AppendMinutes(ref sb, startMinute);
            sb.Append(" - ");
            sb.Append(endHour);
            sb.Append(':');
            AppendMinutes(ref sb, endMinute);
            return;
        }

        if (triggerType.InsensitiveEquals("game_time_window"))
        {
            // Format: game_time_window:startHour:endHour:nightOnly:dayOnly
            Span<Range> parts = stackalloc Range[5];
            var count = span.Split(parts, ':');
            if (count > 3 && span[parts[3]].InsensitiveEquals("true"))
            {
                sb.Append("Game Time: Night hours");
                return;
            }

            if (count > 4 && span[parts[4]].InsensitiveEquals("true"))
            {
                sb.Append("Game Time: Day hours");
                return;
            }

            var startHour = count > 1 ? span[parts[1]] : "0";
            var endHour = count > 2 ? span[parts[2]] : "23";
            sb.Append("Game Time: ");
            sb.Append(startHour);
            sb.Append(":00 - ");
            sb.Append(endHour);
            sb.Append(":00");
            return;
        }

        if (triggerType.InsensitiveEquals("speech"))
        {
            Span<Range> parts = stackalloc Range[3];
            var count = span.Split(parts, ':');
            var keyword = count > 1 ? span[parts[1]] : "unknown";
            sb.Append("Speech: \"");
            sb.Append(keyword);
            sb.Append('"');
        }
    }

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        if (_spawner.Deleted)
        {
            return;
        }

        var from = state.Mobile;
        var triggers = GetTriggerList();

        switch (info.ButtonID)
        {
            case ButtonId_Cancel:
                return;

            case ButtonId_Save:
                from.SendMessage("Trigger configuration saved.");
                from.SendGump(new SpawnerSettingsGump(_spawner));
                return;

            case ButtonId_Back:
                from.SendGump(new SpawnerSettingsGump(_spawner));
                return;

            case ButtonId_PrevPage:
                from.SendGump(new TriggerConfigGump(_spawner, Math.Max(0, _page - 1)));
                return;

            case ButtonId_NextPage:
                from.SendGump(new TriggerConfigGump(_spawner, _page + 1));
                return;

            case ButtonId_ToggleTriggerActivated:
                _spawner.TriggerActivated = !_spawner.TriggerActivated;
                break;

            case ButtonId_AddProximity:
                {
                    var rangeEntry = info.GetTextEntry(TextId_ProximityRange);
                    var range = 15;
                    if (rangeEntry != null && int.TryParse(rangeEntry.Trim(), out var parsedRange))
                    {
                        range = Math.Max(1, parsedRange);
                    }
                    _spawner.AddTriggerDefinition($"proximity:{range}:true");
                    from.SendMessage($"Added proximity trigger with {range} tile range.");
                    break;
                }

            case ButtonId_AddTimeWindow:
                {
                    var startEntry = info.GetTextEntry(TextId_StartHour);
                    var endEntry = info.GetTextEntry(TextId_EndHour);
                    var startHour = 18;
                    var endHour = 6;
                    if (startEntry != null && int.TryParse(startEntry.Trim(), out var parsedStart))
                    {
                        startHour = Math.Clamp(parsedStart, 0, 23);
                    }
                    if (endEntry != null && int.TryParse(endEntry.Trim(), out var parsedEnd))
                    {
                        endHour = Math.Clamp(parsedEnd, 0, 23);
                    }
                    // WallTimeWindowTrigger.Parse reads wall_time_window:startHour:startMin:endHour:endMin;
                    // the gump only offers whole hours, so the minute fields are zero.
                    _spawner.AddTriggerDefinition($"wall_time_window:{startHour}:0:{endHour}:0");
                    from.SendMessage($"Added time window trigger: {startHour}:00 - {endHour}:00.");
                    break;
                }

            case ButtonId_AddGameTime:
                // GameTimeWindowTrigger.Parse reads game_time_window:startHour:endHour:nightOnly;
                // NightOnly is the parser's night preset and overrides the hours it is given.
                _spawner.AddTriggerDefinition("game_time_window:21:5:true");
                from.SendMessage("Added game time trigger for night hours.");
                break;

            default:
                // Check for delete button
                if (info.ButtonID >= ButtonId_DeleteBase)
                {
                    var deleteIndex = info.ButtonID - ButtonId_DeleteBase;
                    if (deleteIndex >= 0 && deleteIndex < triggers.Count)
                    {
                        // triggers is the live list: remove by position so duplicate definitions
                        // still delete the one that was clicked. The wrapper marks the spawner dirty,
                        // drops the definition's runtime state and re-registers.
                        _spawner.RemoveTriggerDefinitionAt(deleteIndex);
                        from.SendMessage("Trigger removed.");
                    }
                }
                break;
        }

        // A delete can empty the page that was being viewed, so clamp before re-sending: the list is
        // re-read because the switch above may have added to or removed from it.
        var remaining = GetTriggerList();
        var lastPage = Math.Max(0, (remaining.Count - 1) / TriggersPerPage);
        from.SendGump(new TriggerConfigGump(_spawner, Math.Min(_page, lastPage)));
    }
}
