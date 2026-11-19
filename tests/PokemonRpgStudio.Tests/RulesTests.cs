using System.Text.Json;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Export;
using PokemonRpgStudio.Core.Json;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;
using PokemonRpgStudio.Core.Storage;
using System.Text;

namespace PokemonRpgStudio.Tests;

public sealed class DataFixture
{
    public GameData Data { get; } = GameData.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
}

/// <summary>Port of <c>tests/pokemon-generator.test.mjs</c>: every Pokémon at level 90.</summary>
public class AllPokemonTests : IClassFixture<DataFixture>
{
    private const int Level = 90;
    private const int AvailablePoints = 9 + Level;
    private readonly GameData _data;

    public AllPokemonTests(DataFixture fixture) => _data = fixture.Data;

    [Fact]
    public void Data_loads()
    {
        Assert.True(_data.Pokemon.Count > 2000);
        Assert.True(_data.Moves.Count > 1000);
        Assert.True(_data.Abilities.Count > 1000);
        Assert.NotNull(_data.FindPokemon("bulbasaur"));
        Assert.NotNull(_data.FindMove("Tackle"));
    }

    [Fact]
    public void Final_stats_are_never_negative_with_zero_points()
    {
        foreach (var p in _data.Pokemon)
            foreach (var card in SheetRules.Cards)
                foreach (var rarity in SheetRules.Rarities)
                {
                    var stats = SheetRules.ComputeFinalStats(p, card, rarity, "HP", "ATK", SheetRulesDefaults.EmptyStats());
                    Assert.All(stats.Values, v => Assert.True(v >= 0, $"{p.Name}: negative stat"));
                }
    }

    [Fact]
    public void Auto_points_spend_exactly_the_available_points()
    {
        var rng = new Random(42);
        foreach (var p in _data.Pokemon)
        {
            var points = SheetRules.ComputePointsAutomatically(p, AvailablePoints, rng);
            Assert.Equal(AvailablePoints, points.Values.Sum());
            Assert.All(points.Values, v => Assert.True(v >= 0, $"{p.Name}: negative points"));
        }
    }

    [Fact]
    public void Pick_moves_keeps_every_chosen_and_locked_move()
    {
        foreach (var p in _data.Pokemon)
        {
            var state = SheetRules.NewSheet(p);
            state.Level = Level;
            state.ChosenMoves = SheetRules.AvailableLevelUpMoves(p, Level)
                .Select(m => _data.FindMove(m.Name)).Where(m => m != null).Take(6).Select(m => m!.Name).ToList();
            state.EggMoves = _data.ResolveMoves(_data.EffectiveEggMoves(p)).Take(3).Select(m => m.Name).ToList();
            state.LockedEggMoves = state.EggMoves.ToList();
            var moves = SheetRules.PickMoves(state, p, _data.FindMove);
            Assert.Equal(state.ChosenMoves.Distinct().Count() + state.LockedEggMoves.Except(state.ChosenMoves).Count(), moves.Count);
        }
    }

    [Fact]
    public void Updated_roll_with_stab_stays_in_the_damage_table()
    {
        var buffs = new Dictionary<string, int>();
        foreach (var p in _data.Pokemon)
            foreach (var move in _data.ResolveMoves(p.Moves))
            {
                var roll = SheetRules.UpdatedRoll(p, buffs, move);
                if (move.DamageBase > 0)
                    Assert.Contains(roll, SheetRules.DamageBaseToRoll.Values);
            }
    }

    [Fact]
    public void Every_pokemon_exports_markdown_and_html()
    {
        foreach (var p in _data.Pokemon.Take(300))
        {
            var state = SheetRules.NewSheet(p);
            state.Level = 30;
            state.Gender = "Male";
            state.BuffedStat = "ATK";
            state.LoweredStat = "DEF";
            state.PointsByStat = SheetRules.ComputePointsAutomatically(p, SheetRules.AvailablePoints(30, state.PointsByStat), new Random(1));
            Assert.True(SheetRules.IsComplete(state), p.Name);
            var snap = SheetSnapshot.Create(state, p, _data, "notes");
            Assert.Contains("## **Moves**", SheetExporter.ToMarkdown(snap));
            Assert.Contains("<h2>Moves</h2>", SheetExporter.ToHtmlFragment(snap));
        }
    }
}

public class RuleUnitTests
{
    private static PokemonSpecies Species(params string[] types) => new()
    {
        Name = "testmon",
        StatHp = 5, StatAtk = 6, StatDef = 4, StatSpAtk = 7, StatSpDef = 5, StatSpd = 8,
        PokemonTypes = types.ToList(),
        GenderRatioM = 50,
    };

    [Fact]
    public void Stats_buff_matches_the_website_rules()
    {
        // No card, normal rarity, +ATK/-DEF: +2 / -2.
        var b = SheetRules.ComputeStatsBuff("None", "Normal", "ATK", "DEF");
        Assert.Equal(2, b["ATK"]);
        Assert.Equal(-2, b["DEF"]);
        Assert.Equal(0, b["HP"]);

        // Normal card (+1 all) and shiny (+2 all); nature bonus becomes 2+2 = 4.
        b = SheetRules.ComputeStatsBuff("Normal", "Shiny", "ATK", "DEF");
        Assert.Equal(3 + 4, b["ATK"]);
        Assert.Equal(3 - 2, b["DEF"]);
        Assert.Equal(3, b["SPD"]);

        // Neutral nature: lowered then buffed by 2 → unchanged, card bonus not added to nature.
        b = SheetRules.ComputeStatsBuff("Platine", "Platine", "SPD", "SPD");
        Assert.Equal(7, b["SPD"]);
        Assert.Equal(7, b["HP"]);
    }

    [Fact]
    public void Hp_evades_and_tutor_points()
    {
        Assert.Equal(10 + 3 * 8 + 10, SheetRules.HitPoints(10, 8));
        Assert.Equal(1, SheetRules.Evade(19));
        Assert.Equal(4, SheetRules.TutorPoints(24));
    }

    [Fact]
    public void Tutor_points_one_every_5_levels_plus_hand_changes()
    {
        Assert.Equal(0, SheetRules.TutorPoints(1));
        Assert.Equal(0, SheetRules.TutorPoints(4));
        Assert.Equal(1, SheetRules.TutorPoints(5));
        Assert.Equal(2, SheetRules.TutorPoints(10));
        Assert.Equal(4, SheetRules.TutorPoints(12, 2));
        Assert.Equal(1, SheetRules.TutorPoints(12, -1));
        Assert.Equal(0, SheetRules.TutorPoints(12, -5)); // never below 0
        var state = new SheetState { TutorPointsAdjustment = 3, TutorNotes = "Saved for Earthquake" };
        var copy = state.Clone();
        Assert.Equal((3, "Saved for Earthquake"), (copy.TutorPointsAdjustment, copy.TutorNotes));
        Assert.Equal(9 + 10 - 3, SheetRules.AvailablePoints(10, new Dictionary<string, int> { ["HP"] = 3 }));
    }

    [Fact]
    public void Overstated_sheets_are_allowed()
    {
        var s = new SheetState { ChosenPokemon = "Bulbasaur", Level = 10, Rarity = "Normal", Card = "None", Gender = "Male", BuffedStat = "SPD", LoweredStat = "ATK" };
        s.PointsByStat["HP"] = 9 + 10 + 5; // 5 points over the budget
        Assert.Equal(-5, SheetRules.AvailablePoints(s.Level, s.PointsByStat));
        Assert.Empty(SheetRules.MissingRequirements(s));
        Assert.True(SheetRules.IsComplete(s));
    }

    [Fact]
    public void Fractional_levels_count_completed_levels_only()
    {
        Assert.Equal(SheetRules.HitPoints(10, 8), SheetRules.HitPoints(10.7, 8));
        Assert.Equal(4, SheetRules.TutorPoints(24.9));
        Assert.Equal(5, SheetRules.TutorPoints(25));
        Assert.Equal(9 + 10, SheetRules.AvailablePoints(10.5, new Dictionary<string, int>()));
        Assert.Equal("12.5", SheetRules.FormatLevel(12.5));
        Assert.Equal("12", SheetRules.FormatLevel(12));
        Assert.Equal("12.33", SheetRules.FormatLevel(12.3333));
    }

    [Fact]
    public void Fractional_level_round_trips_through_json()
    {
        var json = JsonSerializer.Serialize(new SheetState { Level = 12.4 });
        Assert.Equal(12.4, JsonSerializer.Deserialize<SheetState>(json)!.Level);
        Assert.Equal(7.5, JsonSerializer.Deserialize<SheetState>("{\"pokemon_level\":\"7.5\"}")!.Level);
        Assert.Equal(30, JsonSerializer.Deserialize<SheetState>("{\"pokemon_level\":30}")!.Level);
    }

    [Fact]
    public void Nature_names()
    {
        Assert.Equal("Adamant (+ATK/-SPATK)", SheetRules.NatureName("ATK", "SPATK"));
        Assert.Equal("Neutral (+HP/-HP)", SheetRules.NatureName("HP", "HP"));
        Assert.Equal("", SheetRules.NatureName("", ""));
    }

    [Fact]
    public void Stab_and_db_buff_raise_the_roll()
    {
        var fire = Species("Fire");
        var ember = new MoveData { Name = "Ember", Types = new() { "Fire" }, DamageBase = 4, Roll = "1d8+6" };
        Assert.Equal("2d6+8", SheetRules.UpdatedRoll(fire, new Dictionary<string, int>(), ember)); // 4 + 2 STAB = 6
        Assert.Equal("2d6+10", SheetRules.UpdatedRoll(fire, new Dictionary<string, int> { ["Ember"] = 1 }, ember));
        var water = Species("Water");
        Assert.Equal("1d8+6", SheetRules.UpdatedRoll(water, new Dictionary<string, int>(), ember));
        var status = new MoveData { Name = "Growl", Types = new() { "Normal" }, DamageBase = -1, Roll = "" };
        Assert.Equal("", SheetRules.UpdatedRoll(water, new Dictionary<string, int>(), status));
    }

