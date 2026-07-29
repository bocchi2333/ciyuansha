using CiyuanSha.GameCore.Content;

namespace CiyuanSha.GameCore.Tests;

public sealed class ContentPackTests
{
    [Fact]
    public void BundledPacks_AreHashedAndComplete()
    {
        (ContentRegistry registry, _) = TestContent.Load();

        Assert.Equal(5, registry.Packs.Count);
        Assert.Equal(12, registry.Generals.Count);
        Assert.Equal(15, registry.Skills.Count);
        Assert.Equal(43, registry.Cards.Count);
        DeckDefinition deck = Assert.Single(registry.Decks.Values);
        Assert.Equal(161, deck.Cards.Sum(card => card.Copies));
        Assert.All(registry.Packs.Values, pack => Assert.Equal(64, pack.ComputedHash.Length));
    }

    [Fact]
    public void Loader_RejectsHashMismatch()
    {
        string pack = Path.Combine(TestContent.FindWorkspaceRoot(), "Data", "Packs", "core-rules");
        string manifestPath = Path.Combine(pack, "manifest.json");
        string original = File.ReadAllText(manifestPath);
        string temporary = Path.Combine(Path.GetTempPath(), $"ciyuansha-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "manifest.json"), original.Replace(
                "6421282a15d8b1663747f318466059f71816d5be93afb3961eab6869b8e745ad",
                new string('0', 64),
                StringComparison.Ordinal));
            File.Copy(Path.Combine(pack, "rules.json"), Path.Combine(temporary, "rules.json"));
            Assert.Throws<InvalidDataException>(() => ContentPackLoader.LoadDirectory(temporary));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
