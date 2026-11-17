using System.Globalization;
using System.Text.RegularExpressions;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.Core.Data;

public enum MoveSortKey { Name, Type, Class, Frequency, AC, DamageBase, Range, Learners }
public enum AbilitySortKey { Name, Frequency, Holders }

/// <summary>A Pokémon that can learn a move, and how (Level-up, Evolution, TM, Tutor, Egg).</summary>
public sealed record MoveLearner(string Pokemon, string Method, int? Level)
{
    public string Label => Method == "Level-up" && Level is { } l ? $"Lv {l}" : Method;
}

/// <summary>A Pokémon that has an ability, and in which tier (Base, Advanced, High, Mega or a custom section).</summary>
public sealed record AbilityHolder(string Pokemon, string Tier);

public sealed class MoveFilter
{
    public string? Text { get; init; }
    /// <summary>Also search the effect and range texts.</summary>
    public bool SearchEffects { get; init; }
    public string? Type { get; init; }
    public string? MoveClass { get; init; }
    /// <summary>Normalised frequency (see <see cref="GameCatalog.NormalizeFrequency"/>).</summary>
    public string? Frequency { get; init; }
    /// <summary>Only the moves this Pokémon can learn (level-up, TM, tutor, egg).</summary>
    public string? LearnableBy { get; init; }
}

public sealed class AbilityFilter
{
    public string? Text { get; init; }
    public bool SearchEffects { get; init; }
    public string? Frequency { get; init; }
    /// <summary>Only the abilities this Pokémon can have.</summary>
    public string? Pokemon { get; init; }
    /// <summary>Hide abilities no Pokémon has.</summary>
    public bool OnlyUsed { get; init; }
}

/// <summary>
/// Move and ability lists of the game data: who learns / has what, filtering and sorting.
/// Built from a <see cref="GameData"/> snapshot (rebuild it after the data changed).
/// </summary>
public sealed class GameCatalog
{
    private readonly GameData _data;
    private readonly Dictionary<string, List<MoveLearner>> _learners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AbilityHolder>> _holders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _movesBySpecies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _abilitiesBySpecies = new(StringComparer.OrdinalIgnoreCase);

    public GameData Data => _data;

    public GameCatalog(GameData data)
    {
        _data = data;
        foreach (var sp in data.Pokemon)
        {
            var moves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddMoves(IEnumerable<LearnsetMove>? list, Func<LearnsetMove, (string Method, int? Level)> how)
            {
                foreach (var m in list ?? Enumerable.Empty<LearnsetMove>())
                {
                    var key = MoveKey(m.Name);
                    if (key.Length == 0) continue;
                    var (method, level) = how(m);
                    var learners = Bucket(_learners, key);
                    // One line per Pokémon and method (a move learned twice by level-up keeps the first level).
                    if (!learners.Any(l => l.Pokemon == sp.Name && l.Method == method))
                        learners.Add(new MoveLearner(sp.Name, method, level));
                    moves.Add(key);
                }
            }
            AddMoves(sp.Moves.OrderBy(m => m.Level), m => m.Level <= 0 ? ("Evolution", null) : ("Level-up", m.Level));
            AddMoves(sp.EvoMoves, _ => ("Evolution", null));
            AddMoves(sp.TmMoves, _ => ("TM", null));
            AddMoves(sp.TutorMoves, _ => ("Tutor", null));
            AddMoves(data.EffectiveEggMoves(sp), _ => ("Egg", null));
            _movesBySpecies[sp.Name] = moves;

            var abilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddAbility(string? name, string tier)
            {
                var key = AbilityKey(name);
                if (key.Length == 0) return;
                var holders = Bucket(_holders, key);
                if (!holders.Any(h => h.Pokemon == sp.Name && h.Tier == tier)) holders.Add(new AbilityHolder(sp.Name, tier));
                abilities.Add(key);
            }
            foreach (var a in sp.BaseAbilities) AddAbility(a.Name, "Base");
            foreach (var a in sp.AdvancedAbilities) AddAbility(a.Name, "Advanced");
            foreach (var a in sp.HighAbilities) AddAbility(a.Name, "High");
            foreach (var (section, a) in sp.CustomAbilities ?? new()) AddAbility(a.Name, section);
            if (PokedexHelpers.ParseMega(sp) is { Ability.Length: > 0 } mega) AddAbility(mega.Ability, "Mega");
            _abilitiesBySpecies[sp.Name] = abilities;
        }
    }