    [Fact]
    public void Pick_moves_allows_more_than_six_normal_moves()
    {
        var sp = Species("Normal");
        var state = new SheetState
        {
            ChosenMoves = Enumerable.Range(1, 8).Select(i => "M" + i).ToList(),
            LockedEggMoves = new() { "E1", "E2" },
            LockedTmMoves = new() { "T1", "E1" },
        };
        var moves = SheetRules.PickMoves(state, sp, _ => null);
        Assert.Equal(new[] { "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8", "E1", "E2", "T1" }, moves.Select(m => m.Name));
    }

    [Fact]
    public void Damage_adds_attack_or_special_attack()
    {
        Assert.Equal("ATK", SheetRules.DamageStat(new MoveData { MoveClass = "Physical" }));
        Assert.Equal("SPATK", SheetRules.DamageStat(new MoveData { MoveClass = " Special" }));
        Assert.Null(SheetRules.DamageStat(new MoveData { MoveClass = "Status" }));

        var dmg = Dice.RollDamage("2d6+8", 15, "SPATK", critical: false, new Random(5));
        Assert.Equal(dmg.Roll.Rolls[0].Total + 8 + 15, dmg.Total);
        Assert.Contains("SP.ATK 15", dmg.Describe());
    }

    [Fact]
    public void Critical_hits_roll_twice_as_many_dice_but_add_the_bonus_and_stat_once()
    {
        var crit = Dice.RollDamage("2d6+8", 10, "ATK", critical: true, new Random(9));
        Assert.Equal("4d6", crit.Roll.Rolls[0].Dice);
        Assert.Equal(4, crit.Roll.Rolls[0].Results.Count);
        Assert.Equal(crit.Roll.Rolls[0].Total + 8 + 10, crit.Total);
        Assert.Contains("4d6 (", crit.Describe());

        var normal = Dice.RollDamage("2d6+1d8+8", 0, null, critical: false, new Random(9));
        Assert.Equal(new[] { 2, 1 }, normal.Roll.Rolls.Select(r => r.Results.Count));
        var crit2 = Dice.RollDamage("2d6+1d8+8", 0, null, critical: true, new Random(9));
        Assert.Equal(new[] { 4, 2 }, crit2.Roll.Rolls.Select(r => r.Results.Count));

        // Accuracy 20 → critical. Find a seed that rolls a 20 first.
        var seed = Enumerable.Range(0, 1000).First(i => new Random(i).Next(1, 21) == 20);
        var attack = Dice.RollAttack("1d8+6", 4, 12, "ATK", new Random(seed));
        Assert.Equal(20, attack.Accuracy);
        Assert.True(attack.Critical);
        Assert.True(attack.MeetsAc);
        Assert.Equal("2d8", attack.Damage!.Roll.Rolls[0].Dice);
        Assert.Equal(attack.Damage.Roll.Rolls[0].Total + 6 + 12, attack.Damage.Total);
        Assert.Contains("CRITICAL", attack.AccuracyText);
    }

    [Fact]
    public void Several_stats_can_be_added_to_a_damage_roll()
    {
        var stats = new[] { new StatBonus("ATK", 13), new StatBonus("SPD", 18) };
        var d = Dice.RollDamage("2d6+8", stats, critical: false, new Random(4));
        Assert.Equal(d.Roll.Rolls[0].Total + 8 + 13 + 18, d.Total);
        Assert.Equal(31, d.StatTotal);
        Assert.Contains("+ ATK 13 + SPEED 18", d.Describe());

        var crit = Dice.RollDamage("2d6+8", stats, critical: true, new Random(4));
        Assert.Equal("4d6", crit.Roll.Rolls[0].Dice);
        Assert.Equal(crit.Roll.Rolls[0].Total + 8 + 31, crit.Total); // stats still added once

        var none = Dice.RollDamage("1d4", Array.Empty<StatBonus>(), false, new Random(4));
        Assert.Equal(none.Roll.Final, none.Total);
    }

    [Fact]
    public void Ac_text_is_parsed()
    {
        Assert.Equal(4, Dice.ParseAc("4"));
        Assert.Equal(2, Dice.ParseAc("AC 2"));
        Assert.Null(Dice.ParseAc("--"));
        Assert.Null(Dice.ParseAc(null));
    }

    [Fact]
    public void Level_constraints_clear_high_tier_abilities()
    {
        var s = new SheetState { Level = 19, AdvancedAbilities = { "A" }, HighAbilities = { "H" } };
        SheetRules.ApplyLevelConstraints(s);
        Assert.Empty(s.AdvancedAbilities);
        Assert.Empty(s.HighAbilities);
    }

    [Fact]
    public void Dice_expression_is_evaluated_safely()
    {
        var r = Dice.RollExpression("2d6+1d8+4", new Random(3));
        Assert.Equal(2, r.Rolls.Count);
        Assert.Equal(r.Rolls.Sum(x => x.Total) + 4, r.Final);
        Assert.Equal(7, Dice.RollExpression("(1+2)*3-2").Final);
        Assert.Throws<FormatException>(() => Dice.RollExpression("alert(1)"));
        Assert.Throws<FormatException>(() => Dice.RollExpression("1/0"));
    }

    [Fact]
    public void Encounter_rules_follow_the_table()
    {
        Assert.Equal("0 encounter", Dice.ResolveEncounterRule(0).Label);
        Assert.Equal("2 encounters", Dice.ResolveEncounterRule(3).Label);
        for (int i = 0; i < 50; i++)
        {
            var n = int.Parse(Dice.ResolveEncounterRule(8).Label.Split(' ')[0]);
            Assert.InRange(n, 5, 8);
            Assert.StartsWith("Outbreak", Dice.ResolveEncounterRule(20).Label);
        }
    }

    [Fact]
    public void Shiny_rolls_never_land_in_the_reroll_window()
    {
        var rng = new Random(7);
        var rolls = Dice.RollShinyBatch(200, shinyNumber: 50, dexnav: 40, rng);
        Assert.Equal(200, rolls.Count);
        Assert.All(rolls, r => Assert.True(r > 40 || r == 50));
    }

    [Fact]
    public void Card_rolls_produce_extra_batches_for_special_pokemon()
    {
        var settings = new CardRollSettings { Dexnav = 30 };
        var normal = Dice.RollCards(5, settings, false, false, new Random(1));
        Assert.Empty(normal.PlatineTier);
        Assert.Equal(5, normal.BaseTier.Count);
        var shiny = Dice.RollCards(5, settings, true, false, new Random(1));
        Assert.Equal(5, shiny.PlatineTier.Count);
        Assert.Equal(5, shiny.ShinyTier.Count);
        Assert.All(shiny.BaseTier, r => Assert.True(r > 31 || r == 1));
    }

    [Fact]
    public void Evolution_entries_are_split_into_name_and_condition()
    {
        Assert.Equal(("Ivysaur", "Minimum 15"), PokedexHelpers.ParseEvolEntry("Ivysaur Minimum 15"));
        Assert.Equal(("Farigiraf", "(30)"), PokedexHelpers.ParseEvolEntry("Farigiraf (30)"));
        Assert.Equal(("Pichu", ""), PokedexHelpers.ParseEvolEntry("Pichu"));
    }

