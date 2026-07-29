using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Skills;
using System.Security.Cryptography;
using System.Text;

namespace CiyuanSha.GameCore.Tests;

public sealed class ContentPackTests
{
    [Fact]
    public void BundledPacks_AreHashedAndComplete()
    {
        (ContentRegistry registry, _) = TestContent.Load();

        Assert.Equal(5, registry.Packs.Count);
        Assert.Equal(12, registry.Generals.Count);
        Assert.Equal(16, registry.Skills.Count);
        Assert.Equal(43, registry.Cards.Count);
        DeckDefinition deck = Assert.Single(registry.Decks.Values);
        Assert.Equal(161, deck.Cards.Sum(card => card.Copies));
        Assert.All(registry.Packs.Values, pack => Assert.Equal(64, pack.ComputedHash.Length));
    }

    [Fact]
    public void StandardMilitaryDeck_All161PhysicalCardsMatchReviewedSuitRankVector()
    {
        DeckDefinition deck = Assert.Single(TestContent.Load().Registry.Decks.Values);
        Assert.Equal(148, deck.Cards.Count);
        Assert.Equal(161, deck.Cards.Sum(entry => entry.Copies));
        Assert.All(deck.Cards, entry =>
        {
            Assert.NotEqual(CiyuanSha.GameCore.Domain.CardSuit.None, entry.Suit);
            Assert.InRange(entry.Rank, 1, 13);
            Assert.InRange(entry.Copies, 1, 2);
        });

        string canonical = string.Join('|', deck.Cards.Select(entry =>
            $"{entry.CardId}:{(int)entry.Suit}:{entry.Rank}:{entry.Copies}"));
        string reviewedVectorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Assert.Equal("c09149c0d6a88d45b3533250f75c13a548df33f38f82e74851cdf8223be486fa", reviewedVectorHash);
    }

    [Fact]
    public void EveryCardDefinition_HasAnExplicitRuleVectorAndRegisteredEffect()
    {
        (ContentRegistry registry, EffectRegistry effects) = TestContent.Load();
        IReadOnlyDictionary<string, string> reviewedVectors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["slash"] = "basic.slash", ["fire_slash"] = "basic.elemental_slash", ["thunder_slash"] = "basic.elemental_slash",
            ["dodge"] = "response.dodge", ["peach"] = "basic.heal_and_rescue", ["wine"] = "basic.wine",
            ["duel"] = "trick.duel_alternation", ["lightning"] = "delayed.lightning", ["snatch"] = "trick.region_steal",
            ["dismantle"] = "trick.region_discard", ["indulgence"] = "delayed.skip_play", ["supply_shortage"] = "delayed.skip_draw",
            ["barbarians"] = "trick.mass_slash", ["nullification"] = "response.nullification_chain", ["peach_garden"] = "trick.mass_heal",
            ["arrow_barrage"] = "trick.mass_dodge", ["harvest"] = "trick.processing_pick", ["ex_nihilo"] = "trick.draw_two",
            ["borrow_sword"] = "trick.forced_slash_or_transfer", ["fire_attack"] = "trick.reveal_suit_fire", ["iron_chain"] = "trick.chain_or_recast",
            ["crossbow"] = "equipment.unlimited_slash", ["double_swords"] = "equipment.gender_pressure", ["qinggang_sword"] = "equipment.ignore_armor",
            ["ice_sword"] = "equipment.prevent_damage_discard", ["green_dragon_blade"] = "equipment.followup_slash", ["serpent_spear"] = "equipment.two_cards_as_slash",
            ["stone_axe"] = "equipment.force_hit_cost", ["fangtian_halberd"] = "equipment.last_card_three_targets", ["kylin_bow"] = "equipment.remove_mount",
            ["guding_blade"] = "equipment.empty_hand_damage", ["vermilion_fan"] = "equipment.slash_to_fire", ["eight_diagram"] = "equipment.judgement_dodge",
            ["renwang_shield"] = "equipment.black_slash_immunity", ["vine_armor"] = "equipment.normal_slash_mass_immunity_fire", ["silver_lion"] = "equipment.damage_cap_and_lost_heal",
            ["chitu"] = "equipment.offensive_distance", ["dawan"] = "equipment.offensive_distance", ["zixing"] = "equipment.offensive_distance",
            ["jueying"] = "equipment.defensive_distance", ["dilu"] = "equipment.defensive_distance", ["zhuahuangfeidian"] = "equipment.defensive_distance",
            ["hualiu"] = "equipment.defensive_distance"
        };

        Assert.Equal(registry.Cards.Keys.OrderBy(id => id), reviewedVectors.Keys.OrderBy(id => id));
        Assert.All(registry.Cards.Values, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(reviewedVectors[definition.CardId]));
            Assert.True(effects.HasCardEffect(definition.EffectId), $"Missing registered handler for {definition.CardId}/{definition.EffectId}.");
        });
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

    [Fact]
    public void Registry_RejectsMissingDependencyAndUnknownCardAndSkillEffects()
    {
        LoadedContentPack invalid = new()
        {
            RootPath = ".",
            ComputedHash = new string('a', 64),
            Manifest = new ContentPackManifest
            {
                PackId = "invalid-pack",
                DisplayName = "Invalid",
                Version = "2.0.0",
                EngineApiVersion = "2.0.0",
                License = "GPL-3.0-only",
                Attribution = "test",
                Dependencies = new() { new ContentPackDependency("missing-pack", "2.0.0") }
            },
            Data = new ContentPackData
            {
                Cards = new()
                {
                    new CardDefinition("bad-card", "Bad", "Bad", CiyuanSha.GameCore.Domain.CardCategory.Basic, "card.unknown")
                },
                Skills = new()
                {
                    new SkillDefinitionV2(
                        "bad-skill",
                        "Bad",
                        "Bad",
                        "skill.unknown",
                        SkillTags.Locked,
                        SkillUsageScope.Unlimited,
                        Array.Empty<SkillTriggerDefinition>())
                }
            }
        };
        ContentRegistry registry = new();

        ContentValidationResult result = registry.Register(new[] { invalid }, BuiltInEffects.CreateRegistry());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("missing dependency missing-pack", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("unknown effect card.unknown", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("unknown effect skill.unknown", StringComparison.Ordinal));
        Assert.Empty(registry.Packs);
    }
}
