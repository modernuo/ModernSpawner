using System;
using System.Linq;
using Server.Collections;
using Server.Engines.Spawners;
using Server.Gumps;
using Server.Network;

namespace Server.Engines.ModernSpawner;

public class ModernSpawnerGump : DynamicGump
{
    private readonly ModernSpawner _spawner;
    private ModernSpawnerEntry _entry;
    private int _page;

    private const int EntriesPerPage = 10;
    private const int ExpandedOffset = 88; // Extra height for expanded entry

    public override bool Singleton => true;

    public ModernSpawnerGump(ModernSpawner spawner, ModernSpawnerEntry focusEntry = null, int page = 0) : base(50, 50)
    {
        _spawner = spawner;
        _entry = focusEntry;
        _page = page;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        var expandedHeight = _entry != null ? ExpandedOffset : 0;
        builder.AddBackground(0, 0, 450, 420 + expandedHeight, 5054);
        builder.AddAlphaRegion(0, 0, 450, 420 + expandedHeight);

        // Header
        builder.AddHtml(5, 1, 200, 20, _spawner.Name ?? "Modern Spawner", "#FFEA00");
        builder.AddHtml(310, 1, 40, 20, "#", "#F4F4F4");
        builder.AddHtml(350, 1, 40, 20, "Max", "#F4F4F4");
        builder.AddHtml(400, 1, 40, 20, "Prb", "#F4F4F4");

        var offset = 0;

        for (var i = 0; i < EntriesPerPage; i++)
        {
            var textIndex = i * 8; // 8 text entries per row
            var entryIndex = _page * EntriesPerPage + i;

            ModernSpawnerEntry entry = null;

            if (entryIndex < _spawner.ModernEntries.Count)
            {
                entry = _spawner.ModernEntries[entryIndex];
            }

            // Expand/Collapse button
            if (entry == null || _entry != entry)
            {
                builder.AddButton(5, 22 * i + 21 + offset, entry != null ? 0xFBA : 0xFA5, entry != null ? 0xFBC : 0xFA7, GetButtonID(2, i * 2));
            }
            else
            {
                builder.AddButton(5, 22 * i + 21 + offset, 0xFBB, 0xFBC, GetButtonID(2, i * 2));
            }

            // Delete button
            builder.AddButton(38, 22 * i + 21 + offset, 0xFA2, 0xFA4, GetButtonID(2, 1 + i * 2));

            // Name text box
            builder.AddImageTiled(71, 22 * i + 20 + offset, 230, 23, 0xA40);
            builder.AddImageTiled(72, 22 * i + 21 + offset, 228, 21, 0xBBC);

            // Count display
            builder.AddImageTiled(305, 22 * i + 20 + offset, 35, 23, 0xA40);
            builder.AddImageTiled(306, 22 * i + 21 + offset, 33, 21, 0xE14);

            // MaxCount text box
            builder.AddImageTiled(345, 22 * i + 20 + offset, 40, 23, 0xA40);
            builder.AddImageTiled(346, 22 * i + 21 + offset, 38, 21, 0xBBC);

            // Probability text box
            builder.AddImageTiled(390, 22 * i + 20 + offset, 40, 23, 0xA40);
            builder.AddImageTiled(391, 22 * i + 21 + offset, 38, 21, 0xBBC);

            string name;
            string probability;
            string maxCount;
            var flags = EntryFlags.None;

            if (entry != null)
            {
                name = entry.SpawnedName;
                probability = entry.SpawnedProbability.ToString();
                maxCount = entry.SpawnedMaxCount.ToString();
                flags = entry.Valid;

                var count = _spawner.CountSpawns(entry);
                var countColor = GetCountColor(count, entry.SpawnedMaxCount);
                builder.AddHtml(305, 22 * i + 20 + offset + 1, 35, 15, $"{count}/", countColor, align: TextAlignment.Right);
            }
            else
            {
                name = "";
                probability = "";
                maxCount = "";
            }

            // Text entries
            builder.AddTextEntry(75, 22 * i + 21 + offset, 225, 21,
                (flags & EntryFlags.InvalidType) != 0 ? 33 : 0, textIndex, name);
            builder.AddTextEntry(348, 22 * i + 21 + offset, 35, 21, 0, textIndex + 1, maxCount);
            builder.AddTextEntry(393, 22 * i + 21 + offset, 35, 21, 0, textIndex + 2, probability);

            // Expanded entry details
            if (entry != null && _entry == entry)
            {
                var y = 22 * i + 42;

                // Row 1: Parameters
                builder.AddLabel(5, y + offset, 0x384, "Params");
                builder.AddImageTiled(60, y + offset, 370, 23, 0xA40);
                builder.AddImageTiled(61, y + 1 + offset, 368, 21, 0xBBC);
                builder.AddTextEntry(64, y + 1 + offset, 363, 21,
                    (flags & EntryFlags.InvalidParams) != 0 ? 33 : 0, textIndex + 3, entry.Parameters ?? "");

                // Row 2: Properties
                builder.AddLabel(5, y + 22 + offset, 0x384, "Props");
                builder.AddImageTiled(60, y + 22 + offset, 370, 23, 0xA40);
                builder.AddImageTiled(61, y + 23 + offset, 368, 21, 0xBBC);
                builder.AddTextEntry(64, y + 23 + offset, 363, 21,
                    (flags & EntryFlags.InvalidProps) != 0 ? 33 : 0, textIndex + 4, entry.Properties ?? "");

                // Row 3: OnSpawn Script
                builder.AddLabel(5, y + 44 + offset, 0x35E, "OnSpawn");
                builder.AddImageTiled(60, y + 44 + offset, 370, 23, 0xA40);
                builder.AddImageTiled(61, y + 45 + offset, 368, 21, 0xBBC);
                builder.AddTextEntry(64, y + 45 + offset, 363, 21, 0, textIndex + 5, entry.OnSpawnScript ?? "");

                // Row 4: Positioning Rule & Spawn Group + Wizard button
                builder.AddLabel(5, y + 66 + offset, 0x35E, "PosRule");
                builder.AddImageTiled(60, y + 66 + offset, 120, 23, 0xA40);
                builder.AddImageTiled(61, y + 67 + offset, 118, 21, 0xBBC);
                builder.AddTextEntry(64, y + 67 + offset, 113, 21, 0, textIndex + 6, entry.PositioningRule ?? "");

                builder.AddLabel(190, y + 66 + offset, 0x35E, "Group");
                builder.AddImageTiled(230, y + 66 + offset, 110, 23, 0xA40);
                builder.AddImageTiled(231, y + 67 + offset, 108, 21, 0xBBC);
                builder.AddTextEntry(234, y + 67 + offset, 103, 21, 0, textIndex + 7, entry.SpawnGroup ?? "");

                // Entry Wizard button
                builder.AddButton(355, y + 66 + offset, 0xFAB, 0xFAD, GetButtonID(3, i));
                builder.AddLabel(390, y + 67 + offset, 0x55, "Wizard");

                offset += ExpandedOffset;
            }
        }

        // Footer section
        var footerY = 22 * EntriesPerPage + 25 + offset;

        // Running status
        if (_spawner.Running)
        {
            builder.AddButton(5, footerY, 0x2A4E, 0x2A3A, GetButtonID(1, 6));
            builder.AddLabel(38, footerY + 5, 0x384, "On");
        }
        else
        {
            builder.AddButton(5, footerY, 0x2A62, 0x2A3A, GetButtonID(1, 7));
            builder.AddLabel(38, footerY + 5, 0x384, "Off");
        }

        // Totals
        var totalSpawned = 0;
        var totalSpawns = 0;
        var totalWeight = 0;

        foreach (var entry in _spawner.ModernEntries)
        {
            totalSpawns += entry.SpawnedMaxCount;
            totalSpawned += _spawner.CountSpawns(entry);
            totalWeight += entry.SpawnedProbability;
        }

        builder.AddHtml(350, footerY, 40, 20, $"{totalSpawns}", "#F4F4F4", align: TextAlignment.Center);
        builder.AddHtml(400, footerY, 40, 20, $"{totalWeight}", "#F4F4F4", align: TextAlignment.Center);

        // Action buttons
        builder.AddButton(5, footerY + 35, 0xFAB, 0xFAD, GetButtonID(1, 2));
        builder.AddLabel(38, footerY + 35, 0x384, "Props");

        builder.AddButton(5, footerY + 57, 0xFAE, 0xFAF, GetButtonID(1, 8));
        builder.AddLabel(38, footerY + 57, 0x384, "Goto");

        builder.AddButton(110, footerY + 13, 0xFA2, 0xFA3, GetButtonID(1, 9));
        builder.AddLabel(143, footerY + 13, 0x384, "Reset");

        builder.AddButton(110, footerY + 35, 0xFB4, 0xFB6, GetButtonID(1, 3));
        builder.AddLabel(143, footerY + 35, 0x384, "Bring Home");

        builder.AddButton(110, footerY + 57, 0xFA8, 0xFAA, GetButtonID(1, 4));
        builder.AddLabel(143, footerY + 57, 0x384, "Total Respawn");

        builder.AddButton(280, footerY + 13, 0xFAB, 0xFAD, GetButtonID(1, 10));
        builder.AddLabel(313, footerY + 13, 0x55, "Settings");

        builder.AddButton(280, footerY + 35, 0xFB7, 0xFB9, GetButtonID(1, 5));
        builder.AddLabel(313, footerY + 35, 0x384, "Save");

        builder.AddButton(280, footerY + 57, 0xFB1, 0xFB3, 0);
        builder.AddLabel(313, footerY + 57, 0x384, "Cancel");

        // Pagination
        if (_page > 0)
        {
            builder.AddButton(230, footerY, 0x15E3, 0x15E7, GetButtonID(1, 0));
        }
        else
        {
            builder.AddImage(230, footerY, 0x25EA);
        }

        if ((_page + 1) * EntriesPerPage <= _spawner.ModernEntries.Count)
        {
            builder.AddButton(250, footerY, 0x15E1, 0x15E5, GetButtonID(1, 1));
        }
        else
        {
            builder.AddImage(250, footerY, 0x25E6);
        }
    }