    [Fact]
    public void Image_names_match_the_website()
    {
        Assert.Equal("nidoran_(f).png", ImageResolver.WebsiteImageName("Nidoran (F)"));
        Assert.Equal("mr_mime", ImageResolver.NormalizeBaseName("Mr. Mime"));
        Assert.Equal("flabebe", ImageResolver.NormalizeBaseName("flabébé"));
    }
}

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "prs-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Sheets_round_trip_in_the_website_format()
    {
        var lib = new UserLibrary(new AppPaths(_dir));
        var sheet = new SheetRecord
        {
            Nickname = "Sparky",
            PokemonName = "pikachu",
            State = new SheetState { ChosenPokemon = "pikachu", Level = 12, DbBuffByMove = { ["Thunder Shock"] = 2 } },
        };
        lib.Sheets.Save(sheet);

        var json = File.ReadAllText(Path.Combine(lib.Paths.Sheets, sheet.Id + ".json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(12, doc.RootElement.GetProperty("state").GetProperty("pokemon_level").GetInt32());
        Assert.Equal("pikachu", doc.RootElement.GetProperty("pokemonName").GetString());

        var loaded = lib.Sheets.Get(sheet.Id)!;
        Assert.Equal("Sparky", loaded.Nickname);
        Assert.Equal(2, loaded.State.DbBuffByMove["Thunder Shock"]);
        Assert.Single(lib.Sheets.List());
        Assert.True(lib.Sheets.Delete(sheet.Id));
        Assert.Empty(lib.Sheets.List());
    }

    [Fact]
    public void Session_changes_are_saved_and_rest_clears_them_but_keeps_injuries_and_notes()
    {
        var lib = new UserLibrary(new AppPaths(_dir));
        var sheet = new SheetRecord
        {
            Nickname = "Sparky", PokemonName = "pikachu",
            Play = new SheetPlayState
            {
                CurrentHp = 30, MaxHpModifier = 10, Injuries = 1,
                StatModifiers = { ["ATK"] = 2 },
                DisabledMoves = { "Thunderbolt" },
                SkillModifiers = { ["Athl"] = -1 },
                Notes = { ["moves"] = "Thunderbolt disabled by Torment (3 turns)" },
            },
        };
        lib.Sheets.Save(sheet);
        var p = lib.Sheets.Get(sheet.Id)!.Play!;
        Assert.Equal(10, p.MaxHpModifier);
        Assert.True(p.IsMoveDisabled("thunderbolt"));
        Assert.Equal(-1, p.SkillModifier("Athl"));
        Assert.Equal("Thunderbolt disabled by Torment (3 turns)", p.Note("moves"));
        Assert.Equal(60, p.EffectiveMaxHp(50));
        Assert.Equal(1, new SheetPlayState { MaxHpModifier = -99 }.EffectiveMaxHp(50));
        Assert.Equal(30, p.HpStatus(50).CurrentHp);

        p.Reset();
        Assert.Equal((0, 1, 0, 0, 0), (p.MaxHpModifier, p.Injuries, p.StatModifiers.Count, p.DisabledMoves.Count, p.SkillModifiers.Count));
        Assert.Null(p.CurrentHp);
        Assert.Equal("Thunderbolt disabled by Torment (3 turns)", p.Note("moves"));

        // Older play blocks (without the new fields) still load.
        File.WriteAllText(Path.Combine(lib.Paths.Sheets, sheet.Id + ".json"),
            """{"id":"ID","nickname":"Sparky","pokemonName":"pikachu","state":{},"play":{"currentHp":5,"injuries":0,"statModifiers":{}}}""".Replace("ID", sheet.Id));
        var old = lib.Sheets.Get(sheet.Id)!.Play!;
        Assert.Equal(5, old.CurrentHp);
        Assert.Empty(old.DisabledMoves);
        Assert.Equal("", old.Note("hp"));
    }

    [Fact]
    public void Skill_modifiers_are_added_to_the_roll_formula()
    {
        Assert.Equal("2d6+1+2", Dice.WithModifier("2d6+1", 2));
        Assert.Equal("3d6-1", Dice.WithModifier("3d6", -1));
        Assert.Equal("2d6", Dice.WithModifier("2d6", 0));
        var r = Dice.RollExpression(Dice.WithModifier("2d6", -3), new Random(2));
        Assert.Equal(r.Rolls[0].Total - 3, r.Final);
    }

    [Fact]
    public void Rolled_natures_are_saved_with_the_sheet()
    {
        var lib = new UserLibrary(new AppPaths(_dir));
        var sheet = new SheetRecord { Nickname = "Sparky", PokemonName = "pikachu" };
        lib.Sheets.Save(sheet);
        Assert.DoesNotContain("rolled_natures", File.ReadAllText(Path.Combine(lib.Paths.Sheets, sheet.Id + ".json")));

        sheet.State.RolledNatures = new() { new("ATK", "DEF"), new("SPD", "HP") };
        lib.Sheets.Save(sheet);
        var json = File.ReadAllText(Path.Combine(lib.Paths.Sheets, sheet.Id + ".json"));
        Assert.Contains("\"buffed\": \"SPD\"", json);
        Assert.Equal(sheet.State.RolledNatures, lib.Sheets.Get(sheet.Id)!.State.RolledNatures);
        Assert.Equal(2, sheet.State.Clone().RolledNatures!.Count);
    }

    [Fact]
    public void Play_state_is_saved_with_the_sheet_and_optional()
    {
        var lib = new UserLibrary(new AppPaths(_dir));
        var sheet = new SheetRecord { Nickname = "Sparky", PokemonName = "pikachu" };
        lib.Sheets.Save(sheet);
        // No play state yet: nothing extra is written (keeps website files identical).
        Assert.DoesNotContain("\"play\"", File.ReadAllText(Path.Combine(lib.Paths.Sheets, sheet.Id + ".json")));

        sheet.Play = new SheetPlayState { CurrentHp = 12, Injuries = 2, StatModifiers = { ["ATK"] = 2, ["SPD"] = -1 } };
        lib.Sheets.Save(sheet);
        var loaded = lib.Sheets.Get(sheet.Id)!;
        Assert.Equal(12, loaded.Play!.CurrentHp);
        Assert.Equal(2, loaded.Play.Injuries);
        Assert.Equal(-1, loaded.Play.Modifier("SPD"));
        Assert.Equal(0, loaded.Play.Modifier("DEF"));

        // HP is clamped to the (possibly reduced) maximum; null means full health.
        Assert.Equal(8, loaded.Play.HpStatus(10).CurrentHp);   // 2 injuries: max 10 → 8
        Assert.Equal(30, new SheetPlayState().HpStatus(30).CurrentHp);
        loaded.Play.Reset();
        Assert.Null(loaded.Play.CurrentHp);
        Assert.Empty(loaded.Play.StatModifiers);
    }

    [Fact]
    public void Sheets_store_move_names_only_and_read_old_files_with_full_moves()
    {
        var lib = new UserLibrary(new AppPaths(_dir));
        // Old desktop / website format: full move objects inside the state.
        File.WriteAllText(Path.Combine(lib.Paths.Sheets, "11111111-2222-3333-4444-555555555555.json"), """
            {"id":"11111111-2222-3333-4444-555555555555","nickname":"Old","pokemonName":"pikachu","state":{
              "pokemon_chosen_moves":[{"name":"Thunder Shock","types":["Electric"],"roll":"1d8+6","damage_base":4},"Growl"],
              "pokemon_chosen_egg_moves":[{"name":"Charm","types":["Fairy"]}],
              "pokemon_chosen_tmhm_moves":null}}
            """);
        var old = lib.Sheets.Get("11111111-2222-3333-4444-555555555555")!;
        Assert.Equal(new[] { "Thunder Shock", "Growl" }, old.State.ChosenMoves);
        Assert.Equal(new[] { "Charm" }, old.State.EggMoves);
        Assert.Empty(old.State.TmMoves);

        lib.Sheets.Save(old);
        var json = File.ReadAllText(Path.Combine(lib.Paths.Sheets, old.Id + ".json"));
        using var doc = JsonDocument.Parse(json);
        var chosen = doc.RootElement.GetProperty("state").GetProperty("pokemon_chosen_moves");
        Assert.Equal(new[] { "Thunder Shock", "Growl" }, chosen.EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("damage_base", json);
    }

    [Fact]
    public void Move_details_come_from_moves_json_so_updates_apply_to_existing_sheets()
    {
        var bundled = Path.Combine(_dir, "bundled");
        var user = Path.Combine(_dir, "user");
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "pokemon.json"), """[{"name":"testmon","pokemon_types":["Fire"],"moves":[{"name":"Homebrew","level":1,"types":["Ice"]}]}]""");
        File.WriteAllText(Path.Combine(bundled, "moves.json"), """[{"name":"Ember","types":["Fire"],"damage_base":4,"roll":"1d8+6","m_class":"Special"}]""");
        File.WriteAllText(Path.Combine(bundled, "abilities.json"), "[]");
        var state = new SheetState { ChosenPokemon = "testmon", ChosenMoves = { "Ember", "Homebrew" } };

        Assert.Equal(GameData.FileNames, GameData.SeedDataFolder(user, new[] { bundled }));
        var data = GameData.Load(user);
        var sp = data.FindPokemon("testmon")!;
        var moves = SheetRules.PickMoves(state, sp, data.FindMove);
        Assert.Equal("2d6+8", moves[0].Roll);                  // DB 4 + STAB
        Assert.Equal(new[] { "Ice" }, moves[1].Types);          // unknown move: typed from the learnset

        // The player installs an updated moves.json: the same sheet now shows the new values.
        var updated = Path.Combine(_dir, "moves.json");
        File.WriteAllText(updated, """[{"name":"Ember","types":["Fire"],"damage_base":6,"roll":"2d6+8","m_class":"Special"},{"name":"Homebrew","types":["Ice"],"damage_base":5,"roll":"1d8+8","m_class":"Physical"}]""");
        Assert.Equal("moves.json: 2 entries", GameData.InstallDataFile(updated, user));
        data = GameData.Load(user);
        moves = SheetRules.PickMoves(state, sp, data.FindMove);
        Assert.Equal("2d8+10", moves[0].Roll);                 // DB 6 + STAB = 8
        Assert.Equal("Physical", moves[1].MoveClass);

        Assert.Throws<InvalidDataException>(() => GameData.InstallDataFile(Path.Combine(bundled, "pokemon.json").Replace("pokemon", "nope"), user));

        // Seeding never overwrites the user's files, unless asked (Restore default data).
        Assert.Empty(GameData.SeedDataFolder(user, new[] { bundled }));
        Assert.Equal("2d8+10", SheetRules.PickMoves(state, sp, GameData.Load(user).FindMove)[0].Roll);
        Assert.Equal(GameData.FileNames, GameData.SeedDataFolder(user, new[] { bundled }, overwrite: true));
        Assert.Equal("2d6+8", SheetRules.PickMoves(state, sp, GameData.Load(user).FindMove)[0].Roll);
    }

    [Fact]
    public void Website_server_files_are_imported()
    {
        var src = Path.Combine(_dir, "server");
        Directory.CreateDirectory(Path.Combine(src, "pokemon_cards"));
        Directory.CreateDirectory(Path.Combine(src, "teams"));
        Directory.CreateDirectory(Path.Combine(src, "zones"));
        File.WriteAllText(Path.Combine(src, "pokemon_cards", "a.json"),
            """{"id":"9a96df5e-7c1c-49ae-876f-eb3f224cd7d0","nickname":"Test","pokemonName":"liepard delta form","state":{"pokemon_level":"45","pokemon_base_ability":"Limber"},"notes":"","createdAt":"2026-05-07T09:45:12.931Z","updatedAt":"2026-05-07T10:30:52.338Z"}""");
        File.WriteAllText(Path.Combine(src, "teams", "t.json"),
            """{"id":"e95aaf2e-467a-4ad8-8c93-da9901cd0838","name":"Main","selectedType":"Flying","slots":[{"cardId":"9a96df5e-7c1c-49ae-876f-eb3f224cd7d0","pokemonName":"liepard delta form","nickname":"Test","level":45,"rarity":"normal","isMega":false},null]}""");
        File.WriteAllText(Path.Combine(src, "zones", "z.json"),
            """{"zoneName":"Desert","sections":{"common":["natu"],"uncommon":[],"rare":[],"superRare":[]}}""");

        var lib = new UserLibrary(new AppPaths(Path.Combine(_dir, "home")));
        var report = WebsiteImporter.ImportPath(src, lib);
        Assert.Equal((1, 1, 1, 0), (report.Sheets, report.Teams, report.Zones, report.Skipped));

        var sheet = lib.Sheets.List().Single();
        Assert.Equal(45, sheet.State.Level);
        Assert.Equal(new[] { "Limber" }, sheet.State.BaseAbilities);
        Assert.Equal("liepard delta form", sheet.State.ChosenPokemon);
        var team = lib.Teams.List().Single();
        Assert.Equal(6, team.Slots.Count);
        Assert.Equal("45", team.Slots[0]!.Level);
        Assert.Equal("Desert", lib.Zones.List().Single().Name);
    }

    [Fact]
    public void Export_all_bundles_are_imported_back()
    {
        var source = new UserLibrary(new AppPaths(Path.Combine(_dir, "source")));
        var sheet = new SheetRecord { Nickname = "Sparky", PokemonName = "pikachu" };
        sheet.State.ChosenPokemon = "pikachu";
        sheet.State.HeldObject = "Light Ball";
        source.Sheets.Save(sheet);
        source.Sheets.Save(new SheetRecord { Nickname = "Leafy", PokemonName = "bulbasaur" });
        source.Teams.Save(new TeamRecord { Name = "Main" });
        source.Zones.Save(new ZoneRecord { Name = "Forest" });

        var bundles = Path.Combine(_dir, "bundles");
        Directory.CreateDirectory(bundles);
        File.WriteAllText(Path.Combine(bundles, "all-sheets.json"), LibraryExporter.ExportAll(source.Sheets));
        File.WriteAllText(Path.Combine(bundles, "all-teams.json"), LibraryExporter.ExportAll(source.Teams));
        File.WriteAllText(Path.Combine(bundles, "all-zones.json"), LibraryExporter.ExportAll(source.Zones));

        var target = new UserLibrary(new AppPaths(Path.Combine(_dir, "target")));
        var report = WebsiteImporter.ImportPath(bundles, target);
        Assert.Equal((2, 1, 1, 0), (report.Sheets, report.Teams, report.Zones, report.Skipped));
        var imported = target.Sheets.Get(sheet.Id)!;
        Assert.Equal("Light Ball", imported.State.HeldObject);
        Assert.Equal("Main", target.Teams.List().Single().Name);
        Assert.Equal("Forest", target.Zones.List().Single().Name);
    }

    [Fact]
    public void Sprites_are_plain_files_filled_from_the_defaults_and_replaceable()
    {
        var defaults = Path.Combine(_dir, "defaults", "images");
        Directory.CreateDirectory(defaults);
        File.WriteAllText(Path.Combine(defaults, "pikachu.png"), "default-pikachu");
        File.WriteAllText(Path.Combine(defaults, "eevee.png"), "default-eevee");
        File.WriteAllText(Path.Combine(defaults, "notes.txt"), "not a sprite");
        var images = Path.Combine(_dir, "data", "images");

        Assert.Equal(2, ImageResolver.SeedImages(images, new[] { defaults }));
        Assert.Equal(2, ImageResolver.CountImages(images));
        var resolver = new ImageResolver(images);
        Assert.Equal(Path.Combine(images, "pikachu.png"), resolver.Resolve("Pikachu"));
        Assert.Null(resolver.Resolve("charizard"));

        // The user replaces a sprite and adds a new one; seeding again keeps them.
        var mine = Path.Combine(_dir, "Charizard.PNG");
        File.WriteAllText(mine, "my-charizard");
        File.WriteAllText(Path.Combine(images, "pikachu.png"), "my-pikachu");
        Assert.Equal("charizard.png", ImageResolver.InstallImage(mine, images));
        Assert.Throws<InvalidDataException>(() => ImageResolver.InstallImage(Path.Combine(defaults, "notes.txt"), images));
        Assert.Equal(0, ImageResolver.SeedImages(images, new[] { defaults }));
        Assert.Equal("my-pikachu", File.ReadAllText(Path.Combine(images, "pikachu.png")));

        Assert.Null(resolver.Resolve("charizard"));          // cached miss…
        resolver.ClearCache();                               // …until the cache is cleared
        Assert.Equal(Path.Combine(images, "charizard.png"), resolver.Resolve("charizard"));

        // Restore defaults overwrites.
        Assert.Equal(2, ImageResolver.SeedImages(images, new[] { defaults }, overwrite: true));
        Assert.Equal("default-pikachu", File.ReadAllText(Path.Combine(images, "pikachu.png")));
    }

    [Fact]
    public void Data_files_live_in_the_data_folder_and_missing_files_are_reported()
    {
        var defaults = Path.Combine(_dir, "defaults");
        Directory.CreateDirectory(defaults);
        File.WriteAllText(Path.Combine(defaults, "pokemon.json"), """[{"name":"abc","stat_hp":"5","pokemon_types":"Fire","gender_ratio_m":"","custom_abilities":null,"mega_evolution":null,"weird_field":1}]""");
        File.WriteAllText(Path.Combine(defaults, "moves.json"), "[]");
        var dataDir = Path.Combine(_dir, "data");

        // abilities.json is missing everywhere: loading reports exactly what is missing.
        Assert.Equal(new[] { "pokemon.json", "moves.json" }, GameData.SeedDataFolder(dataDir, new[] { defaults, Path.Combine(_dir, "nowhere") }));
        var ex = Assert.Throws<MissingDataException>(() => GameData.Load(dataDir));
        Assert.Equal(new[] { "abilities.json" }, ex.Missing);
        Assert.Equal(dataDir, ex.DataDir);

        File.WriteAllText(Path.Combine(_dir, "abilities.json"), """[{"name":"Blaze","effect":"x"}]""");
        GameData.InstallDataFile(Path.Combine(_dir, "abilities.json"), dataDir);
        var data = GameData.Load(dataDir);
        Assert.Equal(dataDir, data.DataDir);

        // Pokédex edits are written to the data folder's pokemon.json, unknown fields kept.
        var edited = data.FindPokemon("abc")!.DeepClone();
        edited.StatHp = 9;
        data.SaveSpecies("abc", edited);
        Assert.Equal(9, GameData.Load(dataDir).FindPokemon("abc")!.StatHp);
        Assert.Contains("weird_field", File.ReadAllText(Path.Combine(dataDir, "pokemon.json")));
        Assert.Contains("\"stat_hp\": \"5\"", File.ReadAllText(Path.Combine(defaults, "pokemon.json")).Replace("\"stat_hp\":\"5\"", "\"stat_hp\": \"5\"")); // defaults untouched
    }
}

