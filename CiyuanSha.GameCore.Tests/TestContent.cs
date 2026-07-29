using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Skills;

namespace CiyuanSha.GameCore.Tests;

internal static class TestContent
{
    public static (ContentRegistry Registry, EffectRegistry Effects) Load()
    {
        string root = FindWorkspaceRoot();
        EffectRegistry effects = BuiltInEffects.CreateRegistry();
        LoadedContentPack[] packs = Directory.GetDirectories(Path.Combine(root, "Data", "Packs"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => ContentPackLoader.LoadDirectory(path))
            .ToArray();
        ContentRegistry registry = new();
        ContentValidationResult validation = registry.Register(packs, effects);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        return (registry, effects);
    }

    public static string FindWorkspaceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Data", "Packs")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate workspace Data/Packs.");
    }
}
