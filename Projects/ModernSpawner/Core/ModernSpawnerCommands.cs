using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Server.Engines.ModernSpawner.Serialization;
using Server.Logging;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// In-game commands for ModernSpawner management.
/// </summary>
public static class ModernSpawnerCommands
{
    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(ModernSpawnerCommands));

    public static void Configure()
    {
        CommandSystem.Register("ImportModernSpawners", AccessLevel.Developer, ImportModernSpawners_OnCommand);
        CommandSystem.Register("ImportXmlAsModern", AccessLevel.Developer, ImportXmlAsModern_OnCommand);
        CommandSystem.Register("ExportModernSpawner", AccessLevel.Developer, ExportModernSpawner_OnCommand);
    }

    [Usage("ImportModernSpawners <file pattern>")]
    [Aliases("ImportModernSpawner")]
    [Description("Imports ModernSpawner JSON files. Supports glob patterns (e.g., Data/Spawns/**/*.json).")]
    private static void ImportModernSpawners_OnCommand(CommandEventArgs e)
    {
        if (e.Arguments.Length == 0)
        {
            e.Mobile.SendMessage("Usage: [ImportModernSpawners <file pattern>");
            e.Mobile.SendMessage("Example: [ImportModernSpawners Data/Spawns/**/*.json");
            return;
        }

        var pattern = e.Arguments[0];
        var files = FindFiles(pattern);

        if (files.Count == 0)
        {
            e.Mobile.SendMessage($"No files found matching: {pattern}");
            return;
        }

        var watch = Stopwatch.StartNew();
        var totalImported = 0;
        var totalFailed = 0;

        foreach (var file in files)
        {
            e.Mobile.SendMessage($"Importing {file.Name}...");

            try
            {
                var spawners = SpawnerJsonImporter.FromArrayFile(file.FullName);
                foreach (var data in spawners)
                {
                    try
                    {
                        var spawner = SpawnerJsonImporter.CreateSpawner(data);
                        spawner.Respawn();
                        totalImported++;
                    }
                    catch (Exception ex)
                    {
                        totalFailed++;
                        Logger.Error(ex, "Failed to import spawner {Name}", data.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                e.Mobile.SendMessage($"Error reading {file.Name}: {ex.Message}");
                Logger.Error(ex, "Failed to read file {File}", file.FullName);
            }
        }

        watch.Stop();
        e.Mobile.SendMessage(
            $"Imported {totalImported} spawners from {files.Count} files ({watch.Elapsed.TotalSeconds:F2}s, {totalFailed} failures)");
        Logger.Information(
            "Imported {Count} ModernSpawners from {FileCount} files in {Duration:F2}s",
            totalImported, files.Count, watch.Elapsed.TotalSeconds);
    }

    [Usage("ImportXmlAsModern <file pattern>")]
    [Aliases("XmlToModern")]
    [Description("Imports XmlSpawner XML files as ModernSpawners. Supports both ServUO and Sno's export formats.")]
    private static void ImportXmlAsModern_OnCommand(CommandEventArgs e)
    {
        if (e.Arguments.Length == 0)
        {
            e.Mobile.SendMessage("Usage: [ImportXmlAsModern <file pattern>");
            e.Mobile.SendMessage("Example: [ImportXmlAsModern Spawns/*.xml");
            e.Mobile.SendMessage("Supports ServUO XmlSpawner format and Sno's export format.");
            return;
        }

        var pattern = e.Arguments[0];
        var files = FindFiles(pattern);

        if (files.Count == 0)
        {
            // Try as absolute path
            if (File.Exists(pattern))
            {
                files.Add(new FileInfo(pattern));
            }
            else
            {
                e.Mobile.SendMessage($"No files found matching: {pattern}");
                return;
            }
        }

        var watch = Stopwatch.StartNew();
        var totalImported = 0;
        var totalFailed = 0;
        var totalErrors = new List<string>();

        foreach (var file in files)
        {
            e.Mobile.SendMessage($"Importing {file.Name}...");

            var result = XmlSpawnerImporter.ImportFromFile(file.FullName, respawn: true);
            totalImported += result.Imported;
            totalFailed += result.Failed;

            if (result.Errors.Count > 0)
            {
                foreach (var error in result.Errors)
                {
                    totalErrors.Add($"{file.Name}: {error}");
                    if (totalErrors.Count <= 10)
                    {
                        e.Mobile.SendMessage(33, error);
                    }
                }
            }

            e.Mobile.SendMessage($"  {result.Imported} imported, {result.Failed} failed");
        }

        watch.Stop();

        e.Mobile.SendMessage(
            $"Total: {totalImported} spawners from {files.Count} files ({watch.Elapsed.TotalSeconds:F2}s, {totalFailed} failures)");

        if (totalErrors.Count > 10)
        {
            e.Mobile.SendMessage(33, $"... and {totalErrors.Count - 10} more errors (see log)");
        }

        Logger.Information(
            "Imported {Count} XmlSpawners as ModernSpawners from {FileCount} files in {Duration:F2}s ({Failures} failures)",
            totalImported, files.Count, watch.Elapsed.TotalSeconds, totalFailed);

        foreach (var error in totalErrors)
        {
            Logger.Warning("Import error: {Error}", error);
        }
    }

    [Usage("ExportModernSpawner <filename>")]
    [Description("Exports the targeted ModernSpawner to a JSON file.")]
    private static void ExportModernSpawner_OnCommand(CommandEventArgs e)
    {
        if (e.Arguments.Length == 0)
        {
            e.Mobile.SendMessage("Usage: [ExportModernSpawner <filename>");
            return;
        }

        e.Mobile.SendMessage("Target the ModernSpawner to export.");
        e.Mobile.Target = new ExportSpawnerTarget(e.Arguments[0]);
    }

    private static List<FileInfo> FindFiles(string pattern)
    {
        var files = new List<FileInfo>();
        var baseDir = new DirectoryInfo(Core.BaseDirectory);

        try
        {
            var matches = new Matcher()
                .AddInclude(pattern)
                .Execute(new DirectoryInfoWrapper(baseDir))
                .Files;

            foreach (var match in matches)
            {
                files.Add(new FileInfo(Path.Combine(Core.BaseDirectory, match.Path)));
            }
        }
        catch
        {
            // Fall back to direct path
            var fullPath = Path.Combine(Core.BaseDirectory, pattern);
            if (File.Exists(fullPath))
            {
                files.Add(new FileInfo(fullPath));
            }
        }

        return files;
    }

    private class ExportSpawnerTarget : Server.Targeting.Target
    {
        private readonly string _filename;

        public ExportSpawnerTarget(string filename) : base(30, false, Server.Targeting.TargetFlags.None)
        {
            _filename = filename;
        }

        protected override void OnTarget(Mobile from, object targeted)
        {
            if (targeted is not ModernSpawner spawner)
            {
                from.SendMessage("That is not a ModernSpawner.");
                return;
            }

            try
            {
                var dir = Path.Combine(Core.BaseDirectory, "Exports");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var filePath = Path.Combine(dir, _filename);
                if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    filePath += ".json";
                }

                SpawnerJsonExporter.ToFile(spawner, filePath);
                from.SendMessage($"Exported to {filePath}");
            }
            catch (Exception ex)
            {
                from.SendMessage($"Export failed: {ex.Message}");
                Logger.Error(ex, "Failed to export spawner");
            }
        }
    }
}