    public int GetButtonID(int type, int index) => 1 + index * 10 + type;

    private static ReadOnlySpan<char> GetCountColor(int count, int maxCount) =>
        ((double)count / maxCount) switch
        {
            <= 0.25 => "#DE3163",
            <= 0.50 => "#FF7F50",
            <= 0.75 => "#DFFF00",
            <= 1 => "#00FF00",
            _ => "#F4F4F4"
        };

    public void CreateArray(RelayInfo info, Mobile from)
    {
        var ocount = _spawner.ModernEntries.Count;

        using var queue = PooledRefQueue<ModernSpawnerEntry>.Create();

        for (var i = 0; i < EntriesPerPage; i++)
        {
            var index = i * 8;
            var entryIndex = _page * EntriesPerPage + i;

            var nameEntry = info.GetTextEntry(index);
            var maxCountEntry = info.GetTextEntry(index + 1);
            var probEntry = info.GetTextEntry(index + 2);
            var paramsEntry = info.GetTextEntry(index + 3);
            var propsEntry = info.GetTextEntry(index + 4);
            var onSpawnEntry = info.GetTextEntry(index + 5);
            var posRuleEntry = info.GetTextEntry(index + 6);
            var groupEntry = info.GetTextEntry(index + 7);

            if (nameEntry == null)
            {
                continue;
            }

            var str = nameEntry.Trim().ToLower();

            if (str.Length > 0)
            {
                var type = AssemblyHandler.FindTypeByName(str);

                if (type == null)
                {
                    from.SendMessage($"{str} is not a valid type name for entry #{i}.");
                    return;
                }

                ModernSpawnerEntry entry;
                if (entryIndex < ocount)
                {
                    entry = _spawner.ModernEntries[entryIndex];
                    entry.SpawnedName = str;

                    if (maxCountEntry != null)
                    {
                        entry.SpawnedMaxCount = Utility.ToInt32(maxCountEntry.Trim());
                    }

                    if (probEntry != null)
                    {
                        entry.SpawnedProbability = Utility.ToInt32(probEntry.Trim());
                    }
                }
                else
                {
                    var maxCount = 1;
                    var prob = 100;

                    if (maxCountEntry != null)
                    {
                        maxCount = Utility.ToInt32(maxCountEntry.Trim());
                    }

                    if (probEntry != null)
                    {
                        prob = Utility.ToInt32(probEntry.Trim());
                    }

                    entry = (ModernSpawnerEntry)_spawner.AddEntry(str, prob, maxCount);
                }

                if (paramsEntry != null)
                {
                    entry.Parameters = paramsEntry.Trim();
                }

                if (propsEntry != null)
                {
                    entry.Properties = propsEntry.Trim();
                }

                if (onSpawnEntry != null)
                {
                    entry.OnSpawnScript = onSpawnEntry.Trim();
                }

                if (posRuleEntry != null)
                {
                    entry.PositioningRule = posRuleEntry.Trim();
                }

                if (groupEntry != null)
                {
                    entry.SpawnGroup = groupEntry.Trim();
                }
            }
            else if (entryIndex < ocount && _spawner.ModernEntries[entryIndex] != null)
            {
                queue.Enqueue(_spawner.ModernEntries[entryIndex]);
            }
        }

        while (queue.Count > 0)
        {
            _spawner.RemoveEntry(queue.Dequeue());
        }

        if (ocount == 0 && _spawner.ModernEntries.Count > 0)
        {
            _spawner.Start();
        }
    }

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        if (_spawner.Deleted)
        {
            return;
        }

