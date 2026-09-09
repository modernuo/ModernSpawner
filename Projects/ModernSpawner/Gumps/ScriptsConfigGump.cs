using Server.Gumps;
using Server.Network;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Wizard-style gump for configuring spawner-level scripts.
/// Allows users to set up OnActivate, OnDeactivate, OnBeforeSpawn, and OnAfterSpawn scripts.
/// </summary>
public class ScriptsConfigGump : DynamicGump
{
    private readonly ModernSpawner _spawner;

    // Layout constants
    private const int Width = 500;
    private const int Height = 450;

    // Text entry IDs
    private const int TextId_OnActivate = 0;
    private const int TextId_OnDeactivate = 1;
    private const int TextId_OnBeforeSpawn = 2;
    private const int TextId_OnAfterSpawn = 3;

    // Button IDs
    private const int ButtonId_Cancel = 0;
    private const int ButtonId_Save = 1;
    private const int ButtonId_Back = 2;
    private const int ButtonId_HelpActivate = 10;
    private const int ButtonId_HelpDeactivate = 11;
    private const int ButtonId_HelpBeforeSpawn = 12;
    private const int ButtonId_HelpAfterSpawn = 13;
    private const int ButtonId_ConditionBuilder = 20;

    public override bool Singleton => true;

    public ScriptsConfigGump(ModernSpawner spawner) : base(50, 50)
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
        builder.AddHtml(0, 10, Width, 20, "Script Configuration", "#FFEA00", align: TextAlignment.Center);

        var y = 40;

        // Info text
        builder.AddHtml(20, y, Width - 40, 40,
            "Configure scripts that execute at different spawner lifecycle events. Use the expression syntax or the Condition Builder for complex logic.",
            "#C0C0C0");
        y += 50;