public class MarkdownImportTests : IClassFixture<DataFixture>
{
    private readonly GameData _data;
    public MarkdownImportTests(DataFixture fixture) => _data = fixture.Data;

    [Fact]
    public void Markdown_exported_by_the_app_round_trips()
    {
        var sp = _data.FindPokemon("charmander")!;
        var state = SheetRules.NewSheet(sp);
        state.Level = 30;
        state.Rarity = "Shiny";
        state.Card = "Normal";
        state.Gender = "Male";
        state.BuffedStat = "SPATK";
        state.LoweredStat = "DEF";
        state.PointsByStat = SheetRules.ComputePointsAutomatically(sp, SheetRules.AvailablePoints(30, state.PointsByStat), new Random(3));
        state.BaseAbilities.Add(sp.BaseAbilities[0].Name);
        state.AdvancedAbilities.Add(sp.AdvancedAbilities[0].Name);
        state.ChosenMoves = SheetRules.AvailableLevelUpMoves(sp, 30).Select(m => _data.FindMove(m.Name)).Where(m => m != null)
            .Select(m => m!.Name).Distinct().Take(7).ToList();
        var ember = state.ChosenMoves.Select(n => _data.FindMove(n)!).First(m => m.DamageBase > 0);
        state.DbBuffByMove[ember.Name] = 2;
        state.EggMoves = _data.ResolveMoves(_data.EffectiveEggMoves(sp)).Take(2).Select(m => m.Name).ToList();
        state.LockedEggMoves.Add(state.EggMoves[0]);
        state.TutorPointsAdjustment = -2;
        state.TutorNotes = "Spent on Fire Punch\nSaving 1";
        state.HeldObject = "Charcoal";
        state.Loyalty = 7;

        var md = SheetExporter.ToMarkdown(SheetSnapshot.Create(state, sp, _data, "Line one\nLine two", "Blaze"));
        var result = MarkdownSheetImporter.Parse(md, _data, "charmander.md");
        var s2 = result.Sheet.State;

        Assert.Empty(result.Warnings);
        Assert.Equal("Blaze", result.Sheet.Nickname);
        Assert.Equal("charmander", s2.ChosenPokemon);
        Assert.Equal((30, "Shiny", "Normal", "Male", "SPATK", "DEF"), (s2.Level, s2.Rarity, s2.Card, s2.Gender, s2.BuffedStat, s2.LoweredStat));
        Assert.Equal(state.PointsByStat.OrderBy(k => k.Key), s2.PointsByStat.OrderBy(k => k.Key));
        Assert.Equal(state.BaseAbilities, s2.BaseAbilities);
        Assert.Equal(state.AdvancedAbilities, s2.AdvancedAbilities);
        Assert.Equal(state.ChosenMoves, s2.ChosenMoves);
        Assert.Equal(state.LockedEggMoves, s2.LockedEggMoves);
        Assert.Equal(state.EggMoves, s2.EggMoves);
        Assert.Equal(2, s2.DbBuffByMove[ember.Name]);
        Assert.Equal("Line one\nLine two", result.Sheet.Notes);
        Assert.Equal(-2, s2.TutorPointsAdjustment);
        Assert.Equal("Spent on Fire Punch\nSaving 1", s2.TutorNotes);
        Assert.Equal("Charcoal", s2.HeldObject);
        Assert.Equal(7, s2.Loyalty);
        Assert.True(SheetRules.IsComplete(s2));
    }

