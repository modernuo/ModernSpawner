using System;
using System.Collections.Generic;
using Server.Gumps;
using Server.Network;
using Server.Text;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Wizard-style gump for building spawn entry properties visually.
/// Allows users to set property values (fixed or random range) without knowing the syntax.
/// </summary>
public class PropertyBuilderGump : DynamicGump
{
    private readonly ModernSpawner _spawner;
    private readonly ModernSpawnerEntry _entry;
    private readonly List<PropertyPart> _properties;

    // Layout constants
    private const int Width = 500;
    private const int Height = 450;
    private const int PropertiesPerPage = 6;

    // Text entry IDs
    private const int TextId_PropNameBase = 0;
    private const int TextId_PropValueBase = 20;
    private const int TextId_PropMinBase = 40;
    private const int TextId_PropMaxBase = 60;

    // Button IDs
    private const int ButtonId_Cancel = 0;
    private const int ButtonId_Apply = 1;
    private const int ButtonId_Back = 2;
    private const int ButtonId_AddProperty = 3;
    private const int ButtonId_DeleteBase = 100;
    private const int ButtonId_ToggleTypeBase = 200;

    // Common property presets
    private const int ButtonId_PresetHue = 300;
    private const int ButtonId_PresetName = 301;
    private const int ButtonId_PresetStr = 302;
    private const int ButtonId_PresetDex = 303;
    private const int ButtonId_PresetInt = 304;
    private const int ButtonId_PresetHits = 305;

    public override bool Singleton => true;

    public PropertyBuilderGump(ModernSpawner spawner, ModernSpawnerEntry entry)
        : this(spawner, entry, ParseExistingProperties(entry.Properties))
    {
    }

    private PropertyBuilderGump(ModernSpawner spawner, ModernSpawnerEntry entry, List<PropertyPart> properties) : base(50, 50)
    {
        _spawner = spawner;
        _entry = entry;
        _properties = properties;
    }

    private static List<PropertyPart> ParseExistingProperties(string properties)
    {
        var result = new List<PropertyPart>();

        if (string.IsNullOrWhiteSpace(properties))
        {
            return result;
        }

        var span = properties.AsSpan();
        foreach (var pairRange in span.Split('/'))
        {
            var pair = span[pairRange].Trim();
            if (pair.IsEmpty)
            {
                continue;
            }

            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var propName = pair[..equalsIndex].Trim().ToString();
            var propValue = pair[(equalsIndex + 1)..].Trim();

            // Check if it's a range value {min-max}
            if (propValue.Length > 2 && propValue[0] == '{' && propValue[^1] == '}')
            {
                var rangeValue = propValue[1..^1];
                var dashIndex = rangeValue.IndexOf('-');
                if (dashIndex > 0 &&
                    int.TryParse(rangeValue[..dashIndex], out var min) &&
                    int.TryParse(rangeValue[(dashIndex + 1)..], out var max))
                {
                    result.Add(new PropertyPart(propName, true, null, min, max));
                    continue;
                }
            }

            result.Add(new PropertyPart(propName, false, propValue.ToString(), 0, 0));
        }

        return result;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        // Background
        builder.AddBackground(0, 0, Width, Height, 5054);
        builder.AddAlphaRegion(0, 0, Width, Height);

        // Title
        builder.AddHtml(0, 10, Width, 20, "Property Builder", "#FFEA00", align: TextAlignment.Center);

        var y = 40;

        // Info text
        builder.AddHtml(20, y, Width - 40, 30,
            "Build property assignments visually. Toggle between fixed values and random ranges.",
            "#C0C0C0");
        y += 40;

        // --- Preview Section ---
        builder.AddHtml(20, y, 200, 20, "Generated Properties", "#00BFFF");
        y += 22;
        var sb = ValueStringBuilder.Create();
        BuildPropertiesString(ref sb);
        builder.AddImageTiled(20, y, Width - 40, 30, 0xA40);
        builder.AddImageTiled(21, y + 1, Width - 42, 28, 0xE14);
        builder.AddHtml(24, y + 4, Width - 48, 24, sb.Length == 0 ? "(empty)" : sb.AsSpan(), "#00FF00");
        y += 40;
        sb.Dispose();

        // --- Properties List ---
        builder.AddHtml(20, y, 200, 20, "Properties", "#00BFFF");
        y += 25;

        // Header
        builder.AddLabel(55, y, 0x384, "Name");
        builder.AddLabel(160, y, 0x384, "Type");
        builder.AddLabel(250, y, 0x384, "Value / Min");
        builder.AddLabel(360, y, 0x384, "Max");
        y += 22;

        for (var i = 0; i < _properties.Count && i < PropertiesPerPage; i++)
        {
            var prop = _properties[i];

            // Delete button
            builder.AddButton(20, y, 0xFA2, 0xFA4, ButtonId_DeleteBase + i);

            // Property Name
            builder.AddImageTiled(55, y - 2, 90, 22, 0xA40);
            builder.AddImageTiled(56, y - 1, 88, 20, 0xBBC);
            builder.AddTextEntry(59, y - 1, 83, 20, 0, TextId_PropNameBase + i, prop.Name);

            // Type toggle (Fixed/Random)
            builder.AddButton(155, y - 2, prop.IsRandom ? 0xD3 : 0xD2, prop.IsRandom ? 0xD2 : 0xD3, ButtonId_ToggleTypeBase + i);
            builder.AddLabel(180, y, 0x384, prop.IsRandom ? "Rnd" : "Fix");

            if (prop.IsRandom)
            {
                // Min value
                builder.AddImageTiled(220, y - 2, 60, 22, 0xA40);
                builder.AddImageTiled(221, y - 1, 58, 20, 0xBBC);
                builder.AddTextEntry(224, y - 1, 53, 20, 0, TextId_PropMinBase + i, $"{prop.Min}");

                // Max value
                builder.AddImageTiled(290, y - 2, 60, 22, 0xA40);
                builder.AddImageTiled(291, y - 1, 58, 20, 0xBBC);
                builder.AddTextEntry(294, y - 1, 53, 20, 0, TextId_PropMaxBase + i, $"{prop.Max}");
            }
            else
            {
                // Fixed value
                builder.AddImageTiled(220, y - 2, 130, 22, 0xA40);
                builder.AddImageTiled(221, y - 1, 128, 20, 0xBBC);
                builder.AddTextEntry(224, y - 1, 123, 20, 0, TextId_PropValueBase + i, prop.Value ?? "");
            }

            y += 28;
        }

        // Add Property button
        y += 5;
        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_AddProperty);
        builder.AddLabel(55, y + 3, 0x384, "Add Property");
        y += 35;

