using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CiyuanSha.Gameplay.Characters;
using Godot;

namespace CiyuanSha.Gameplay.Generals;

/// <summary>
/// Resolves general ids to actual image files in the art folder.
/// </summary>
public static class GeneralArtRegistry
{
    private const string MappingResourcePath = "res://Data/general_art_mapping.json";

    private static bool _loaded;
    private static Dictionary<string, string> _mappings = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> Mappings
    {
        get
        {
            EnsureLoaded();
            return _mappings;
        }
    }

    public static string ResolveFileName(string generalId, string fallbackFileName = "")
    {
        EnsureLoaded();

        if (!string.IsNullOrWhiteSpace(generalId) && _mappings.TryGetValue(generalId.Trim(), out string? mappedFile))
        {
            return mappedFile;
        }

        return fallbackFileName ?? string.Empty;
    }

    public static string GetExplicitMapping(string generalId)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(generalId))
        {
            return string.Empty;
        }

        return _mappings.TryGetValue(generalId.Trim(), out string? mappedFile)
            ? mappedFile
            : string.Empty;
    }

    public static bool HasExplicitMapping(string generalId)
    {
        return !string.IsNullOrWhiteSpace(GetExplicitMapping(generalId));
    }

    public static string ResolveImagePath(string generalId, string fallbackFileName = "", string? baseDirectory = null)
    {
        string fileName = ResolveFileName(generalId, fallbackFileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        string directory = string.IsNullOrWhiteSpace(baseDirectory)
            ? PlayerCharacter.DefaultGeneralCardDirectory
            : baseDirectory;

        return Path.Combine(directory, fileName);
    }

    public static void SetMapping(string generalId, string fileName)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(generalId))
        {
            return;
        }

        string normalizedId = generalId.Trim();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            _mappings.Remove(normalizedId);
        }
        else
        {
            _mappings[normalizedId] = fileName.Trim();
        }

        Save();
    }

    public static void Reload()
    {
        _loaded = false;
        _mappings.Clear();
        EnsureLoaded();
    }

    public static string[] ListAvailableImageFiles(string? baseDirectory = null)
    {
        string directory = string.IsNullOrWhiteSpace(baseDirectory)
            ? PlayerCharacter.DefaultGeneralCardDirectory
            : baseDirectory;

        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(directory)
            .Where(IsSupportedImage)
            .Select(Path.GetFileName)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        string absolutePath = ProjectSettings.GlobalizePath(MappingResourcePath);
        if (!File.Exists(absolutePath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(absolutePath);
            GeneralArtMappingData? data = JsonSerializer.Deserialize<GeneralArtMappingData>(json);
            _mappings = data?.Mappings is not null
                ? new Dictionary<string, string>(data.Mappings, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to load general art mapping: {ex.Message}");
            _mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save()
    {
        try
        {
            string absolutePath = ProjectSettings.GlobalizePath(MappingResourcePath);
            string? directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            GeneralArtMappingData data = new()
            {
                Mappings = new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase)
            };

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(absolutePath, json);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to save general art mapping: {ex.Message}");
        }
    }

    private static bool IsSupportedImage(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp";
    }
}
