using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Skills;
using Godot;

namespace CiyuanSha.Gameplay.Generals;

/// <summary>
/// In-memory catalog for currently recognized generals.
/// </summary>
public static class GeneralCatalog
{
    private const string CatalogResourcePath = "res://Data/general_catalog.json";

    private static bool _externalCatalogLoaded;

    private static readonly Dictionary<string, GeneralDefinition> _definitions = new()
    {
        ["xingjianya"] = new GeneralDefinition
        {
            GeneralId = "xingjianya",
            DisplayName = "Xing Jianya",
            Title = "Void Class Monitor",
            Gender = PlayerGender.Female,
            MaxHealth = 4,
            CardFileName = "IMG_20260423_125518.png",
            Summary = "Pressure attacker with discard pressure, judgement payoff, and burst potential.",
            SkillIds = new List<string> { "discard_strike", "judgement_draw", "first_damage_boost" }
        },
        ["huanglubaiquan"] = new GeneralDefinition
        {
            GeneralId = "huanglubaiquan",
            DisplayName = "Huanglu Baiquan",
            Title = "Resource Tactician",
            Gender = PlayerGender.Male,
            MaxHealth = 4,
            CardFileName = "IMG_20260423_161312.png",
            Summary = "Utility and resource-routing general focused on card flow and judgement support.",
            SkillIds = new List<string> { "active_draw", "off_turn_loss_draw" }
        },
        ["lishengming"] = new GeneralDefinition
        {
            GeneralId = "lishengming",
            DisplayName = "Li Shengming",
            Title = "Eternal Controller",
            Gender = PlayerGender.Male,
            MaxHealth = 5,
            CardFileName = "mmexport1777169744471.jpg",
            Summary = "High-end defensive general with auto dodge and extra draw tools.",
            SkillIds = new List<string> { "auto_dodge", "bonus_draw" }
        },
        ["chenchen"] = new GeneralDefinition
        {
            GeneralId = "chenchen",
            DisplayName = "Chenchen",
            Title = "Thousand-Bell Resolve",
            Gender = PlayerGender.Female,
            MaxHealth = 4,
            CardFileName = "file_000000000f78720981f51191eb4b20cc.png",
            Summary = "Resource-cycle general with steady draw and active planning.",
            SkillIds = new List<string> { "bonus_draw", "active_draw" }
        },
        ["hanfeiyang"] = new GeneralDefinition
        {
            GeneralId = "hanfeiyang",
            DisplayName = "Han Feiyang",
            Title = "Relentless Momentum",
            Gender = PlayerGender.Male,
            MaxHealth = 4,
            CardFileName = "file_0000000017b07209a00bb1ec28212d8e.png",
            Summary = "Burst attacker built around card cost, pressure, and chain tempo.",
            SkillIds = new List<string> { "discard_strike" }
        },
        ["funingna"] = new GeneralDefinition
        {
            GeneralId = "funingna",
            DisplayName = "Funingna",
            Title = "Singer of Fortune",
            Gender = PlayerGender.Female,
            MaxHealth = 3,
            CardFileName = "file_000000003cdc720992d0d8f04661fe90.png",
            Summary = "Control-leaning general focused on damage response and hand flow.",
            SkillIds = new List<string> { "bonus_draw", "pain_draw" }
        },
        ["jiefeng"] = new GeneralDefinition
        {
            GeneralId = "jiefeng",
            DisplayName = "Jiefeng",
            Title = "Blade of Gale",
            Gender = PlayerGender.Male,
            MaxHealth = 4,
            CardFileName = "file_000000005864720998ce9b8a58e8e031.png",
            Summary = "Mobile defender that fits the automatic dodge skill shell.",
            SkillIds = new List<string> { "auto_dodge" }
        }
    };

    public static IReadOnlyCollection<GeneralDefinition> All
    {
        get
        {
            EnsureExternalCatalogLoaded();
            return BuildAllDefinitions();
        }
    }

    public static bool TryGetDefinition(string generalId, out GeneralDefinition definition)
    {
        EnsureExternalCatalogLoaded();
        if (!string.IsNullOrWhiteSpace(generalId) && _definitions.TryGetValue(generalId.Trim(), out GeneralDefinition? existing))
        {
            definition = CloneDefinition(existing);
            return true;
        }

        definition = null!;
        return false;
    }

    public static GeneralDefinition Resolve(string generalId, string fallbackName)
    {
        if (TryGetDefinition(generalId, out GeneralDefinition definition))
        {
            return definition;
        }

        string normalizedName = string.IsNullOrWhiteSpace(fallbackName) ? "Unknown" : fallbackName.Trim();
        return new GeneralDefinition
        {
            GeneralId = string.IsNullOrWhiteSpace(generalId) ? normalizedName.ToLowerInvariant() : generalId,
            DisplayName = normalizedName,
            Title = "Unmapped General",
            MaxHealth = 4,
            CardFileName = string.IsNullOrWhiteSpace(generalId) ? string.Empty : $"{generalId}.png",
            Summary = "No catalog entry yet. Assign art through the GeneralArtMapperPanel scene if needed."
        };
    }

    public static void Reload()
    {
        _externalCatalogLoaded = false;
        EnsureExternalCatalogLoaded();
    }