        // Condition Builder button
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_ConditionBuilder);
        builder.AddLabel(55, y + 3, 0x55, "Open Condition Builder...");
        y += 35;

        // --- OnActivate Script ---
        builder.AddHtml(20, y, 300, 20, "OnActivate Script", "#00BFFF");
        builder.AddButton(Width - 50, y, 0x15E1, 0x15E5, ButtonId_HelpActivate);
        builder.AddLabel(Width - 35, y + 3, 0x384, "?");
        y += 22;
        builder.AddHtml(20, y, Width - 40, 15, "Executes when spawner becomes active", "#808080");
        y += 18;
        builder.AddImageTiled(20, y, Width - 40, 40, 0xA40);
        builder.AddImageTiled(21, y + 1, Width - 42, 38, 0xBBC);
        builder.AddTextEntry(24, y + 1, Width - 48, 36, 0, TextId_OnActivate, _spawner.OnActivateScript?.Source ?? "");
        y += 50;

        // --- OnDeactivate Script ---
        builder.AddHtml(20, y, 300, 20, "OnDeactivate Script", "#00BFFF");
        builder.AddButton(Width - 50, y, 0x15E1, 0x15E5, ButtonId_HelpDeactivate);
        builder.AddLabel(Width - 35, y + 3, 0x384, "?");
        y += 22;
        builder.AddHtml(20, y, Width - 40, 15, "Executes when spawner becomes inactive", "#808080");
        y += 18;
        builder.AddImageTiled(20, y, Width - 40, 40, 0xA40);
        builder.AddImageTiled(21, y + 1, Width - 42, 38, 0xBBC);
        builder.AddTextEntry(24, y + 1, Width - 48, 36, 0, TextId_OnDeactivate, _spawner.OnDeactivateScript?.Source ?? "");
        y += 50;

        // --- OnBeforeSpawn Script ---
        builder.AddHtml(20, y, 300, 20, "OnBeforeSpawn Script", "#00BFFF");
        builder.AddButton(Width - 50, y, 0x15E1, 0x15E5, ButtonId_HelpBeforeSpawn);
        builder.AddLabel(Width - 35, y + 3, 0x384, "?");
        y += 22;
        builder.AddHtml(20, y, Width - 40, 15, "Executes before each spawn attempt (can cancel)", "#808080");
        y += 18;
        builder.AddImageTiled(20, y, Width - 40, 40, 0xA40);
        builder.AddImageTiled(21, y + 1, Width - 42, 38, 0xBBC);
        builder.AddTextEntry(24, y + 1, Width - 48, 36, 0, TextId_OnBeforeSpawn, _spawner.OnBeforeSpawnScript?.Source ?? "");
        y += 50;

        // --- OnAfterSpawn Script ---
        builder.AddHtml(20, y, 300, 20, "OnAfterSpawn Script", "#00BFFF");
        builder.AddButton(Width - 50, y, 0x15E1, 0x15E5, ButtonId_HelpAfterSpawn);
        builder.AddLabel(Width - 35, y + 3, 0x384, "?");
        y += 22;
        builder.AddHtml(20, y, Width - 40, 15, "Executes after a successful spawn", "#808080");
        y += 18;
        builder.AddImageTiled(20, y, Width - 40, 40, 0xA40);
        builder.AddImageTiled(21, y + 1, Width - 42, 38, 0xBBC);
        builder.AddTextEntry(24, y + 1, Width - 48, 36, 0, TextId_OnAfterSpawn, _spawner.OnAfterSpawnScript?.Source ?? "");

        // --- Footer Buttons ---
        builder.AddButton(20, Height - 35, 0xFAE, 0xFAF, ButtonId_Back);
        builder.AddLabel(55, Height - 32, 0x384, "Back");

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
                SaveScripts(info);
                from.SendMessage("Script configuration saved.");
                from.SendGump(new SpawnerSettingsGump(_spawner));
                return;

            case ButtonId_Back:
                from.SendGump(new SpawnerSettingsGump(_spawner));
                return;

            case ButtonId_ConditionBuilder:
                SaveScripts(info);
                from.SendGump(new ConditionBuilderGump(_spawner, null, ConditionBuilderTarget.Spawner));
                return;

            case ButtonId_HelpActivate:
                from.SendMessage("OnActivate: Runs when the spawner starts. Example: broadcast(\"The dungeon awakens!\", 30)");
                break;

            case ButtonId_HelpDeactivate:
                from.SendMessage("OnDeactivate: Runs when the spawner stops. Example: broadcast(\"The dungeon grows quiet.\", 30)");
                break;

            case ButtonId_HelpBeforeSpawn:
                from.SendMessage("OnBeforeSpawn: Runs before spawning. Use cancel() to prevent spawn. Example: if playersNearby(20) < 1 then cancel()");
                break;

            case ButtonId_HelpAfterSpawn:
                from.SendMessage("OnAfterSpawn: Runs after a successful spawn. Example: sound(0x1F5)");
                break;
        }

        from.SendGump(new ScriptsConfigGump(_spawner));
    }

    private void SaveScripts(RelayInfo info)
    {
        // OnActivate
        var activateEntry = info.GetTextEntry(TextId_OnActivate);
        if (activateEntry != null)
        {
            _spawner.SetOnActivateScript(activateEntry.Trim());
        }

        // OnDeactivate
        var deactivateEntry = info.GetTextEntry(TextId_OnDeactivate);
        if (deactivateEntry != null)
        {
            _spawner.SetOnDeactivateScript(deactivateEntry.Trim());
        }

        // OnBeforeSpawn
        var beforeEntry = info.GetTextEntry(TextId_OnBeforeSpawn);
        if (beforeEntry != null)
        {
            _spawner.SetOnBeforeSpawnScript(beforeEntry.Trim());
        }

        // OnAfterSpawn
        var afterEntry = info.GetTextEntry(TextId_OnAfterSpawn);
        if (afterEntry != null)
        {
            _spawner.SetOnAfterSpawnScript(afterEntry.Trim());
        }
    }
}