    [Fact]
    public void Markdown_generated_by_the_website_is_imported()
    {
        var sp = _data.FindPokemon("bulbasaur")!;
        var tackle = _data.FindMove("Tackle")!;
        var vine = _data.FindMove("Vine Whip")!;
        var egg = _data.ResolveMoves(_data.EffectiveEggMoves(sp)).First();
        // Website table: STAB already applied, +1 DB buff on Vine Whip.
        string Roll(MoveData m, int buff)
        {
            var expected = SheetRules.UpdatedRoll(sp, new Dictionary<string, int>(), m);
            var db = SheetRules.DamageBaseToRoll.First(kv => kv.Value == expected).Key;
            return SheetRules.DamageBaseToRoll[db + buff];
        }
        string Row(MoveData m, string roll) =>
            $"|{m.Name}|{m.Frequency}|{m.AC}|{string.Join(" /", m.Types)}|{roll}|Phys|{m.Range}|{m.Effect}|\n";

        var md = new StringBuilder();
        md.Append("![](http://faeresdev.site/images/bulbasaur.png)\n# Bulbasaur\nCard: Normal\nGender: Female\nItem: \n");
        md.Append("Nature: Bold (+DEF/-ATK)\nLevel: 22\nType: Grass,Poison\nRarity: Normal\nWeight: 15\nHeight: 2\n\n");
        md.Append("## **Abilities**\n\n| **Ability** | **Effect** |\n| ---------------- | ------------------ |\n");
        md.Append("| Overgrow |  Static\nEffect: a multi-line effect | \n| Confidence |  Something}} | \n\n\n");
        md.Append("## **Capabilities**\n\n| **Capability** | **Value** |\n| --- | --- |\n| Overland | 5 | \n\n");
        md.Append("## **Stats**\n\n| **Hit Points Max :XX** | **Hit Points: XX/XX** |\n|---|---|\n");
        md.Append("| **Max HP**: 5+1+4=10 | **Current HP**:10 |\n| **Max ATK**: 5-1+3=7 | **Current ATK**:7 |\n");
        md.Append("| **Max DEF**: 5+5+6=16 | x |\n| **Max SPATK**: 7+1+8=16 | x |\n| **Max SPDEF**: 7+1+6=14 | x |\n| **Max SPEED**: 5+1+4=10 | x |\n\n");
        md.Append("## **Moves** :\n\n| **Move** | **Freq** | **AC** | **Type** | **Roll** | **Dmg. Type** | **Range** | **Special Effect** |\n|---|---|---|---|---|---|---|---|\n");
        md.Append(Row(vine, Roll(vine, 1))).Append(Row(tackle, Roll(tackle, 0))).Append(Row(egg, egg.Roll ?? ""));
        md.Append("|               |   |   |   |   |   |   |   |\n|               |   |   |   |   |   |   |   |\n");
        md.Append("|               |   |   |   |   |   |   |   |\n\n| **Move** | **Freq** |\n|---------------|---|\n");
        md.Append("|               |   |   |   |   |   |   |   |\n\nTutor points = 4\n\n\n## **Notes**\n\n");
        md.Append($"egg moves : {egg.Name}\nTM moves : None\ntutor moves : None\n\n\nGrumpy in the morning.");

        var result = MarkdownSheetImporter.Parse(md.ToString(), _data, "bulbasaur.md");
        var s = result.Sheet.State;

        Assert.Empty(result.Warnings);
        Assert.Equal("Bulbasaur", result.Sheet.Nickname);
        Assert.Equal((22, "Normal", "Normal", "Female", "DEF", "ATK"), (s.Level, s.Rarity, s.Card, s.Gender, s.BuffedStat, s.LoweredStat));
        Assert.Equal(new[] { 4, 3, 6, 8, 6, 4 }, SheetRules.StatNames.Select(k => s.PointsByStat[k]));
        Assert.Equal(new[] { "Overgrow" }, s.BaseAbilities);
        Assert.Equal(new[] { "Confidence" }, s.AdvancedAbilities);
        Assert.Equal(new[] { "Vine Whip", "Tackle" }, s.ChosenMoves);
        Assert.Equal(new[] { egg.Name }, s.LockedEggMoves);
        Assert.Equal(1, s.DbBuffByMove["Vine Whip"]);
        Assert.False(s.DbBuffByMove.ContainsKey("Tackle"));
        Assert.Equal("Grumpy in the morning.", result.Sheet.Notes);
        Assert.True(SheetRules.IsComplete(s));
    }

    [Fact]
    public void Hand_edited_markdown_sheets_are_imported()
    {
        var md = new StringBuilder();
        md.Append("# Bulbasaur\n\nCard : None\n\nGender : Female\n\nNature : Timid (+ SPD / - ATK)\n\nLevel : 12,5\n\nRarity : Normal\n\n");
        md.Append("## **Abilities**\n\n| **Ability** | **Effect** |\n| --- | --- |\n| Overgrow | Static |\n");
        md.Append("| Keywords - Defensive |  |\n| Trigger: the user is hit by a Melee Attack |  |\n\n");
        md.Append("## **Stats**\n\n| **Hit Points Max :XX** | x |\n| --- | --- |\n");
        md.Append("| **Max HP**: 5+4=9 | x |\n| **Max Atk :** 5-2+3=6 | x |\n| **Max Def**: 5+3=8 | x |\n");
        md.Append("| **Max SpAtk**: 7+4=11 | x |\n| **Max SpDef**: 7+3=10 | x |\n| **Max Speed**: 5+2+4 = **11** | x |\n\n");
        md.Append("**Attack&#xA;**\n\nStab deja mis\n\n| Move | Freq | AC | Type | Roll |\n| --- | --- | --- | --- | --- |\n");
        md.Append("| **Vine whip** | At-Will | 2 | Grass | 1d8+6 |\n| Lashes the target. |  |  |  |  |\n| Tackle | At-Will | 2 | Normal | 1d6+5 |\n\n");
        md.Append("## **Notes**\n\negg move : todo\n");

        var result = MarkdownSheetImporter.Parse(md.ToString(), _data, "bulbasaur.md");
        var s = result.Sheet.State;

        Assert.Equal((12.5, "None", "Female", "SPD", "ATK"), (s.Level, s.Card, s.Gender, s.BuffedStat, s.LoweredStat));
        Assert.Equal(new[] { 4, 3, 3, 4, 3, 4 }, SheetRules.StatNames.Select(k => s.PointsByStat[k]));
        Assert.Equal(0, SheetRules.AvailablePoints(s.Level, s.PointsByStat));
        Assert.Equal(new[] { "Overgrow" }, s.BaseAbilities);
        Assert.Equal(new[] { "Vine Whip", "Tackle" }, s.ChosenMoves);
        Assert.Contains("Stab deja mis", result.Sheet.Notes);
        Assert.Contains("egg move : todo", result.Sheet.Notes);
    }

    [Fact]
    public void Markdown_files_are_imported_from_a_folder_and_bad_files_reported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "prs-md-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var sp = _data.FindPokemon("pikachu")!;
            var state = SheetRules.NewSheet(sp);
            state.Level = 5; state.Gender = "Male"; state.BuffedStat = "SPD"; state.LoweredStat = "HP";
            state.PointsByStat = SheetRules.ComputePointsAutomatically(sp, 14, new Random(1));
            File.WriteAllText(Path.Combine(dir, "pikachu.md"), SheetExporter.ToMarkdown(SheetSnapshot.Create(state, sp, _data)));
            File.WriteAllText(Path.Combine(dir, "readme.md"), "# Just a readme");
            var lib = new UserLibrary(new AppPaths(Path.Combine(dir, "home")));

            var report = WebsiteImporter.ImportPath(dir, lib, _data);
            Assert.Equal(1, report.Sheets);
            Assert.Equal(1, report.Skipped);
            Assert.Contains(report.AllWarnings, w => w.StartsWith("readme.md"));
            Assert.Equal("pikachu", lib.Sheets.List().Single().PokemonName);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class SheetSortingTests
{
    private static SheetRecord Sheet(string nick, string pokemon, int level, string rarity, int updatedDaysAgo, int createdDaysAgo) => new()
    {
        Nickname = nick,
        PokemonName = pokemon,
        State = new SheetState { Level = level, Rarity = rarity },
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-updatedDaysAgo),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-createdDaysAgo),
    };

    private static readonly SheetRecord[] All =
    {
        Sheet("blaze", "charmander", 25, "Shiny", 1, 10),
        Sheet("Aqua", "squirtle", 40, "Normal", 5, 3),
        Sheet("Zap", "pikachu", 12, "Platine", 0, 20),
        Sheet("Bulby", "bulbasaur", 40, "Normal", 3, 1),
    };

    private static string[] Names(SheetSortKey key, bool desc) => SheetSorting.Sort(All, key, desc).Select(s => s.Nickname).ToArray();

    [Fact]
    public void Sorts_by_every_key_in_both_directions()
    {
        Assert.Equal(new[] { "Aqua", "blaze", "Bulby", "Zap" }, Names(SheetSortKey.Nickname, false)); // case-insensitive
        Assert.Equal(new[] { "Zap", "Bulby", "blaze", "Aqua" }, Names(SheetSortKey.Nickname, true));
        Assert.Equal(new[] { "Bulby", "blaze", "Zap", "Aqua" }, Names(SheetSortKey.Pokemon, false));
        Assert.Equal(new[] { "Aqua", "Bulby", "blaze", "Zap" }, Names(SheetSortKey.Level, true));     // tie broken by nickname
        Assert.Equal(new[] { "Zap", "blaze", "Aqua", "Bulby" }, Names(SheetSortKey.Rarity, true));    // Platine > Shiny > Normal
        Assert.Equal(new[] { "Zap", "blaze", "Bulby", "Aqua" }, Names(SheetSortKey.Updated, true));
        Assert.Equal(new[] { "Zap", "blaze", "Aqua", "Bulby" }, Names(SheetSortKey.Created, false));
    }

    [Fact]
    public void Numbers_and_dates_default_to_descending()
    {
        Assert.False(SheetSorting.DefaultDescending(SheetSortKey.Nickname));
        Assert.True(SheetSorting.DefaultDescending(SheetSortKey.Level));
        Assert.True(SheetSorting.DefaultDescending(SheetSortKey.Updated));
    }
}

public class InjuryTests
{
    [Fact]
    public void One_injury_per_quarter_of_max_hp_lost()
    {
        Assert.Equal(0, InjuryRules.DamageInjuries(100, 100));
        Assert.Equal(0, InjuryRules.DamageInjuries(100, 76));
        Assert.Equal(1, InjuryRules.DamageInjuries(100, 75));   // 25 % lost
        Assert.Equal(2, InjuryRules.DamageInjuries(100, 50));   // 50 % lost
        Assert.Equal(3, InjuryRules.DamageInjuries(100, 25));
        Assert.Equal(3, InjuryRules.DamageInjuries(100, 1));
        Assert.Equal(4, InjuryRules.DamageInjuries(100, 0));    // 100 % lost
        Assert.Equal(5, InjuryRules.DamageInjuries(100, -25));
        Assert.Equal(1, InjuryRules.DamageInjuries(69, 51));    // odd max: 18 lost ≥ 17.25
        Assert.Equal(0, InjuryRules.DamageInjuries(69, 52));
        Assert.Equal(75, InjuryRules.InjuryThreshold(100, 1));
        Assert.Equal(0, InjuryRules.InjuryThreshold(100, 4));
    }

