using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.Engines.ModernSpawner.Serialization;
using Server.Gumps;
using Server.Network;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Wizard-style gump for exporting spawner configurations to files.
/// Supports JSON and YAML formats with various export options.
/// </summary>
public class SpawnerExportGump : DynamicGump
{
    private readonly ModernSpawner _spawner;
    private bool _useYaml;
    private string _lastExportPath;

    // Layout constants
    private const int Width = 450;
    private const int Height = 400;

    // Text entry IDs
    private const int TextId_FileName = 0;

    // Button IDs
    private const int ButtonId_Cancel = 0;
    private const int ButtonId_Export = 1;
    private const int ButtonId_Back = 2;
    private const int ButtonId_ToggleFormat = 3;
    private const int ButtonId_ListFiles = 4;
    private const int ButtonId_Import = 5;
    private const int ButtonId_OpenFolder = 6;

    public override bool Singleton => true;

    public SpawnerExportGump(ModernSpawner spawner, bool useYaml = false, string lastExportPath = null) : base(50, 50)
    {
        _spawner = spawner;
        _useYaml = useYaml;
        _lastExportPath = lastExportPath;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        // Background
        builder.AddBackground(0, 0, Width, Height, 5054);
        builder.AddAlphaRegion(0, 0, Width, Height);

        // Title
        builder.AddHtml(0, 10, Width, 20, "Export / Import Spawner", "#FFEA00", align: TextAlignment.Center);

        var y = 40;

        // Info text
        builder.AddHtml(20, y, Width - 40, 40,
            "Export spawner configuration to a file for backup, sharing, or editing. Import configurations from previously exported files.",
            "#C0C0C0");
        y += 50;

        // --- Export Section ---
        builder.AddHtml(20, y, 200, 20, "Export Configuration", "#00BFFF");
        y += 25;

        // File Name
        builder.AddLabel(20, y, 0x384, "File Name:");
        builder.AddImageTiled(100, y - 2, 250, 22, 0xA40);
        builder.AddImageTiled(101, y - 1, 248, 20, 0xBBC);
        var defaultName = GenerateDefaultFileName();
        builder.AddTextEntry(104, y - 1, 243, 20, 0, TextId_FileName, defaultName);
        y += 28;

        // Format toggle
        builder.AddLabel(20, y, 0x384, "Format:");
        builder.AddButton(80, y - 3, _useYaml ? 0xD2 : 0xD3, _useYaml ? 0xD3 : 0xD2, ButtonId_ToggleFormat);
        builder.AddLabel(105, y, 0x384, "JSON");
        builder.AddButton(160, y - 3, _useYaml ? 0xD3 : 0xD2, _useYaml ? 0xD2 : 0xD3, ButtonId_ToggleFormat);
        builder.AddLabel(185, y, 0x384, "YAML");
        y += 28;

        // Format description
        var formatDesc = _useYaml
            ? "YAML: Best for complex scripts, more readable, supports comments"
            : "JSON: Universal format, works with most tools, strict syntax";
        builder.AddHtml(20, y, Width - 40, 20, formatDesc, "#808080");
        y += 25;

        // Export button
        builder.AddButton(20, y, 0xFA8, 0xFAA, ButtonId_Export);
        builder.AddLabel(55, y + 3, 0x55, "Export to File");
        y += 35;

        // Last export result
        if (!string.IsNullOrEmpty(_lastExportPath))
        {
            builder.AddHtml(20, y, Width - 40, 40, $"Last export: {_lastExportPath}", "#00FF00");
            y += 45;
        }
        else
        {
            y += 15;
        }

        // --- Import Section ---
        builder.AddHtml(20, y, 200, 20, "Import Configuration", "#00BFFF");
        y += 25;

        builder.AddHtml(20, y, Width - 40, 30,
            "Import a previously exported configuration to apply to this spawner. Current configuration will be replaced.",
            "#C0C0C0");
        y += 35;

        builder.AddButton(20, y, 0xFA8, 0xFAA, ButtonId_Import);
        builder.AddLabel(55, y + 3, 0x384, "Import from File...");
        y += 35;

        // --- File Management ---
        builder.AddHtml(20, y, 200, 20, "File Management", "#00BFFF");
        y += 25;

        builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_ListFiles);
        builder.AddLabel(55, y + 3, 0x384, "List Exported Files");

        builder.AddButton(200, y, 0xFA5, 0xFA7, ButtonId_OpenFolder);
        builder.AddLabel(235, y + 3, 0x384, $"Folder: {SpawnerFileManager.ExportDirectory}");

        // --- Footer Buttons ---
        builder.AddButton(20, Height - 35, 0xFAE, 0xFAF, ButtonId_Back);
        builder.AddLabel(55, Height - 32, 0x384, "Back");

