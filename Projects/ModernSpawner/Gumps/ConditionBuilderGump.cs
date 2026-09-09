using System.Collections.Generic;
using System.Text;
using Server.Gumps;
using Server.Network;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Target type for the condition builder.
/// </summary>
public enum ConditionBuilderTarget
{
    Spawner,        // Building condition for spawner scripts
    Entry,          // Building condition for entry OnSpawn/OnDespawn
    PropertyValue   // Building a property value expression
}

/// <summary>
/// Wizard-style gump for building script conditions visually.
/// Generates expression language code from user selections.
/// </summary>
public class ConditionBuilderGump : DynamicGump
{
    private readonly ModernSpawner _spawner;
    private readonly ModernSpawnerEntry _entry;
    private readonly ConditionBuilderTarget _target;
    private readonly List<ConditionPart> _conditions;

    // Layout constants
    private const int Width = 550;
    private const int Height = 480;
    private const int ConditionsPerPage = 5;

    // Text entry IDs
    private const int TextId_CustomValue = 50;
    private const int TextId_PreviewBase = 100;

    // Button IDs
    private const int ButtonId_Cancel = 0;
    private const int ButtonId_Apply = 1;
    private const int ButtonId_Back = 2;
    private const int ButtonId_Clear = 3;

    // Add condition buttons
    private const int ButtonId_AddTimeNight = 10;
    private const int ButtonId_AddTimeDay = 11;
    private const int ButtonId_AddPlayersNearby = 12;
    private const int ButtonId_AddKarmaLow = 13;
    private const int ButtonId_AddKarmaHigh = 14;
    private const int ButtonId_AddCancel = 15;
    private const int ButtonId_AddRandom = 16;
    private const int ButtonId_AddSpawnCount = 17;

    // Logic buttons
    private const int ButtonId_LogicAnd = 30;
    private const int ButtonId_LogicOr = 31;

    // Delete condition base
    private const int ButtonId_DeleteBase = 200;

    public override bool Singleton => true;

    public ConditionBuilderGump(ModernSpawner spawner, ModernSpawnerEntry entry, ConditionBuilderTarget target)
        : this(spawner, entry, target, new List<ConditionPart>())
    {
    }

    private ConditionBuilderGump(ModernSpawner spawner, ModernSpawnerEntry entry,
        ConditionBuilderTarget target, List<ConditionPart> conditions) : base(50, 50)
    {
        _spawner = spawner;
        _entry = entry;
        _target = target;
        _conditions = conditions;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        // Background
        builder.AddBackground(0, 0, Width, Height, 5054);
        builder.AddAlphaRegion(0, 0, Width, Height);

        // Title
        var title = _target switch
        {
            ConditionBuilderTarget.Entry => "Entry Condition Builder",
            ConditionBuilderTarget.PropertyValue => "Property Value Builder",
            _ => "Condition Builder"
        };
        builder.AddHtml(0, 10, Width, 20, title, "#FFEA00", align: TextAlignment.Center);

        var y = 40;

        // Info text
        builder.AddHtml(20, y, Width - 40, 30,
            "Build conditions visually. Click buttons to add condition parts, then Apply to generate the expression.",
            "#C0C0C0");
        y += 40;

        // --- Preview Section ---
        builder.AddHtml(20, y, 200, 20, "Generated Expression", "#00BFFF");
        y += 22;
        var expression = BuildExpression();
        builder.AddImageTiled(20, y, Width - 40, 50, 0xA40);
        builder.AddImageTiled(21, y + 1, Width - 42, 48, 0xE14);
        builder.AddHtml(24, y + 4, Width - 48, 44, string.IsNullOrEmpty(expression) ? "(empty)" : expression, "#00FF00");
        y += 60;

        // --- Current Conditions ---
        builder.AddHtml(20, y, 200, 20, "Condition Parts", "#00BFFF");
        builder.AddButton(Width - 80, y - 2, 0xFA2, 0xFA4, ButtonId_Clear);
        builder.AddLabel(Width - 45, y, 0x384, "Clear");
        y += 25;

        if (_conditions.Count == 0)
        {
            builder.AddHtml(20, y, Width - 40, 20, "No conditions added yet. Use the buttons below.", "#808080");
            y += 25;
        }
        else
        {
            for (var i = 0; i < _conditions.Count && i < ConditionsPerPage; i++)
            {
                var condition = _conditions[i];

                // Delete button
                builder.AddButton(20, y, 0xFA2, 0xFA4, ButtonId_DeleteBase + i);

                // Condition display
                builder.AddHtml(55, y + 3, Width - 100, 20, condition.Display, "#F4F4F4");
                y += 25;
            }
        }

        y += 10;

        // --- Add Conditions Section ---
        builder.AddHtml(20, y, 200, 20, "Add Condition", "#00BFFF");
        y += 25;

        // Row 1: Time conditions
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_AddTimeNight);
        builder.AddLabel(55, y + 3, 0x384, "Is Night");

        builder.AddButton(140, y, 0xFA5, 0xFA7, ButtonId_AddTimeDay);
        builder.AddLabel(175, y + 3, 0x384, "Is Day");

        builder.AddButton(260, y, 0xFA5, 0xFA7, ButtonId_AddPlayersNearby);
        builder.AddLabel(295, y + 3, 0x384, "Players Nearby > 0");
        y += 28;