        // --- Quick Presets ---
        builder.AddHtml(20, y, 200, 20, "Quick Presets", "#00BFFF");
        y += 25;

        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_PresetHue);
        builder.AddLabel(55, y + 3, 0x384, "Hue");

        builder.AddButton(100, y, 0xFA5, 0xFA7, ButtonId_PresetName);
        builder.AddLabel(135, y + 3, 0x384, "Name");

        builder.AddButton(190, y, 0xFA5, 0xFA7, ButtonId_PresetStr);
        builder.AddLabel(225, y + 3, 0x384, "Str");

        builder.AddButton(270, y, 0xFA5, 0xFA7, ButtonId_PresetDex);
        builder.AddLabel(305, y + 3, 0x384, "Dex");

        builder.AddButton(350, y, 0xFA5, 0xFA7, ButtonId_PresetInt);
        builder.AddLabel(385, y + 3, 0x384, "Int");

        builder.AddButton(420, y, 0xFA5, 0xFA7, ButtonId_PresetHits);
        builder.AddLabel(455, y + 3, 0x384, "Hits");

        // --- Footer Buttons ---
        builder.AddButton(20, Height - 35, 0xFAE, 0xFAF, ButtonId_Back);
        builder.AddLabel(55, Height - 32, 0x384, "Back");

        builder.AddButton(Width - 160, Height - 35, 0xFB7, 0xFB9, ButtonId_Apply);
        builder.AddLabel(Width - 125, Height - 32, 0x384, "Apply");

        builder.AddButton(Width - 80, Height - 35, 0xFB1, 0xFB3, ButtonId_Cancel);
        builder.AddLabel(Width - 45, Height - 32, 0x384, "Cancel");
    }

    private void BuildPropertiesString(scoped ref ValueStringBuilder sb)
    {
        if (_properties.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _properties.Count; i++)
        {
            var prop = _properties[i];
            if (string.IsNullOrEmpty(prop.Name))
            {
                continue;
            }

            if (i > 0)
            {
                sb.Append('/');
            }

            if (prop.IsRandom)
            {
                sb.Append(prop.Name);
                sb.Append("={");
                sb.Append(prop.Min);
                sb.Append('-');
                sb.Append(prop.Max);
                sb.Append('}');
            }
            else
            {
                sb.Append(prop.Name);
                sb.Append('=');
                sb.Append(prop.Value);
            }
        }
    }

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        if (_spawner.Deleted)
        {
            return;
        }

        var from = state.Mobile;

        // Read current property values from text entries
        ReadPropertyValues(info);

        switch (info.ButtonID)
        {
            case ButtonId_Cancel:
                {
                    from.SendGump(new SpawnerEntryWizardGump(_spawner, _entry));
                    return;
                }

            case ButtonId_Apply:
                {
                    var sb = ValueStringBuilder.Create();
                    BuildPropertiesString(ref sb);
                    _entry.Properties = sb.ToString();
                    sb.Dispose();

                    from.SendMessage("Properties applied.");
                    from.SendGump(new SpawnerEntryWizardGump(_spawner, _entry));
                    return;
                }

            case ButtonId_Back:
                {
                    from.SendGump(new SpawnerEntryWizardGump(_spawner, _entry));
                    return;
                }

            case ButtonId_AddProperty:
                {
                    _properties.Add(new PropertyPart("", false, "", 0, 0));
                    break;
                }

            case ButtonId_PresetHue:
                {
                    _properties.Add(new PropertyPart("Hue", false, "0x8000", 0, 0));
                    break;
                }

            case ButtonId_PresetName:
                {
                    _properties.Add(new PropertyPart("Name", false, "Custom Name", 0, 0));
                    break;
                }

            case ButtonId_PresetStr:
                {
                    _properties.Add(new PropertyPart("Str", true, null, 80, 120));
                    break;
                }

            case ButtonId_PresetDex:
                {
                    _properties.Add(new PropertyPart("Dex", true, null, 80, 120));
                    break;
                }

            case ButtonId_PresetInt:
                {
                    _properties.Add(new PropertyPart("Int", true, null, 80, 120));
                    break;
                }

            case ButtonId_PresetHits:
                {
                    _properties.Add(new PropertyPart("Hits", true, null, 100, 200));
                    break;
                }

            default:
                {
                    // Check for delete button
                    if (info.ButtonID is >= ButtonId_DeleteBase and < ButtonId_ToggleTypeBase)
                    {
                        var deleteIndex = info.ButtonID - ButtonId_DeleteBase;
                        if (deleteIndex >= 0 && deleteIndex < _properties.Count)
                        {
                            _properties.RemoveAt(deleteIndex);
                        }
                    }
                    // Check for toggle type button
                    else if (info.ButtonID is >= ButtonId_ToggleTypeBase and < ButtonId_PresetHue)
                    {
                        var toggleIndex = info.ButtonID - ButtonId_ToggleTypeBase;
                        if (toggleIndex >= 0 && toggleIndex < _properties.Count)
                        {
                            var prop = _properties[toggleIndex];
                            _properties[toggleIndex] = new PropertyPart(
                                prop.Name,
                                !prop.IsRandom,
                                prop.Value,
                                prop.Min,
                                prop.Max
                            );
                        }
                    }
                    break;
                }
        }

        from.SendGump(new PropertyBuilderGump(_spawner, _entry, _properties));
    }

    private void ReadPropertyValues(RelayInfo info)
    {
        for (var i = 0; i < _properties.Count && i < PropertiesPerPage; i++)
        {
            var prop = _properties[i];

            // Read name
            var nameEntry = info.GetTextEntry(TextId_PropNameBase + i);
            var name = nameEntry?.Trim() ?? prop.Name;

            if (prop.IsRandom)
            {
                // Read min/max
                var minEntry = info.GetTextEntry(TextId_PropMinBase + i);
                var maxEntry = info.GetTextEntry(TextId_PropMaxBase + i);
                var min = prop.Min;
                var max = prop.Max;

                if (minEntry != null && int.TryParse(minEntry.Trim(), out var parsedMin))
                {
                    min = parsedMin;
                }
                if (maxEntry != null && int.TryParse(maxEntry.Trim(), out var parsedMax))
                {
                    max = parsedMax;
                }

                _properties[i] = new PropertyPart(name, true, null, min, max);
            }
            else
            {
                // Read value
                var valueEntry = info.GetTextEntry(TextId_PropValueBase + i);
                var value = valueEntry?.Trim() ?? prop.Value;

                _properties[i] = new PropertyPart(name, false, value, 0, 0);
            }
        }
    }

    private class PropertyPart
    {
        public string Name { get; }
        public bool IsRandom { get; }
        public string Value { get; }
        public int Min { get; }
        public int Max { get; }

        public PropertyPart(string name, bool isRandom, string value, int min, int max)
        {
            Name = name;
            IsRandom = isRandom;
            Value = value;
            Min = min;
            Max = max;
        }
    }
}