        builder.AddButton(Width - 80, Height - 35, 0xFB1, 0xFB3, ButtonId_Cancel);
        builder.AddLabel(Width - 45, Height - 32, 0x384, "Close");
    }

    private string GenerateDefaultFileName()
    {
        var name = _spawner.Name ?? "Spawner";

        // Sanitize filename
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        // Add location for uniqueness
        if (_spawner.Map != null && _spawner.Map != Map.Internal)
        {
            name = $"{name}_{_spawner.X}_{_spawner.Y}";
        }

        return name;
    }

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        if (_spawner.Deleted && info.ButtonID != ButtonId_Cancel)
        {
            return;
        }

        var from = state.Mobile;

        switch (info.ButtonID)
        {
            case ButtonId_Cancel:
                return;

            case ButtonId_Back:
                from.SendGump(new SpawnerSettingsGump(_spawner));
                return;

            case ButtonId_ToggleFormat:
                _useYaml = !_useYaml;
                break;

            case ButtonId_Export:
                {
                    var fileNameEntry = info.GetTextEntry(TextId_FileName);
                    var fileName = fileNameEntry?.Trim() ?? GenerateDefaultFileName();

                    // Add extension if not present
                    if (!Path.HasExtension(fileName))
                    {
                        fileName += _useYaml ? ".yaml" : ".json";
                    }

                    try
                    {
                        var path = SpawnerFileManager.Export(_spawner, fileName);
                        _lastExportPath = path;
                        from.SendMessage($"Spawner exported to: {path}");
                    }
                    catch (Exception ex)
                    {
                        from.SendMessage($"Export failed: {ex.Message}");
                    }
                    break;
                }

            case ButtonId_Import:
                from.SendGump(new SpawnerImportGump(_spawner));
                return;

            case ButtonId_ListFiles:
                ListExportedFiles(from);
                break;

            case ButtonId_OpenFolder:
                from.SendMessage($"Export folder: {Path.GetFullPath(SpawnerFileManager.ExportDirectory)}");
                break;
        }

        from.SendGump(new SpawnerExportGump(_spawner, _useYaml, _lastExportPath));
    }

    private static void ListExportedFiles(Mobile from)
    {
        try
        {
            var files = SpawnerFileManager.ListExportedFiles().ToList();

            if (files.Count == 0)
            {
                from.SendMessage("No exported files found.");
                return;
            }

            from.SendMessage($"Found {files.Count} exported file(s):");
            foreach (var file in files.Take(10))
            {
                var fileInfo = SpawnerFileManager.GetFileInfo(file);
                if (fileInfo != null)
                {
                    from.SendMessage($"  {file} ({fileInfo.Format}, {fileInfo.EntryCount} entries)");
                }
                else
                {
                    from.SendMessage($"  {file}");
                }
            }

            if (files.Count > 10)
            {
                from.SendMessage($"  ... and {files.Count - 10} more");
            }
        }
        catch (Exception ex)
        {
            from.SendMessage($"Error listing files: {ex.Message}");
        }
    }
}

/// <summary>
/// Gump for importing spawner configurations from files.
/// </summary>
public class SpawnerImportGump : DynamicGump
{
    private readonly ModernSpawner _spawner;
    private readonly List<string> _files;
    private readonly int _page;

    private const int Width = 450;
    private const int Height = 400;
    private const int FilesPerPage = 8;

    private const int ButtonId_Cancel = 0;
    private const int ButtonId_Back = 1;
    private const int ButtonId_PrevPage = 2;
    private const int ButtonId_NextPage = 3;
    private const int ButtonId_ImportBase = 100;
    private const int ButtonId_PreviewBase = 200;

    public override bool Singleton => true;