        var val = info.ButtonID - 1;

        if (val < 0)
        {
            return;
        }

        var type = val % 10;
        var index = val / 10;

        switch (type)
        {
            case 0: // Cancel
                {
                    return;
                }

            case 1:
                {
                    switch (index)
                    {
                        case 0: // Previous page
                            {
                                if (_page > 0)
                                {
                                    _page--;
                                    _entry = null;
                                }
                                break;
                            }

                        case 1: // Next page
                            {
                                if ((_page + 1) * EntriesPerPage <= _spawner.ModernEntries.Count)
                                {
                                    _page++;
                                    _entry = null;
                                }
                                break;
                            }

                        case 2: // Props
                            {
                                state.Mobile.SendGump(new PropertiesGump(state.Mobile, _spawner));
                                break;
                            }

                        case 3: // Bring Home
                            {
                                _spawner.BringToHome();
                                break;
                            }

                        case 4: // Total Respawn
                            {
                                _spawner.Respawn();
                                break;
                            }

                        case 5: // Save
                            {
                                CreateArray(info, state.Mobile);
                                break;
                            }

                        case 6: // On button (turn off)
                            {
                                _spawner.Running = false;
                                break;
                            }

                        case 7: // Off button (turn on)
                            {
                                _spawner.Running = true;
                                break;
                            }

                        case 8: // Goto
                            {
                                state.Mobile.MoveToWorld(_spawner.Location, _spawner.Map);
                                break;
                            }

                        case 9: // Reset
                            {
                                _spawner.Reset();
                                break;
                            }

                        case 10: // Settings
                            {
                                CreateArray(info, state.Mobile);
                                state.Mobile.SendGump(new SpawnerSettingsGump(_spawner));
                                return;
                            }
                    }
                    break;
                }

            case 2:
                {
                    var entryIndex = index / 2 + _page * EntriesPerPage;
                    var buttonType = index % 2;

                    if (entryIndex >= 0 && entryIndex < _spawner.ModernEntries.Count)
                    {
                        var entry = _spawner.ModernEntries[entryIndex];
                        if (buttonType == 0) // Expand/Collapse
                        {
                            _entry = _entry != entry ? entry : null;
                        }
                        else // Delete spawns for this entry
                        {
                            _spawner.RemoveSpawn(entryIndex);
                        }
                    }

                    CreateArray(info, state.Mobile);
                    break;
                }

            case 3: // Entry Wizard button
                {
                    var entryIndex = index + _page * EntriesPerPage;
                    if (entryIndex >= 0 && entryIndex < _spawner.ModernEntries.Count)
                    {
                        var entry = _spawner.ModernEntries[entryIndex];
                        if (entry != null)
                        {
                            CreateArray(info, state.Mobile);
                            state.Mobile.SendGump(new SpawnerEntryWizardGump(_spawner, entry));
                            return;
                        }
                    }
                    break;
                }
        }

        if (_entry != null && _spawner.ModernEntries.Contains(_entry))
        {
            state.Mobile.SendGump(new ModernSpawnerGump(_spawner, _entry, _page));
        }
        else
        {
            state.Mobile.SendGump(new ModernSpawnerGump(_spawner, null, _page));
        }
    }
}