    private static List<T> Bucket<T>(Dictionary<string, List<T>> dict, string key)
    {
        if (!dict.TryGetValue(key, out var list)) dict[key] = list = new List<T>();
        return list;
    }

    /// <summary>Learnsets may spell a move differently (case, colon): use the moves.json name when known.</summary>
    private string MoveKey(string? name) => _data.FindMove(name)?.Name.Trim() ?? (name ?? "").Replace(":", "").Trim();
    private string AbilityKey(string? name) => _data.FindAbility(name)?.Name.Trim() ?? (name ?? "").Trim();

    public IReadOnlyList<MoveLearner> LearnersOf(string moveName) =>
        _learners.GetValueOrDefault(MoveKey(moveName)) ?? (IReadOnlyList<MoveLearner>)Array.Empty<MoveLearner>();

    public IReadOnlyList<AbilityHolder> HoldersOf(string abilityName) =>
        _holders.GetValueOrDefault(AbilityKey(abilityName)) ?? (IReadOnlyList<AbilityHolder>)Array.Empty<AbilityHolder>();

    public int LearnerCount(string moveName) => LearnersOf(moveName).Select(l => l.Pokemon).Distinct().Count();
    public int HolderCount(string abilityName) => HoldersOf(abilityName).Select(h => h.Pokemon).Distinct().Count();

    // ─── Frequencies ────────────────────────────────────────────────────────