    public SpawnerImportGump(ModernSpawner spawner, int page = 0) : base(50, 50)
    {
        _spawner = spawner;
        _page = page;
        _files = SpawnerFileManager.ListExportedFiles().ToList();
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        // Background
        builder.AddBackground(0, 0, Width, Height, 5054);
        builder.AddAlphaRegion(0, 0, Width, Height);

        // Title
        builder.AddHtml(0, 10, Width, 20, "Import Spawner Configuration", "#FFEA00", align: TextAlignment.Center);

        var y = 40;

        // Warning
        builder.AddHtml(20, y, Width - 40, 40,
            "WARNING: Importing will replace the current spawner configuration. This cannot be undone.",
            "#FF6600");
        y += 50;

        // --- File List ---
        builder.AddHtml(20, y, 200, 20, "Available Files", "#00BFFF");
        y += 25;

        if (_files.Count == 0)
        {
            builder.AddHtml(20, y, Width - 40, 20, "No exported files found.", "#808080");
            y += 25;
        }
        else
        {
            var startIndex = _page * FilesPerPage;
            var endIndex = Math.Min(startIndex + FilesPerPage, _files.Count);

            // Header
            builder.AddLabel(55, y, 0x384, "File");
            builder.AddLabel(250, y, 0x384, "Format");
            builder.AddLabel(320, y, 0x384, "Entries");
            y += 22;

            for (var i = startIndex; i < endIndex; i++)
            {
                var file = _files[i];
                var fileInfo = SpawnerFileManager.GetFileInfo(file);

                // Import button
                builder.AddButton(20, y, 0xFA5, 0xFA7, ButtonId_ImportBase + i);

                // File name
                var displayName = file.Length > 25 ? file[..22] + "..." : file;
                builder.AddLabel(55, y + 3, fileInfo?.IsValid == true ? 0x384 : 33, displayName);

                // Format
                builder.AddLabel(250, y + 3, 0x384, fileInfo?.Format ?? "?");

                // Entry count
                builder.AddLabel(320, y + 3, 0x384, $"{fileInfo?.EntryCount ?? 0}");

                // Preview button
                builder.AddButton(370, y, 0x15E1, 0x15E5, ButtonId_PreviewBase + i);
                builder.AddLabel(388, y + 3, 0x384, "Info");

                y += 25;
            }

            // Pagination
            if (_files.Count > FilesPerPage)
            {
                y += 10;
                if (_page > 0)
                {
                    builder.AddButton(150, y, 0x15E3, 0x15E7, ButtonId_PrevPage);
                }

                builder.AddLabel(180, y + 3, 0x384, $"Page {_page + 1}/{(_files.Count - 1) / FilesPerPage + 1}");

                if (endIndex < _files.Count)
                {
                    builder.AddButton(250, y, 0x15E1, 0x15E5, ButtonId_NextPage);
                }
            }
        }

        // --- Footer Buttons ---
        builder.AddButton(20, Height - 35, 0xFAE, 0xFAF, ButtonId_Back);
        builder.AddLabel(55, Height - 32, 0x384, "Back");

        builder.AddButton(Width - 80, Height - 35, 0xFB1, 0xFB3, ButtonId_Cancel);
        builder.AddLabel(Width - 45, Height - 32, 0x384, "Cancel");
    }

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        if (_spawner.Deleted && info.ButtonID != ButtonId_Cancel)
        {
            return;
        }

        var from = state.Mobile;

        switch (info.ButtonID)
        {
            case ButtonId_Cancel:
                return;

            case ButtonId_Back:
                from.SendGump(new SpawnerExportGump(_spawner));
                return;

            case ButtonId_PrevPage:
                from.SendGump(new SpawnerImportGump(_spawner, Math.Max(0, _page - 1)));
                return;

            case ButtonId_NextPage:
                from.SendGump(new SpawnerImportGump(_spawner, _page + 1));
                return;

            default:
                // Check for import button
                if (info.ButtonID is >= ButtonId_ImportBase and < ButtonId_PreviewBase)
                {
                    var importIndex = info.ButtonID - ButtonId_ImportBase;
                    if (importIndex >= 0 && importIndex < _files.Count)
                    {
                        ImportFile(from, _files[importIndex]);
                        return;
                    }
                }
                // Check for preview button
                else if (info.ButtonID >= ButtonId_PreviewBase)
                {
                    var previewIndex = info.ButtonID - ButtonId_PreviewBase;
                    if (previewIndex >= 0 && previewIndex < _files.Count)
                    {
                        PreviewFile(from, _files[previewIndex]);
                    }
                }
                break;
        }

        from.SendGump(new SpawnerImportGump(_spawner, _page));
    }

    private void ImportFile(Mobile from, string fileName)
    {
        try
        {
            // Validate first
            if (!SpawnerFileManager.ValidateFile(fileName, out var error))
            {
                from.SendMessage($"Invalid file: {error}");
                from.SendGump(new SpawnerImportGump(_spawner, _page));
                return;
            }

            // Apply configuration
            SpawnerFileManager.ApplyConfiguration(_spawner, fileName);
            from.SendMessage($"Configuration imported from: {fileName}");
            from.SendGump(new SpawnerSettingsGump(_spawner));
        }
        catch (Exception ex)
        {
            from.SendMessage($"Import failed: {ex.Message}");
            from.SendGump(new SpawnerImportGump(_spawner, _page));
        }
    }

    private static void PreviewFile(Mobile from, string fileName)
    {
        try
        {
            var fileInfo = SpawnerFileManager.GetFileInfo(fileName);
            if (fileInfo == null)
            {
                from.SendMessage("Could not read file information.");
                return;
            }

            from.SendMessage("--- File Information ---");
            from.SendMessage($"Name: {fileInfo.FileName}");
            from.SendMessage($"Format: {fileInfo.Format}");
            from.SendMessage($"Size: {fileInfo.FileSize} bytes");
            from.SendMessage($"Modified: {fileInfo.LastModified}");

            if (!string.IsNullOrEmpty(fileInfo.SpawnerName))
            {
                from.SendMessage($"Spawner: {fileInfo.SpawnerName}");
            }

            if (!string.IsNullOrEmpty(fileInfo.Location))
            {
                from.SendMessage($"Location: {fileInfo.Location}");
            }

            from.SendMessage($"Entries: {fileInfo.EntryCount}");

            if (!fileInfo.IsValid)
            {
                from.SendMessage($"Validation Error: {fileInfo.ValidationError}");
            }
        }
        catch (Exception ex)
        {
            from.SendMessage($"Error reading file: {ex.Message}");
        }
    }
}