    [Fact]
    public void Each_injury_removes_a_tenth_of_the_max_hp()
    {
        Assert.Equal(100, InjuryRules.MaxHpWithInjuries(100, 0));
        Assert.Equal(90, InjuryRules.MaxHpWithInjuries(100, 1));
        Assert.Equal(61, InjuryRules.MaxHpWithInjuries(68, 1));  // 61.2 rounded down
        Assert.Equal(0, InjuryRules.MaxHpWithInjuries(100, 10));
        Assert.Equal(0, InjuryRules.MaxHpWithInjuries(100, 14));
    }

    [Fact]
    public void Massive_damage_is_a_hit_of_more_than_half_the_max_hp()
    {
        Assert.False(InjuryRules.IsMassiveDamage(100, 50));
        Assert.True(InjuryRules.IsMassiveDamage(100, 51));
        Assert.True(InjuryRules.IsMassiveDamage(69, 35));        // 35 > 34.5
        Assert.False(InjuryRules.IsMassiveDamage(69, 34));
        Assert.False(InjuryRules.IsMassiveDamage(0, 10));
    }

    [Fact]
    public void Damage_adds_injuries_and_healing_never_removes_them()
    {
        var hp = InjuryRules.FullHeal(100, 0);
        Assert.Equal((100, 0, 100), (hp.CurrentHp, hp.Injuries, hp.MaxHp));

        hp = InjuryRules.Damage(hp, 30);                 // 70 HP: past the 75 marker
        Assert.Equal((70, 1, 90), (hp.CurrentHp, hp.Injuries, hp.MaxHp));
        Assert.Equal(50, hp.NextInjuryAt);

        hp = InjuryRules.Damage(hp, 25);                 // 45 HP: past 50 (not massive: two hits)
        Assert.Equal((45, 2, 80), (hp.CurrentHp, hp.Injuries, hp.MaxHp));

        hp = InjuryRules.Damage(hp, 50);                 // −5 HP: past 25 and 0
        Assert.Equal((-5, 4, 60), (hp.CurrentHp, hp.Injuries, hp.MaxHp));
        Assert.True(hp.IsFainted);

        hp = InjuryRules.Heal(hp, 1000);                 // the injuries stay: HP capped to the injured max
        Assert.Equal((60, 4, 60), (hp.CurrentHp, hp.Injuries, hp.MaxHp));
        Assert.Equal(60, InjuryRules.FullHeal(100, hp.Injuries).CurrentHp);
    }

    [Fact]
    public void Passing_a_marker_again_after_healing_gives_another_injury()
    {
        var hp = InjuryRules.Damage(InjuryRules.FullHeal(100, 0), 30);   // 70: +1
        hp = InjuryRules.Heal(hp, 10);                                    // 80, still 1 injury
        Assert.Equal((80, 1), (hp.CurrentHp, hp.Injuries));
        hp = InjuryRules.Damage(hp, 4);                                   // 76: no marker passed
        Assert.Equal(1, hp.Injuries);
        hp = InjuryRules.Damage(hp, 1);                                   // 75: the 75 marker again
        Assert.Equal(2, hp.Injuries);
    }

    [Fact]
    public void Massive_damage_adds_an_injury()
    {
        var hp = InjuryRules.Damage(InjuryRules.FullHeal(100, 0), 60);   // one big hit: 40 HP, 2 markers + massive
        Assert.Equal((40, 3, 70), (hp.CurrentHp, hp.Injuries, hp.MaxHp));
        hp = InjuryRules.Heal(hp, 1000);
        Assert.Equal((70, 3, 70), (hp.CurrentHp, hp.Injuries, hp.MaxHp));
    }

    [Fact]
    public void Injuries_are_removed_by_hand_only()
    {
        var hp = InjuryRules.FullHeal(100, 2);
        Assert.Equal((80, 2, 80), (hp.CurrentHp, hp.Injuries, hp.MaxHp));
        hp = InjuryRules.Heal(InjuryRules.Damage(hp, 50), 500);   // 30 HP: past the 75 and 50 markers, not massive
        Assert.Equal((60, 4, 60), (hp.CurrentHp, hp.Injuries, hp.MaxHp));

        hp = InjuryRules.WithInjuries(hp, hp.Injuries - 1);       // removed by hand: max back up, HP unchanged
        Assert.Equal((60, 3, 70), (hp.CurrentHp, hp.Injuries, hp.MaxHp));

        // Capping the HP to a lower max (many injuries) is not damage: no extra injury.
        hp = InjuryRules.FullHeal(100, 6);
        Assert.Equal((40, 6, 40), (hp.CurrentHp, hp.Injuries, hp.MaxHp));
    }

    [Fact]
    public void Play_state_keeps_the_injuries_of_older_saves()
    {
        // Older save: only the massive / hand-added injury is stored, the others came from the HP.
        var play = new SheetPlayState { MaxHpModifier = 10, CurrentHp = 30, Injuries = 1 };
        var hp = play.HpStatus(50);                      // max 60 with temp; 30 lost, steps of 12.5 (25 % of 50) → 2
        Assert.Equal((60, 3, 42), (hp.BaseMaxHp, hp.Injuries, hp.MaxHp));
        Assert.Equal(30, hp.CurrentHp);

        // New saves store every injury: nothing is derived from the HP.
        play.InjuriesIncludeDamage = true;
        Assert.Equal(1, play.HpStatus(50).Injuries);
        Assert.Equal(60, new SheetPlayState().HpStatus(60).CurrentHp);
    }

    [Fact]
    public void Injury_steps_are_25_percent_of_the_sheet_max_hp()
    {
        // Sheet max 100, temporary −40: max 60, but each marker is still 25 HP apart (25 % of 100).
        var hp = new SheetPlayState { MaxHpModifier = -40 }.HpStatus(100);
        Assert.Equal((60, 60, 0, 100), (hp.BaseMaxHp, hp.CurrentHp, hp.Injuries, hp.RealMaxHp));
        Assert.Equal(35, hp.NextInjuryAt);
        hp = InjuryRules.Damage(hp, 24);                  // 36 HP: 24 lost, under 25
        Assert.Equal(0, hp.Injuries);
        hp = InjuryRules.Damage(hp, 1);                   // 35 HP: 25 lost
        Assert.Equal(1, hp.Injuries);
    }
}

public class CatalogTests : IClassFixture<DataFixture>
{
    private readonly GameData _real;
    public CatalogTests(DataFixture fixture) => _real = fixture.Data;

    private static GameCatalog Small()
    {
        var moves = new[]
        {
            new MoveData { Name = "Ember", Types = new() { "Fire" }, Frequency = "At-Will", AC = "2", DamageBase = 4, MoveClass = "Special", Range = "4, 1 Target", Effect = "Burns on 18+" },
            new MoveData { Name = "Flamethrower", Types = new() { "Fire" }, Frequency = "Scene x2", AC = "2", DamageBase = 9, MoveClass = "Special", Effect = "Burns on 19+" },
            new MoveData { Name = "Growl", Types = new() { "Normal" }, Frequency = "Daily x1", AC = "/", DamageBase = 0, MoveClass = "Status", Effect = "Lowers attack" },
            new MoveData { Name = "Tackle", Types = new() { "Normal" }, Frequency = "EOT", AC = "2", DamageBase = 5, MoveClass = "Physical" },
        };
        var abilities = new[]
        {
            new AbilityData { Name = "Blaze", Effect = "Static\nEffect: fire moves +5 damage." },
            new AbilityData { Name = "Run Away", Effect = "Scene x2 – Free Action\nEffect: flee." },
            new AbilityData { Name = "Unused", Effect = "Daily x2 - Swift Action" },
        };
        var charmander = new PokemonSpecies
        {
            Name = "Charmander",
            Moves = new() { new LearnsetMove { Name = "Ember", Level = 7 }, new LearnsetMove { Name = "growl", Level = 1 } },
            TmMoves = new() { new LearnsetMove { Name = "Flamethrower" } },
            BaseAbilities = new() { new AbilityRef("Blaze") },
        };
        var rattata = new PokemonSpecies
        {
            Name = "Rattata",
            Moves = new() { new LearnsetMove { Name = "Tackle", Level = 1 }, new LearnsetMove { Name = "Growl", Level = 4 } },
            BaseAbilities = new() { new AbilityRef("Run Away") },
            HighAbilities = new() { new AbilityRef("Blaze") },
        };
        return new GameCatalog(GameData.FromLists(new[] { charmander, rattata }, moves, abilities));
    }

    [Theory]
    [InlineData("Daily x1", "Daily")]
    [InlineData("2x Daily", "Daily x2")]
    [InlineData("Scene x1", "Scene")]
    [InlineData("At Will - Free Action", "At-Will")]
    [InlineData("\"Static", "Static")]
    [InlineData("Scene x2 – Swift Action", "Scene x2")]
    [InlineData("Daily x1 Quick Action", "Daily")]
    [InlineData("TBD", "Other")]
    public void Frequencies_are_grouped(string raw, string expected) => Assert.Equal(expected, GameCatalog.NormalizeFrequency(raw));

    [Fact]
    public void Frequencies_sort_most_usable_first() =>
        Assert.Equal(new[] { "At-Will", "EOT", "Scene x2", "Scene", "Daily x2", "Daily", "Other" },
            GameCatalog.SortFrequencies(new[] { "Daily", "Other", "Scene", "EOT", "Daily x2", "At-Will", "Scene x2" }));

    [Fact]
    public void Learners_and_holders_are_indexed()
    {
        var c = Small();
        Assert.Equal(new[] { "Charmander:Lv 1", "Rattata:Lv 4" }, c.LearnersOf("Growl").Select(l => $"{l.Pokemon}:{l.Label}").OrderBy(s => s));
        Assert.Equal("TM", c.LearnersOf("flamethrower").Single().Method);
        Assert.Equal(2, c.HolderCount("Blaze"));
        Assert.Equal(new[] { "Base", "High" }, c.HoldersOf("Blaze").Select(h => h.Tier).OrderBy(t => t));
        Assert.Empty(c.HoldersOf("Unused"));
    }