    private static readonly Regex FrequencyRx = new(
        @"^\W*(?:(?<n1>\d)\s*x\s*)?(?<base>at[\s-]*will|eot|scene|daily|static)(?:\s*x\s*(?<n2>\d))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Groups frequency spellings: "Daily x1" → "Daily", "2x Daily" → "Daily x2", "At Will - Free Action" → "At-Will".
    /// Anything else becomes "Other".
    /// </summary>
    public static string NormalizeFrequency(string? value)
    {
        var m = FrequencyRx.Match(value ?? "");
        if (!m.Success) return "Other";
        var b = m.Groups["base"].Value.ToLowerInvariant().Replace(" ", "").Replace("-", "");
        var name = b switch { "atwill" => "At-Will", "eot" => "EOT", "scene" => "Scene", "daily" => "Daily", _ => "Static" };
        var n = m.Groups["n1"].Success ? m.Groups["n1"].Value : m.Groups["n2"].Success ? m.Groups["n2"].Value : "1";
        return n == "1" || name is "At-Will" or "EOT" or "Static" ? name : $"{name} x{n}";
    }

    /// <summary>Most usable first: Static, At-Will, EOT, Scene x3, Scene x2, Scene, Daily x3 … Daily, Other.</summary>
    public static int FrequencyRank(string normalized)
    {
        var parts = normalized.Split(" x");
        int count = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c : 1;
        int baseRank = parts[0] switch { "Static" => 0, "At-Will" => 1, "EOT" => 2, "Scene" => 3, "Daily" => 4, _ => 9 };
        return baseRank * 10 + (9 - Math.Min(count, 9));
    }

    /// <summary>The frequency written on the first line of an ability effect ("Scene x2 - Free Action" …).</summary>
    public static string AbilityFrequency(AbilityData ability) =>
        NormalizeFrequency((ability.Effect ?? "").Split('\n').FirstOrDefault());

    public static IReadOnlyList<string> SortFrequencies(IEnumerable<string> normalized) =>
        normalized.Distinct().OrderBy(FrequencyRank).ThenBy(f => f, StringComparer.Ordinal).ToList();

    // ─── Moves ──────────────────────────────────────────────────────────────

    public static int? ParseAC(string? ac) =>
        int.TryParse((ac ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;

    public IEnumerable<MoveData> FilterMoves(MoveFilter filter)
    {
        HashSet<string>? learnable = null;
        if (!string.IsNullOrWhiteSpace(filter.LearnableBy))
        {
            var sp = _data.FindPokemon(filter.LearnableBy);
            learnable = sp is null ? new HashSet<string>() : _movesBySpecies.GetValueOrDefault(sp.Name) ?? new HashSet<string>();
        }
        var text = filter.Text?.Trim();
        foreach (var m in _data.Moves)
        {
            if (string.IsNullOrWhiteSpace(m.Name)) continue;
            if (!string.IsNullOrEmpty(text) && !m.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !(filter.SearchEffects && ((m.Effect ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)
                                              || (m.Range ?? "").Contains(text, StringComparison.OrdinalIgnoreCase))))
                continue;
            if (!string.IsNullOrEmpty(filter.Type) && !m.Types.Any(t => t.Trim().Equals(filter.Type, StringComparison.OrdinalIgnoreCase))) continue;
            if (!string.IsNullOrEmpty(filter.MoveClass) && !(m.MoveClass ?? "").Trim().Equals(filter.MoveClass, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(filter.Frequency) && NormalizeFrequency(m.Frequency) != filter.Frequency) continue;
            if (learnable != null && !learnable.Contains(m.Name.Trim())) continue;
            yield return m;
        }
    }

    /// <summary>Sorts moves; entries without a value for the key (no AC, no damage…) always come last.</summary>
    public IReadOnlyList<MoveData> SortMoves(IEnumerable<MoveData> moves, MoveSortKey key, bool descending)
    {
        var list = moves.ToList();
        return key switch
        {
            MoveSortKey.Type => By(list, m => m.TypesLabel, descending),
            MoveSortKey.Class => By(list, m => m.MoveClass?.Trim(), descending),
            MoveSortKey.Frequency => ByNumber(list, m => FrequencyRank(NormalizeFrequency(m.Frequency)), descending),
            MoveSortKey.AC => ByNumber(list, m => ParseAC(m.AC), descending),
            MoveSortKey.DamageBase => ByNumber(list, m => m.DamageBase > 0 ? m.DamageBase : null, descending),
            MoveSortKey.Range => By(list, m => m.Range?.Trim(), descending),
            MoveSortKey.Learners => ByNumber(list, m => LearnerCount(m.Name), descending),
            _ => ByName(list, m => m.Name, descending),
        };
    }

    // ─── Abilities ──────────────────────────────────────────────────────────

    public IEnumerable<AbilityData> FilterAbilities(AbilityFilter filter)
    {
        HashSet<string>? allowed = null;
        if (!string.IsNullOrWhiteSpace(filter.Pokemon))
        {
            var sp = _data.FindPokemon(filter.Pokemon);
            allowed = sp is null ? new HashSet<string>() : _abilitiesBySpecies.GetValueOrDefault(sp.Name) ?? new HashSet<string>();
        }
        var text = filter.Text?.Trim();
        foreach (var a in _data.Abilities)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) continue;
            if (!string.IsNullOrEmpty(text) && !a.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !(filter.SearchEffects && (a.Effect ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!string.IsNullOrEmpty(filter.Frequency) && AbilityFrequency(a) != filter.Frequency) continue;
            if (allowed != null && !allowed.Contains(a.Name.Trim())) continue;
            if (filter.OnlyUsed && HoldersOf(a.Name).Count == 0) continue;
            yield return a;
        }
    }

    public IReadOnlyList<AbilityData> SortAbilities(IEnumerable<AbilityData> abilities, AbilitySortKey key, bool descending)
    {
        var list = abilities.ToList();
        return key switch
        {
            AbilitySortKey.Frequency => ByNumber(list, a => FrequencyRank(AbilityFrequency(a)), descending),
            AbilitySortKey.Holders => ByNumber(list, a => HolderCount(a.Name), descending),
            _ => ByName(list, a => a.Name, descending),
        };
    }

    // ─── Sorting helpers (ties broken by name, missing values last) ────────

    private static IReadOnlyList<MoveData> By(List<MoveData> list, Func<MoveData, string?> key, bool descending)
    {
        var withValue = list.Where(m => !string.IsNullOrWhiteSpace(key(m)));
        var ordered = descending
            ? withValue.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
            : withValue.OrderBy(key, StringComparer.OrdinalIgnoreCase);
        return ordered.ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(list.Where(m => string.IsNullOrWhiteSpace(key(m))).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<T> ByNumber<T>(List<T> list, Func<T, int?> key, bool descending) where T : class
    {
        static string Name(T item) => item switch { MoveData m => m.Name, AbilityData a => a.Name, _ => "" };
        var withValue = list.Where(x => key(x).HasValue);
        var ordered = descending ? withValue.OrderByDescending(key) : withValue.OrderBy(key);
        return ordered.ThenBy(Name, StringComparer.OrdinalIgnoreCase)
            .Concat(list.Where(x => !key(x).HasValue).OrderBy(Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<T> ByName<T>(List<T> list, Func<T, string> name, bool descending) =>
        (descending ? list.OrderByDescending(name, StringComparer.OrdinalIgnoreCase) : list.OrderBy(name, StringComparer.OrdinalIgnoreCase)).ToList();
}
