using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// Unified file-based export/import system for ModernSpawner configurations.
/// Handles both JSON (spawner configs) and YAML (complex scripts) formats.
/// </summary>
public static class SpawnerFileManager
{
    /// <summary>
    /// Default export directory for spawner files.
    /// </summary>
    public static string ExportDirectory { get; set; } = Path.Combine("Exports", "Spawners");

    /// <summary>
    /// Exports a spawner to a file, detecting format from extension.
    /// </summary>
    /// <param name="spawner">The spawner to export.</param>
    /// <param name="fileName">The file name (with extension). If no extension, .json is used.</param>
    /// <returns>The full path to the exported file.</returns>
    public static string Export(ModernSpawner spawner, string fileName = null)
    {
        ArgumentNullException.ThrowIfNull(spawner);

        // Generate filename if not provided
        fileName ??= GenerateFileName(spawner);

        // Ensure extension
        if (!Path.HasExtension(fileName))
        {
            fileName += ".json";
        }

        // Ensure directory exists
        EnsureExportDirectory();

        var filePath = Path.Combine(ExportDirectory, fileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        switch (extension)
        {
            case ".yaml":
            case ".yml":
                {
                    var scriptData = ScriptYamlSerializer.FromSpawner(spawner);
                    ScriptYamlSerializer.ToFile(scriptData, filePath);
                    break;
                }

            case ".json":
            default:
                {
                    SpawnerJsonExporter.ToFile(spawner, filePath);
                    break;
                }
        }

        return filePath;
    }

    /// <summary>
    /// Exports multiple spawners to a JSON array file.
    /// </summary>
    public static string ExportMultiple(IEnumerable<ModernSpawner> spawners, string fileName)
    {
        ArgumentNullException.ThrowIfNull(spawners);

        EnsureExportDirectory();

        if (!Path.HasExtension(fileName))
        {
            fileName += ".json";
        }

        var filePath = Path.Combine(ExportDirectory, fileName);
        SpawnerJsonExporter.ToFile(spawners, filePath);

        return filePath;
    }

    /// <summary>
    /// Imports spawner configuration from a file and creates a new spawner.
    /// </summary>
    /// <param name="filePath">Path to the file to import.</param>
    /// <param name="map">Map to place the spawner on. If null, uses map from file.</param>
    /// <returns>The created ModernSpawner (not yet added to world).</returns>
    public static ModernSpawner Import(string filePath, Map map = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be empty", nameof(filePath));
        }

        // Handle relative paths
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.Combine(ExportDirectory, filePath);
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Spawner file not found: {filePath}");
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".yaml" or ".yml" => ImportFromYaml(filePath, map),
            _ => ImportFromJson(filePath, map)
        };
    }

    /// <summary>
    /// Imports multiple spawners from a JSON array file.
    /// </summary>
    public static List<ModernSpawner> ImportMultiple(string filePath, Map defaultMap = null)
    {
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.Combine(ExportDirectory, filePath);
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Spawner file not found: {filePath}");
        }

        var exports = SpawnerJsonImporter.FromArrayFile(filePath);
        var spawners = new List<ModernSpawner>();

        foreach (var export in exports)
        {
            spawners.Add(SpawnerJsonImporter.CreateSpawner(export, defaultMap));
        }

        return spawners;
    }

    /// <summary>
    /// Applies configuration from a file to an existing spawner.
    /// </summary>
    public static void ApplyConfiguration(ModernSpawner spawner, string filePath)
    {
        ArgumentNullException.ThrowIfNull(spawner);

        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.Combine(ExportDirectory, filePath);
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        switch (extension)
        {
            case ".yaml":
            case ".yml":
                {
                    var scriptData = ScriptYamlSerializer.FromFile(filePath);
                    ScriptYamlSerializer.ApplyToSpawner(scriptData, spawner);
                    break;
                }

            default:
                {
                    var jsonData = SpawnerJsonImporter.FromFile(filePath);
                    SpawnerJsonImporter.ConfigureSpawner(spawner, jsonData);
                    break;
                }
        }
    }

    /// <summary>
    /// Lists all spawner files in the export directory.
    /// </summary>
    public static IEnumerable<string> ListExportedFiles()
    {
        EnsureExportDirectory();

        return Directory.EnumerateFiles(ExportDirectory, "*.*")
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".json" or ".yaml" or ".yml";
            })
            .Select(Path.GetFileName);
    }

    /// <summary>
    /// Validates a spawner file without importing.
    /// </summary>
    /// <returns>True if valid, false otherwise. Error message in out parameter.</returns>
    public static bool ValidateFile(string filePath, out string error)
    {
        error = null;

        try
        {
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(ExportDirectory, filePath);
            }

            if (!File.Exists(filePath))
            {
                error = $"File not found: {filePath}";
                return false;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            switch (extension)
            {
                case ".yaml":
                case ".yml":
                    {
                        ScriptYamlSerializer.FromFile(filePath);
                        break;
                    }

                default:
                    {
                        SpawnerJsonImporter.FromFile(filePath);
                        break;
                    }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Gets a preview of a spawner file's contents.
    /// </summary>
    public static SpawnerFileInfo GetFileInfo(string filePath)
    {
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.Combine(ExportDirectory, filePath);
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        var info = new FileInfo(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        var fileInfo = new SpawnerFileInfo
        {
            FileName = info.Name,
            FilePath = filePath,
            FileSize = info.Length,
            LastModified = info.LastWriteTime,
            Format = extension is ".yaml" or ".yml" ? "YAML" : "JSON"
        };

        try
        {
            switch (extension)
            {
                case ".yaml":
                case ".yml":
                    {
                        var scriptData = ScriptYamlSerializer.FromFile(filePath);
                        fileInfo.SpawnerName = scriptData.Name;
                        fileInfo.Description = scriptData.Description;
                        fileInfo.EntryCount = scriptData.Entries?.Count ?? 0;
                        break;
                    }

                default:
                    {
                        var content = File.ReadAllText(filePath).TrimStart();
                        if (content.StartsWith("["))
                        {
                            var exports = SpawnerJsonImporter.FromArrayFile(filePath);
                            fileInfo.SpawnerName = $"{exports.Count} spawners";
                            fileInfo.IsMultiple = true;
                            fileInfo.EntryCount = exports.Sum(e => e.Entries?.Count ?? 0);
                        }
                        else
                        {
                            var jsonData = SpawnerJsonImporter.FromFile(filePath);
                            fileInfo.SpawnerName = jsonData.Name;
                            fileInfo.EntryCount = jsonData.Entries?.Count ?? 0;
                            fileInfo.Location = jsonData.Location != null
                                ? $"{jsonData.Location.X},{jsonData.Location.Y},{jsonData.Location.Z} ({jsonData.Location.Map})"
                                : null;
                        }
                        break;
                    }
            }

            fileInfo.IsValid = true;
        }
        catch (Exception ex)
        {
            fileInfo.IsValid = false;
            fileInfo.ValidationError = ex.Message;
        }

        return fileInfo;
    }

    private static ModernSpawner ImportFromJson(string filePath, Map map)
    {
        var data = SpawnerJsonImporter.FromFile(filePath);
        return SpawnerJsonImporter.CreateSpawner(data, map);
    }

    private static ModernSpawner ImportFromYaml(string filePath, Map map)
    {
        var scriptData = ScriptYamlSerializer.FromFile(filePath);

        // Create a new spawner and apply the script configuration
        var spawner = new ModernSpawner();

        if (map != null)
        {
            spawner.MoveToWorld(new Point3D(0, 0, 0), map);
        }

        ScriptYamlSerializer.ApplyToSpawner(scriptData, spawner);

        return spawner;
    }

    private static string GenerateFileName(ModernSpawner spawner)
    {
        var name = spawner.Name ?? "Spawner";

        // Sanitize filename
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        // Add location to make unique
        if (spawner.Map != null && spawner.Map != Map.Internal)
        {
            name = $"{name}_{spawner.X}_{spawner.Y}_{spawner.Map.Name}";
        }

        return name;
    }

    private static void EnsureExportDirectory()
    {
        if (!Directory.Exists(ExportDirectory))
        {
            Directory.CreateDirectory(ExportDirectory);
        }
    }
}

/// <summary>
/// Information about a spawner export file.
/// </summary>
public class SpawnerFileInfo
{
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
    public string Format { get; set; }
    public string SpawnerName { get; set; }
    public string Description { get; set; }
    public string Location { get; set; }
    public int EntryCount { get; set; }
    public bool IsMultiple { get; set; }
    public bool IsValid { get; set; }
    public string ValidationError { get; set; }
}