    [Fact]
    public void Moves_are_filtered()
    {
        var c = Small();
        string[] Names(MoveFilter f) => c.FilterMoves(f).Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "Ember", "Flamethrower" }, Names(new MoveFilter { Type = "fire" }));
        Assert.Empty(Names(new MoveFilter { Text = "burn" }));
        Assert.Equal(new[] { "Ember", "Flamethrower" }, Names(new MoveFilter { Text = "burn", SearchEffects = true }));
        Assert.Equal(new[] { "Growl" }, Names(new MoveFilter { MoveClass = "Status" }));
        Assert.Equal(new[] { "Growl" }, Names(new MoveFilter { Frequency = "Daily" }));
        Assert.Equal(new[] { "Ember", "Flamethrower", "Growl" }, Names(new MoveFilter { LearnableBy = "charmander" }));
        Assert.Equal(new[] { "Growl" }, Names(new MoveFilter { LearnableBy = "Charmander", Type = "Normal" }));
    }

    [Fact]
    public void Moves_are_sorted_with_missing_values_last()
    {
        var c = Small();
        var all = c.FilterMoves(new MoveFilter()).ToList();
        string[] Sort(MoveSortKey k, bool d) => c.SortMoves(all, k, d).Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "Tackle", "Growl", "Flamethrower", "Ember" }, Sort(MoveSortKey.Name, true));
        Assert.Equal(new[] { "Flamethrower", "Tackle", "Ember", "Growl" }, Sort(MoveSortKey.DamageBase, true));
        Assert.Equal(new[] { "Ember", "Tackle", "Flamethrower", "Growl" }, Sort(MoveSortKey.DamageBase, false));
        Assert.Equal("Growl", Sort(MoveSortKey.AC, true).Last());
        Assert.Equal(new[] { "Ember", "Tackle", "Flamethrower", "Growl" }, Sort(MoveSortKey.Frequency, false));
        Assert.Equal("Growl", Sort(MoveSortKey.Learners, true).First());
    }

    [Fact]
    public void Abilities_are_filtered_and_sorted()
    {
        var c = Small();
        string[] Names(AbilityFilter f, AbilitySortKey k = AbilitySortKey.Name, bool d = false) =>
            c.SortAbilities(c.FilterAbilities(f), k, d).Select(a => a.Name).ToArray();
        Assert.Equal(new[] { "Blaze", "Run Away" }, Names(new AbilityFilter { OnlyUsed = true }));
        Assert.Equal(new[] { "Blaze", "Run Away" }, Names(new AbilityFilter { Pokemon = "Rattata" }));
        Assert.Equal(new[] { "Run Away" }, Names(new AbilityFilter { Frequency = "Scene x2" }));
        Assert.Equal(new[] { "Run Away" }, Names(new AbilityFilter { Text = "flee", SearchEffects = true }));
        Assert.Equal(new[] { "Blaze", "Run Away", "Unused" }, Names(new AbilityFilter(), AbilitySortKey.Holders, true));
        Assert.Equal(new[] { "Blaze", "Run Away", "Unused" }, Names(new AbilityFilter(), AbilitySortKey.Frequency));
    }

    [Fact]
    public void Real_data_catalog_covers_learnsets()
    {
        var c = new GameCatalog(_real);
        Assert.True(_real.Moves.Count(m => c.LearnerCount(m.Name) > 0) > _real.Moves.Count / 2);
        Assert.True(_real.Abilities.Count(a => c.HolderCount(a.Name) > 0) > 100);
        var sp = _real.Pokemon.First(p => p.Moves.Count > 0);
        var learnable = c.FilterMoves(new MoveFilter { LearnableBy = sp.Name }).Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(_real.FindMove(sp.Moves.First(m => _real.FindMove(m.Name) != null).Name)!.Name, learnable);
    }
}

public class SheetSearchAndDbTests
{
    private static PokemonSpecies Fire() => new() { Name = "charmander", PokemonTypes = new() { "Fire" } };

    private static SheetRecord Sheet() => new()
    {
        Nickname = "Flamèche",
        PokemonName = "charmander",
        Notes = "Caught in Lavender Town",
        State = new SheetState { ChosenPokemon = "charmander", Level = 12, ChosenMoves = new() { "Ember", "Scratch" }, BaseAbilities = new() { "Blaze" } },
        Play = new SheetPlayState { Notes = { ["hp"] = "Burned by Willow" } },
    };

    [Theory]
    [InlineData("flameche")]          // accents ignored
    [InlineData("FIRE")]              // type from the species
    [InlineData("ember")]             // move
    [InlineData("blaze")]             // ability
    [InlineData("lavender")]          // sheet notes
    [InlineData("willow")]            // session notes
    [InlineData("fire  scratch")]     // every word must match
    [InlineData("")]
    public void Search_finds_sheet_fields(string query) =>
        Assert.True(SheetSearch.Matches(SheetSearch.BuildIndex(Sheet(), Fire()), query));

    [Theory]
    [InlineData("water")]
    [InlineData("fire tackle")]
    public void Search_rejects_missing_terms(string query) =>
        Assert.False(SheetSearch.Matches(SheetSearch.BuildIndex(Sheet(), Fire()), query));

    [Fact]
    public void Status_moves_ignore_db_buff()
    {
        var gleam = new MoveData { Name = "Gleam", Types = new() { "Normal" }, MoveClass = "Status", DamageBase = -1, Roll = "2d8+10" };
        Assert.False(SheetRules.CanHaveDbBuff(gleam));
        var buffs = new Dictionary<string, int> { ["Gleam"] = 3 };
        Assert.Equal(SheetRules.UpdatedRoll(Fire(), new Dictionary<string, int>(), gleam), SheetRules.UpdatedRoll(Fire(), buffs, gleam));
        Assert.True(SheetRules.CanHaveDbBuff(new MoveData { Name = "Ember", MoveClass = "Special" }));
    }
}

public sealed class SheetFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "prs-folders-" + Guid.NewGuid());
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Paths_are_normalized_and_sorted_as_a_tree()
    {
        Assert.Equal("Campaign/Fire team", SheetFolders.Normalize(" Campaign / /Fire team \\"));
        Assert.True(SheetFolders.IsInside("Campaign/Fire", "campaign"));
        Assert.False(SheetFolders.IsInside("Campaigns", "Campaign"));
        Assert.True(SheetFolders.IsInside("", ""));
        Assert.Equal(new[] { "A", "A/B", "A/B/C", "Z" }, SheetFolders.All(new[] { "A/B/C", "", null }, new[] { "Z" }));
    }

    [Fact]
    public void Rename_and_delete_move_sheets_and_subfolders()
    {
        var lib = new UserLibrary(new AppPaths(_dir));
        var settings = new AppSettings();
        var a = lib.Sheets.Save(new SheetRecord { Nickname = "A", Folder = "Campaign/Fire" });
        var b = lib.Sheets.Save(new SheetRecord { Nickname = "B", Folder = "Campaign/Fire/Reserve" });
        var c = lib.Sheets.Save(new SheetRecord { Nickname = "C", Folder = "Other" });
        SheetFolders.Create(settings, "Campaign/Fire", "Empty");

        Assert.Equal("Campaign/Flames", SheetFolders.RenameFolder(lib.Sheets, settings, "Campaign/Fire", "Flames"));
        Assert.Equal("Campaign/Flames", lib.Sheets.Get(a.Id)!.Folder);
        Assert.Equal("Campaign/Flames/Reserve", lib.Sheets.Get(b.Id)!.Folder);
        Assert.Contains("Campaign/Flames/Empty", settings.SheetFolders);
        Assert.Throws<InvalidOperationException>(() => SheetFolders.Move(lib.Sheets, settings, "Campaign", "Campaign/Flames"));

        SheetFolders.Delete(lib.Sheets, settings, "Campaign");          // contents go to the top level
        Assert.Equal("Flames", lib.Sheets.Get(a.Id)!.Folder);
        Assert.Equal("Flames/Reserve", lib.Sheets.Get(b.Id)!.Folder);
        Assert.Equal("Other", lib.Sheets.Get(c.Id)!.Folder);
        Assert.Equal(3, lib.Sheets.List().Count);                       // no sheet deleted
    }

    [Fact]
    public void Zone_folders_are_separate_from_sheet_folders()
    {
        var lib = new UserLibrary(new AppPaths(_dir));
        var settings = new AppSettings();
        var forest = lib.Zones.Save(new ZoneRecord { Name = "Forest", Folder = "Kanto/Routes" });
        var sheet = lib.Sheets.Save(new SheetRecord { Nickname = "A", Folder = "Kanto/Routes" });
        SheetFolders.Create(settings.ZoneFolders, "Kanto", "Caves");

        Assert.Equal(new[] { "Kanto", "Kanto/Caves", "Kanto/Routes" }, SheetFolders.All(lib.Zones.List(), settings.ZoneFolders));
        Assert.Equal("Johto/Routes", SheetFolders.RenameFolder(lib.Zones, settings.ZoneFolders, "Kanto", "Johto") + "/Routes");
        Assert.Equal("Johto/Routes", lib.Zones.Get(forest.Id)!.Folder);
        Assert.Contains("Johto/Caves", settings.ZoneFolders);
        Assert.Equal("Kanto/Routes", lib.Sheets.Get(sheet.Id)!.Folder);  // sheets untouched
        Assert.Empty(settings.SheetFolders);

        SheetFolders.Delete(lib.Zones, settings.ZoneFolders, "Johto");
        Assert.Equal("Routes", lib.Zones.Get(forest.Id)!.Folder);
    }
}

public class SheetValidationTests
{
    private static GameData Data()
    {
        LearnsetMove L(string n, int lv = -1) => new() { Name = n, Level = lv };
        var basic = new PokemonSpecies
        {
            Name = "snivy", PokemonTypes = new() { "Grass" }, GenderRatioM = 87.5,
            BaseAbilities = new() { new AbilityRef { Name = "Overgrow" } },
            Moves = new() { L("Tackle", 1), L("Mega Drain", 12) },
            Evolutions = new() { "Snivy", "Servine Minimum 17" },
        };
        var evolved = new PokemonSpecies
        {
            Name = "servine", PokemonTypes = new() { "Grass" }, GenderRatioM = 87.5,
            BaseAbilities = new() { new AbilityRef { Name = "Overgrow" } },
            HighAbilities = new() { new AbilityRef { Name = "Contrary" } },
            Moves = new() { L("Tackle", 1), L("Leaf Tornado", 30) },
            TmMoves = new() { L("Giga Drain") },
            Evolutions = new() { "Snivy", "Servine Minimum 17" },
        };
        var moves = new[] { "Tackle", "Mega Drain", "Leaf Tornado", "Giga Drain" }.Select(n => new MoveData { Name = n, MoveClass = "Special" });
        return GameData.FromLists(new[] { basic, evolved }, moves, Array.Empty<AbilityData>());
    }