        // Row 2: Karma conditions
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_AddKarmaLow);
        builder.AddLabel(55, y + 3, 0x384, "Karma < 0");

        builder.AddButton(160, y, 0xFA5, 0xFA7, ButtonId_AddKarmaHigh);
        builder.AddLabel(195, y + 3, 0x384, "Karma > 0");
        y += 28;

        // Row 3: Spawn conditions
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_AddSpawnCount);
        builder.AddLabel(55, y + 3, 0x384, "Spawn Count Check");

        builder.AddButton(200, y, 0xFA5, 0xFA7, ButtonId_AddRandom);
        builder.AddLabel(235, y + 3, 0x384, "Random Value");
        y += 28;

        // Row 4: Action conditions
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_AddCancel);
        builder.AddLabel(55, y + 3, 0x384, "Cancel Spawn");
        y += 35;

        // --- Logic Section ---
        builder.AddHtml(20, y, 200, 20, "Logic Operators", "#00BFFF");
        y += 25;

        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_LogicAnd);
        builder.AddLabel(55, y + 3, 0x384, "AND");

        builder.AddButton(120, y, 0xFA5, 0xFA7, ButtonId_LogicOr);
        builder.AddLabel(155, y + 3, 0x384, "OR");

        // --- Footer Buttons ---
        builder.AddButton(20, Height - 35, 0xFAE, 0xFAF, ButtonId_Back);
        builder.AddLabel(55, Height - 32, 0x384, "Back");

        builder.AddButton(Width - 160, Height - 35, 0xFB7, 0xFB9, ButtonId_Apply);
        builder.AddLabel(Width - 125, Height - 32, 0x384, "Apply");

        builder.AddButton(Width - 80, Height - 35, 0xFB1, 0xFB3, ButtonId_Cancel);
        builder.AddLabel(Width - 45, Height - 32, 0x384, "Cancel");
    }

    private string BuildExpression()
    {
        if (_conditions.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < _conditions.Count; i++)
        {
            var condition = _conditions[i];

            if (condition.Type == ConditionPartType.LogicOperator)
            {
                sb.Append($" {condition.Expression} ");
            }
            else
            {
                sb.Append(condition.Expression);
            }
        }

        return sb.ToString().Trim();
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

            case ButtonId_Apply:
                ApplyExpression(from);
                return;

            case ButtonId_Back:
                GoBack(from);
                return;

            case ButtonId_Clear:
                _conditions.Clear();
                break;

            case ButtonId_AddTimeNight:
                _conditions.Add(new ConditionPart(ConditionPartType.Function, "isNight()", "Is Night"));
                break;

            case ButtonId_AddTimeDay:
                _conditions.Add(new ConditionPart(ConditionPartType.Function, "isDay()", "Is Day"));
                break;

            case ButtonId_AddPlayersNearby:
                _conditions.Add(new ConditionPart(ConditionPartType.Comparison, "playersNearby(20) > 0", "Players within 20 tiles"));
                break;

            case ButtonId_AddKarmaLow:
                _conditions.Add(new ConditionPart(ConditionPartType.Comparison, "trigMob.Karma < 0", "Triggering mobile has negative karma"));
                break;

            case ButtonId_AddKarmaHigh:
                _conditions.Add(new ConditionPart(ConditionPartType.Comparison, "trigMob.Karma > 0", "Triggering mobile has positive karma"));
                break;

            case ButtonId_AddSpawnCount:
                _conditions.Add(new ConditionPart(ConditionPartType.Comparison, "spawner.SpawnedCount < spawner.Count", "Not at max spawns"));
                break;

            case ButtonId_AddRandom:
                _conditions.Add(new ConditionPart(ConditionPartType.Function, "random(1, 100) > 50", "50% chance"));
                break;

            case ButtonId_AddCancel:
                _conditions.Add(new ConditionPart(ConditionPartType.Action, "cancel()", "Cancel spawn"));
                break;

            case ButtonId_LogicAnd:
                _conditions.Add(new ConditionPart(ConditionPartType.LogicOperator, "and", "AND"));
                break;

            case ButtonId_LogicOr:
                _conditions.Add(new ConditionPart(ConditionPartType.LogicOperator, "or", "OR"));
                break;

            default:
                // Check for delete button
                if (info.ButtonID >= ButtonId_DeleteBase)
                {
                    var deleteIndex = info.ButtonID - ButtonId_DeleteBase;
                    if (deleteIndex >= 0 && deleteIndex < _conditions.Count)
                    {
                        _conditions.RemoveAt(deleteIndex);
                    }
                }
                break;
        }

        from.SendGump(new ConditionBuilderGump(_spawner, _entry, _target, _conditions));
    }

    private void ApplyExpression(Mobile from)
    {
        var expression = BuildExpression();
        if (string.IsNullOrEmpty(expression))
        {
            from.SendMessage("No expression built. Nothing applied.");
            GoBack(from);
            return;
        }

        // Copy to clipboard-like behavior - just show message for now
        from.SendMessage($"Expression: {expression}");
        from.SendMessage("Copy this expression to the appropriate script field.");

        GoBack(from);
    }

    private void GoBack(Mobile from)
    {
        switch (_target)
        {
            case ConditionBuilderTarget.Entry when _entry != null:
                from.SendGump(new SpawnerEntryWizardGump(_spawner, _entry));
                break;

            case ConditionBuilderTarget.Spawner:
                from.SendGump(new ScriptsConfigGump(_spawner));
                break;

            default:
                from.SendGump(new SpawnerSettingsGump(_spawner));
                break;
        }
    }

    private enum ConditionPartType
    {
        Function,
        Comparison,
        Action,
        LogicOperator
    }

    private class ConditionPart
    {
        public ConditionPartType Type { get; }
        public string Expression { get; }
        public string Display { get; }

        public ConditionPart(ConditionPartType type, string expression, string display)
        {
            Type = type;
            Expression = expression;
            Display = display;
        }
    }
}