    private static void EnsureExternalCatalogLoaded()
    {
        if (_externalCatalogLoaded)
        {
            return;
        }

        _externalCatalogLoaded = true;
        string absolutePath = ProjectSettings.GlobalizePath(CatalogResourcePath);
        if (!File.Exists(absolutePath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(absolutePath);
            GeneralCatalogData? data = JsonSerializer.Deserialize<GeneralCatalogData>(json, CreateJsonOptions());
            if (data?.Generals is null)
            {
                return;
            }

            foreach (GeneralDefinition definition in data.Generals)
            {
                if (definition is null || string.IsNullOrWhiteSpace(definition.GeneralId))
                {
                    continue;
                }

                _definitions[definition.GeneralId.Trim()] = NormalizeDefinition(definition);
            }
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Failed to load general catalog: {ex.Message}");
        }
    }

    private static GeneralDefinition NormalizeDefinition(GeneralDefinition definition)
    {
        string normalizedId = definition.GeneralId.Trim();
        return new GeneralDefinition
        {
            GeneralId = normalizedId,
            DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? normalizedId : definition.DisplayName.Trim(),
            Title = definition.Title ?? string.Empty,
            Gender = definition.Gender,
            MaxHealth = System.Math.Max(1, definition.MaxHealth),
            CardFileName = definition.CardFileName ?? string.Empty,
            Summary = definition.Summary ?? string.Empty,
            SkillIds = definition.SkillIds?
                .Where(skillId => !string.IsNullOrWhiteSpace(skillId))
                .Select(skillId => skillId.Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>()
        };
    }

    private static IReadOnlyCollection<GeneralDefinition> BuildAllDefinitions()
    {
        List<GeneralDefinition> definitions = _definitions.Values
            .Select(CloneDefinition)
            .ToList();
        HashSet<string> usedFiles = definitions
            .Select(definition => definition.CardFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        foreach (string fileName in GeneralArtRegistry.ListAvailableImageFiles())
        {
            if (usedFiles.Contains(fileName))
            {
                continue;
            }

            string generalId = BuildGeneratedGeneralId(fileName);
            if (_definitions.ContainsKey(generalId) || definitions.Any(definition => definition.GeneralId == generalId))
            {
                continue;
            }

            definitions.Add(new GeneralDefinition
            {
                GeneralId = generalId,
                DisplayName = BuildGeneratedDisplayName(fileName),
                Title = BuildPrototypeTitle(fileName),
                Gender = BuildPrototypeGender(fileName),
                MaxHealth = 4,
                CardFileName = fileName,
                Summary = "Auto-discovered from the general card folder with prototype skills. Refine in Data/general_catalog.json when design is finalized.",
                SkillIds = BuildPrototypeSkillIds(fileName)
            });
        }

        return definitions
            .OrderBy(definition => definition.DisplayName)
            .ToList();
    }

    private static GeneralDefinition CloneDefinition(GeneralDefinition definition)
    {
        return new GeneralDefinition
        {
            GeneralId = definition.GeneralId,
            DisplayName = definition.DisplayName,
            Title = definition.Title,
            Gender = definition.Gender,
            MaxHealth = definition.MaxHealth,
            CardFileName = definition.CardFileName,
            Summary = BuildSummaryWithSkillMetadata(definition),
            SkillIds = new List<string>(definition.SkillIds)
        };
    }

    private static string BuildSummaryWithSkillMetadata(GeneralDefinition definition)
    {
        IReadOnlyList<SkillDefinition> skillDefinitions = SkillRegistry.ResolveMany(definition.SkillIds);
        if (skillDefinitions.Count == 0)
        {
            return definition.Summary;
        }

        string skillSummary = string.Join("; ", skillDefinitions.Select(skill => $"{skill.DisplayName} [{skill.TriggerTimings}]"));
        return string.IsNullOrWhiteSpace(definition.Summary)
            ? $"Skills: {skillSummary}"
            : $"{definition.Summary} Skills: {skillSummary}";
    }

    private static string BuildGeneratedGeneralId(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        StringBuilder builder = new("art");
        foreach (char character in stem)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.Length > 3 ? builder.ToString() : $"art{System.Math.Abs(fileName.GetHashCode())}";
    }

    private static string BuildGeneratedDisplayName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(stem) ? "Unnamed Art General" : stem;
    }

    private static string BuildPrototypeTitle(string fileName)
    {
        string[] titles =
        {
            "Dimensional Vanguard",
            "Echo Tactician",
            "Judgement Weaver",
            "Burst Duelist",
            "Flow Defender",
            "Resource Adept"
        };
        return titles[ComputeStableHash(fileName) % titles.Length];
    }

    private static PlayerGender BuildPrototypeGender(string fileName)
    {
        return (ComputeStableHash(fileName) % 3) switch
        {
            0 => PlayerGender.Female,
            1 => PlayerGender.Male,
            _ => PlayerGender.Unknown
        };
    }

    private static List<string> BuildPrototypeSkillIds(string fileName)
    {
        string[][] presets =
        {
            new[] { "bonus_draw" },
            new[] { "pain_draw" },
            new[] { "active_draw" },
            new[] { "discard_strike" },
            new[] { "auto_dodge" },
            new[] { "off_turn_loss_draw" },
            new[] { "judgement_draw", "active_draw" },
            new[] { "first_damage_boost", "pain_draw" }
        };

        return presets[ComputeStableHash(fileName) % presets.Length].ToList();
    }

    private static int ComputeStableHash(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value ?? string.Empty)
            {
                hash = hash * 31 + character;
            }

            return System.Math.Abs(hash);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