    private static SheetState Valid() => new()
    {
        ChosenPokemon = "servine", Level = 20, Rarity = "Normal", Card = "None", Gender = "Male",
        BuffedStat = "ATK", LoweredStat = "DEF", PointsByStat = new() { ["HP"] = 29 },
        BaseAbilities = new() { "Overgrow" },
        ChosenMoves = new() { "Tackle", "Mega Drain" },   // Mega Drain: learned as Snivy
        TmMoves = new() { "Giga Drain" },
    };

    [Fact]
    public void A_valid_sheet_has_no_issue() => Assert.Empty(SheetValidation.Check(Valid(), Data()).All);

    [Fact]
    public void Moves_and_abilities_taken_early_are_not_errors()
    {
        var s = Valid();                               // level 20
        s.HighAbilities = new() { "Contrary" };        // high ability before level 40 (tutor)
        s.ChosenMoves.Add("Leaf Tornado");             // level-up move learned at 30 (tutor)
        Assert.False(SheetValidation.Check(s, Data()).HasIssues);
    }

    [Fact]
    public void Extra_abilities_are_not_errors_and_are_exported()
    {
        var s = Valid();
        s.ExtraAbilities = new() { "Damp", "Levitate" };   // not Servine abilities, added by hand
        Assert.False(SheetValidation.Check(s, Data()).HasIssues);

        var data = Data();
        var snap = SheetSnapshot.Create(s, data.FindPokemon("servine")!, data);
        Assert.Equal(new[] { "Overgrow", "Damp", "Levitate" }, snap.Abilities.Select(a => a.Name));

        var json = System.Text.Json.JsonSerializer.Serialize(s, JsonDefaults.Compact);
        Assert.Equal(s.ExtraAbilities, s.Clone().ExtraAbilities);
        Assert.Contains("pokemon_extra_ability", json);
    }

    [Fact]
    public void Unfinished_and_incorrect_sheets_are_reported()
    {
        var s = Valid();
        s.Gender = "";
        var issues = SheetValidation.Check(s, Data());
        Assert.True(issues.IsUnfinished);
        Assert.False(issues.IsIncorrect);

        s = Valid();
        s.BaseAbilities = new() { "Damp" };            // not a Servine ability
        s.ChosenMoves.Add("Hyper Beam");               // can't learn, not in moves.json
        s.Gender = "Genderless";
        issues = SheetValidation.Check(s, Data());
        Assert.False(issues.IsUnfinished);
        Assert.Equal(4, issues.Errors.Count);
        Assert.Contains(issues.Errors, e => e.Contains("Damp"));
        Assert.Contains(issues.Errors, e => e.StartsWith("Moves missing from moves.json") && e.Contains("Hyper Beam"));

        s = Valid();
        s.ChosenPokemon = "missingno";
        Assert.Contains(SheetValidation.Check(s, Data()).Errors, e => e.Contains("not in the Pokédex"));
    }
}

public class CombatStageTests
{
    [Theory]
    [InlineData(20, 1, 4)]
    [InlineData(20, 3, 12)]
    [InlineData(20, -2, -8)]
    [InlineData(13, 1, 3)]      // 2.6 rounded to the nearest
    [InlineData(13, -1, -3)]
    [InlineData(12, 1, 2)]      // 2.4 rounded to the nearest
    [InlineData(20, 9, 24)]     // capped at +6
    [InlineData(20, -9, -24)]
    public void Each_stage_is_twenty_percent_of_the_stat(int stat, int stages, int expected) =>
        Assert.Equal(expected, SheetRules.CombatStageBonus(stat, stages));

    [Fact]
    public void Combat_stages_are_saved_and_cleared_by_rest()
    {
        var play = new SheetPlayState { CombatStages = { ["ATK"] = 2 } };
        var json = JsonSerializer.Serialize(play, JsonDefaults.Options);
        var loaded = JsonSerializer.Deserialize<SheetPlayState>(json, JsonDefaults.Options)!;
        Assert.Equal(2, loaded.CombatStage("ATK"));
        Assert.Equal(0, loaded.CombatStage("DEF"));
        loaded.Reset();
        Assert.Empty(loaded.CombatStages);
    }
}

public class ExperienceTests
{
    [Fact]
    public void Old_sheets_without_experience_load_with_zero()
    {
        var json = """{"id":"a","nickname":"Old","pokemonName":"pikachu","state":{"chosen_pokemon":"pikachu","pokemon_level":12}}""";
        var record = JsonSerializer.Deserialize<SheetRecord>(json, JsonDefaults.Options)!;
        Assert.Equal(0, record.State.Experience);
        Assert.Equal(12, record.State.Level);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("\"57\"", 57)]
    [InlineData("150", 100)]   // clamped to the maximum
    [InlineData("-5", 0)]
    [InlineData("null", 0)]
    public void Experience_is_read_and_kept_between_0_and_100(string value, int expected)
    {
        var state = JsonSerializer.Deserialize<SheetState>($$"""{"pokemon_experience":{{value}}}""", JsonDefaults.Options)!;
        Assert.Equal(expected, state.Experience);
    }

    [Fact]
    public void Experience_is_saved_and_survives_a_clone()
    {
        var state = new SheetState { Experience = 73 };
        Assert.Contains("\"pokemon_experience\":73", JsonSerializer.Serialize(state, JsonDefaults.Compact));
        Assert.Equal(73, state.Clone().Experience);
    }
}

public class LoyaltyTests
{
    [Fact]
    public void Old_sheets_without_loyalty_load_with_one()
    {
        var json = """{"id":"a","nickname":"Old","pokemonName":"pikachu","state":{"chosen_pokemon":"pikachu","pokemon_level":12}}""";
        var record = JsonSerializer.Deserialize<SheetRecord>(json, JsonDefaults.Options)!;
        Assert.Equal(1, record.State.Loyalty);
        Assert.Equal(1, new SheetState().Loyalty);
    }

    [Theory]
    [InlineData("4", 4)]
    [InlineData("\"8\"", 8)]
    [InlineData("15", 10)]   // clamped to the maximum
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("null", 1)]
    public void Loyalty_is_read_and_kept_between_1_and_10(string value, int expected)
    {
        var state = JsonSerializer.Deserialize<SheetState>($$"""{"pokemon_loyalty":{{value}}}""", JsonDefaults.Options)!;
        Assert.Equal(expected, state.Loyalty);
    }

    [Fact]
    public void Loyalty_is_saved_and_survives_a_clone()
    {
        var state = new SheetState { Loyalty = 6 };
        Assert.Contains("\"pokemon_loyalty\":6", JsonSerializer.Serialize(state, JsonDefaults.Compact));
        Assert.Equal(6, state.Clone().Loyalty);
    }
}

public class TypeChartTests
{
    private static readonly TypeChart Chart = TypeChart.Default;

    [Fact]
    public void Default_chart_has_the_21_types()
    {
        Assert.Equal(21, Chart.Types.Count);
        Assert.Contains("Light", Chart.Types);
        Assert.Contains("Data", Chart.Types);
        Assert.Contains("Sound", Chart.Types);
    }

    [Theory]
    [InlineData("Fire", "Bug", 2)]          // column attacks, row defends
    [InlineData("Fighting", "Bug", 0.5)]
    [InlineData("Dragon", "Fairy", 0)]
    [InlineData("Ghost", "Normal", 0)]
    [InlineData("Normal", "Ghost", 0)]
    [InlineData("Electric", "Ground", 0)]
    [InlineData("Ground", "Flying", 0)]
    [InlineData("Psychic", "Dark", 0)]
    [InlineData("Poison", "Steel", 0)]
    [InlineData("Light", "Steel", 0)]
    [InlineData("Data", "Ice", 0)]
    [InlineData("Data", "Data", 0.5)]
    [InlineData("Sound", "Water", 2)]
    [InlineData("Water", "Fire", 2)]
    [InlineData("Unknown", "Fire", 1)]
    public void Single_type_multipliers(string attacking, string defending, double expected) =>
        Assert.Equal(expected, Chart.Multiplier(attacking, defending));

    [Fact]
    public void Dual_types_multiply()
    {
        Assert.Equal(4, Chart.Multiplier("Ice", new[] { "Dragon", "Flying" }));
        Assert.Equal(0, Chart.Multiplier("Electric", new[] { "Water", "Ground" }));
        Assert.Equal(0.25, Chart.Multiplier("Grass", new[] { "Fire", "Dragon" }));
        Assert.Equal(2, Chart.Multiplier("Fire", new[] { "Bug", "Bug" })); // same type counted once
    }

    [Fact]
    public void Json_round_trip_and_file_seeding()
    {
        var copy = TypeChart.FromJson(Chart.ToJson());
        Assert.Equal(Chart.Types, copy.Types);
        foreach (var a in Chart.Types)
            foreach (var d in Chart.Types)
                Assert.Equal(Chart.Multiplier(a, d), copy.Multiplier(a, d));

        var dir = Path.Combine(Path.GetTempPath(), "prs-typechart-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(TypeChart.EnsureFile(dir));
            Assert.False(TypeChart.EnsureFile(dir));
            File.WriteAllText(Path.Combine(dir, TypeChart.FileName),
                """{ "types": ["Fire", "Water"], "damage_taken": { "Fire": { "Water": 2 } } }""");
            var custom = TypeChart.LoadOrDefault(dir);
            Assert.Equal(new[] { "Fire", "Water" }, custom.Types);
            Assert.Equal(2, custom.Multiplier("Water", "Fire"));
            Assert.Equal(1, custom.Multiplier("Fire", "Water")); // missing = neutral
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Negative_multipliers_are_rejected() =>
        Assert.Throws<InvalidDataException>(() =>
            TypeChart.FromJson("""{ "types": ["Fire"], "damage_taken": { "Fire": { "Fire": -1 } } }"""));
}
