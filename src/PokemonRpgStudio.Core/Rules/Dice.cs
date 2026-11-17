using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PokemonRpgStudio.Core.Rules;

public sealed record DiceGroupRoll(string Dice, IReadOnlyList<int> Results)
{
    public int Total => Results.Sum();
}

public sealed record DiceExpressionResult(string Expression, IReadOnlyList<DiceGroupRoll> Rolls, double Final)
{
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Result = {Final.ToString(CultureInfo.InvariantCulture)}");
        foreach (var r in Rolls) sb.AppendLine($"{r.Dice} : ( {string.Join(",", r.Results)} )");
        return sb.ToString().TrimEnd();
    }
}

public sealed record EncounterRuleResult(string Label, string Detail);

/// <summary>A stat added to a damage roll, e.g. ("ATK", 13).</summary>
public sealed record StatBonus(string Stat, int Value)
{
    public string Label => DamageRollResult.StatLabel(Stat);
}

public sealed record DamageRollResult(string Formula, DiceExpressionResult Roll, bool Critical, IReadOnlyList<StatBonus> Stats, double Total)
{
    public int StatTotal => Stats.Sum(s => s.Value);

    /// <summary>"2d6+8: 2d6 (3, 5) = 16 + ATK 13 + SPEED 18 → 47"; on a crit the dice read "4d6 (…)".</summary>
    public string Describe()
    {
        var dice = string.Join(" + ", Roll.Rolls.Select(r => $"{r.Dice} ({string.Join(", ", r.Results)})"));
        var text = $"{Formula}: {dice} = {Roll.Final.ToString(CultureInfo.InvariantCulture)}";
        foreach (var stat in Stats) text += $" + {stat.Label} {stat.Value}";
        return text + $" → {Total.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string StatLabel(string stat) => stat switch { "SPATK" => "SP.ATK", "SPDEF" => "SP.DEF", "SPD" => "SPEED", _ => stat };
}

public sealed record AttackRollResult(int Accuracy, int? AC, bool Critical, bool MeetsAc, DamageRollResult? Damage)
{
    public string AccuracyText =>
        $"Accuracy d20 = {Accuracy}" + (AC is { } ac ? $" vs AC {ac} ({(MeetsAc ? "meets AC" : "below AC")})" : "") +
        (Critical ? " — CRITICAL HIT, twice as many damage dice" : Accuracy == 1 ? " — natural 1" : "");
}

public enum CardRollTier { Platinum = 0, Shiny = 1, Hit = 2, Miss = 3 }

public sealed record CardRollBatches(IReadOnlyList<int> PlatineTier, IReadOnlyList<int> ShinyTier, IReadOnlyList<int> BaseTier);

/// <summary>Dice helpers ported from the website's Dice Rolls and Encounter Generator pages.</summary>
public static class Dice
{
    private static readonly Regex DiceRegex = new(@"(\d+)\s*d\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int Roll(int sides, Random? rng = null) => (rng ?? Random.Shared).Next(1, sides + 1);

    public static int RollMany(int count, int sides, Random? rng = null)
    {
        int total = 0;
        for (int i = 0; i < count; i++) total += Roll(sides, rng);
        return total;
    }

    public static int RollRange(int min, int max, Random? rng = null) => (rng ?? Random.Shared).Next(min, max + 1);

    /// <summary>
    /// Rolls an expression such as <c>2d6+1d8+4</c>. Every NdM group is replaced by the sum of
    /// its dice, then the arithmetic (+ - * / and parentheses) is evaluated.
    /// </summary>
    public static DiceExpressionResult RollExpression(string expression, Random? rng = null) => RollExpression(expression, 1, rng);

    /// <summary>
    /// Same as <see cref="RollExpression(string, Random?)"/>, rolling <paramref name="diceMultiplier"/> times
    /// as many dice in every group (critical hits roll twice as many dice: 2d8+10 → 4d8+10).
    /// </summary>
    public static DiceExpressionResult RollExpression(string expression, int diceMultiplier, Random? rng = null)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new FormatException("Enter an expression, e.g. 2d6+1d8+4.");
        var rolls = new List<DiceGroupRoll>();
        var replaced = DiceRegex.Replace(expression, m =>
        {
            int count = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * Math.Max(1, diceMultiplier);
            int sides = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (count > 1000) throw new FormatException("Too many dice (max 1000 per group).");
            if (sides < 1) throw new FormatException("A die needs at least one side.");
            var results = Enumerable.Range(0, count).Select(_ => Roll(sides, rng)).ToList();
            rolls.Add(new DiceGroupRoll($"{count}d{sides}", results));
            return results.Sum().ToString(CultureInfo.InvariantCulture);
        });
        var value = new ArithmeticParser(replaced).Parse();
        return new DiceExpressionResult(expression, rolls, Math.Round(value, 4));
    }

    /// <summary>
    /// Damage roll of an attack: the damage formula (twice as many dice on a critical hit) plus
    /// every given stat (each added once).
    /// </summary>
    public static DamageRollResult RollDamage(string formula, IReadOnlyList<StatBonus> stats, bool critical = false, Random? rng = null)
    {
        var roll = RollExpression(formula, critical ? 2 : 1, rng);
        return new DamageRollResult(formula, roll, critical, stats, roll.Final + stats.Sum(s => s.Value));
    }

    /// <summary>Damage roll with at most one stat (a move adds ATK or SP.ATK).</summary>
    public static DamageRollResult RollDamage(string formula, int statBonus = 0, string? statName = null, bool critical = false, Random? rng = null) =>
        RollDamage(formula, Single(statBonus, statName), critical, rng);

    /// <summary>
    /// Attack: accuracy d20 compared to the AC (when there is one); a 20 is a critical hit and
    /// rolls twice as many damage dice. The damage (formula + stats) is rolled when a formula is given.
    /// </summary>
    public static AttackRollResult RollAttack(string? damageFormula, int? ac, IReadOnlyList<StatBonus> stats, Random? rng = null)
    {
        int accuracy = Roll(20, rng);
        bool critical = accuracy >= SheetRules.CriticalAccuracy;
        DamageRollResult? damage = null;
        if (!string.IsNullOrWhiteSpace(damageFormula) && damageFormula.Trim() != "--")
            damage = RollDamage(damageFormula, stats, critical, rng);
        return new AttackRollResult(accuracy, ac, critical, ac is null || accuracy >= ac, damage);
    }

    /// <summary>Attack with at most one stat added to the damage.</summary>
    public static AttackRollResult RollAttack(string? damageFormula, int? ac, int statBonus = 0, string? statName = null, Random? rng = null) =>
        RollAttack(damageFormula, ac, Single(statBonus, statName), rng);

    private static IReadOnlyList<StatBonus> Single(int value, string? stat) =>
        stat is null ? Array.Empty<StatBonus>() : new[] { new StatBonus(stat, value) };

    /// <summary>Adds a flat bonus / malus to a roll formula: ("2d6+1", 2) → "2d6+1+2", ("3d6", -1) → "3d6-1".</summary>
    public static string WithModifier(string formula, int modifier) =>
        modifier == 0 ? formula : $"{formula.Trim()}{(modifier > 0 ? "+" : "-")}{Math.Abs(modifier)}";

    /// <summary>Parses an AC text ("2", "AC 4", "--", "/") into a number, or null when there is no check.</summary>
    public static int? ParseAc(string? ac)
    {
        var m = Regex.Match(ac ?? "", @"\d+");
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Encounter count from a 0–20 value (Encounter Generator rules panel).</summary>
    public static EncounterRuleResult ResolveEncounterRule(int value, Random? rng = null)
    {
        if (value >= 20)
        {
            int outbreak = RollRange(1, 100, rng);
            if (outbreak <= 50)
            {
                int a = RollMany(1, 12, rng), b = RollMany(2, 8, rng);
                return new($"Outbreak: {a + b} Pokemon", $"d100 ({outbreak}) -> 1d12 ({a}) + 2d8 ({b}) = {a + b}");
            }
            if (outbreak <= 70)
            {
                int a = RollRange(15, 25, rng), b = RollMany(2, 10, rng);
                return new($"Outbreak: {a + b} Pokemon", $"d100 ({outbreak}) -> 15-25 ({a}) + 2d10 ({b}) = {a + b}");
            }
            if (outbreak <= 90)
            {
                int a = RollRange(25, 40, rng), b = RollMany(2, 12, rng);
                return new($"Outbreak: {a + b} Pokemon", $"d100 ({outbreak}) -> 25-40 ({a}) + 2d12 ({b}) = {a + b}");
            }
            if (outbreak <= 95)
            {
                int a = RollRange(40, 60, rng), b = RollMany(2, 20, rng);
                return new($"Outbreak: {a + b} Pokemon", $"d100 ({outbreak}) -> 40-60 ({a}) + 2d20 ({b}) = {a + b}");
            }
            int c = RollRange(70, 75, rng), d = RollMany(2, 20, rng);
            return new($"Outbreak: {c + d} Pokemon", $"d100 ({outbreak}) -> 70-75 ({c}) + 2d20 ({d}) = {c + d}");
        }
        if (value >= 18) { int b = RollMany(2, 8, rng); return new($"{10 + b} encounters", $"10 encounters + 2d8 ({b})"); }
        if (value >= 16) { int b = RollMany(2, 6, rng); return new($"{8 + b} encounters", $"8 encounters + 2d6 ({b})"); }
        if (value >= 11) { int b = RollMany(1, 6, rng); return new($"{6 + b} encounters", $"6 encounters + 1d6 ({b})"); }
        if (value >= 6) { int b = RollMany(1, 4, rng); return new($"{4 + b} encounters", $"4 encounters + 1d4 ({b})"); }
        if (value >= 1) return new("2 encounters", "2 encounters + 0");
        return new("0 encounter", "No encounter");
    }

    /// <summary>
    /// Shiny rolls: d100 per Pokémon; results ≤ Dexnav are rerolled unless they hit the shiny
    /// number. Hits are sorted first.
    /// </summary>
    public static List<int> RollShinyBatch(int count, int shinyNumber, int dexnav, Random? rng = null)
    {
        int rerollWindow = dexnav;
        if (shinyNumber < dexnav) rerollWindow += 1; // the shiny number sits inside the window
        rerollWindow = Math.Min(90, rerollWindow);

        var final = new List<int>();
        int pending = Math.Max(0, count);
        while (pending > 0)
        {
            int next = 0;
            for (int i = 0; i < pending; i++)
            {
                int roll = Roll(100, rng);
                if (roll > rerollWindow || roll == shinyNumber) final.Add(roll);
                else next++;
            }
            pending = next;
        }

        bool hasTarget = final.Contains(shinyNumber);
        return final
            .OrderByDescending(r => r == shinyNumber || (r == 100 && hasTarget))
            .ToList();
    }

    /// <summary>One batch of card rolls with Dexnav rerolls; the window grows for each target inside it.</summary>
    public static List<int> RollCardBatch(int count, int dexnav, IReadOnlyCollection<int> targets, Random? rng = null)
    {
        int rerollWindow = dexnav + targets.Count(t => t < dexnav);
        rerollWindow = Math.Min(90, rerollWindow);

        var final = new List<int>();
        int pending = Math.Max(0, count);
        while (pending > 0)
        {
            int next = 0;
            for (int i = 0; i < pending; i++)
            {
                int roll = Roll(100, rng);
                if (roll > rerollWindow || targets.Contains(roll)) final.Add(roll);
                else next++;
            }
            pending = next;
        }
        return final;
    }

    public static CardRollTier CardTier(int roll, CardRollSettings s, bool canShiny, bool canPlatine)
    {
        if (roll == s.Normal1 || roll == s.Normal2 || roll == s.Normal3) return CardRollTier.Hit;
        if (canShiny && (roll == s.Shiny1 || roll == s.Shiny2)) return CardRollTier.Shiny;
        if (canPlatine && roll == s.Platine1) return CardRollTier.Platinum;
        return CardRollTier.Miss;
    }

    /// <summary>
    /// Card rolls. Everyone rolls a normal-only batch. Shiny or platine Pokémon also roll a
    /// batch that can land shiny/platine/normal and one that can land shiny/normal.
    /// </summary>
    public static CardRollBatches RollCards(int count, CardRollSettings s, bool isShiny, bool isPlatine, Random? rng = null)
    {
        var normal = new[] { s.Normal1, s.Normal2, s.Normal3 };
        var shiny = normal.Concat(new[] { s.Shiny1, s.Shiny2 }).ToArray();
        var full = shiny.Concat(new[] { s.Platine1 }).ToArray();
        bool special = isShiny || isPlatine;

        List<int> Sort(List<int> rolls, bool canShiny, bool canPlatine) =>
            rolls.OrderBy(r => (int)CardTier(r, s, canShiny, canPlatine)).ToList();

        var platineTier = special ? Sort(RollCardBatch(count, s.Dexnav, full, rng), true, true) : new List<int>();
        var shinyTier = special ? Sort(RollCardBatch(count, s.Dexnav, shiny, rng), true, false) : new List<int>();
        var baseTier = Sort(RollCardBatch(count, s.Dexnav, normal, rng), false, false);
        return new CardRollBatches(platineTier, shinyTier, baseTier);
    }

    /// <summary>Minimal recursive-descent evaluator for + - * / and parentheses.</summary>
    private sealed class ArithmeticParser
    {
        private readonly string _s;
        private int _pos;

        public ArithmeticParser(string s) => _s = s;

        public double Parse()
        {
            var v = ParseExpr();
            SkipWs();
            if (_pos < _s.Length) throw new FormatException($"Unexpected '{_s[_pos]}' in expression.");
            return v;
        }

        private void SkipWs() { while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++; }

        private double ParseExpr()
        {
            var v = ParseTerm();
            while (true)
            {
                SkipWs();
                if (_pos >= _s.Length) return v;
                char c = _s[_pos];
                if (c == '+') { _pos++; v += ParseTerm(); }
                else if (c == '-') { _pos++; v -= ParseTerm(); }
                else return v;
            }
        }

        private double ParseTerm()
        {
            var v = ParseFactor();
            while (true)
            {
                SkipWs();
                if (_pos >= _s.Length) return v;
                char c = _s[_pos];
                if (c == '*') { _pos++; v *= ParseFactor(); }
                else if (c == '/')
                {
                    _pos++;
                    var d = ParseFactor();
                    if (d == 0) throw new FormatException("Division by zero.");
                    v /= d;
                }
                else return v;
            }
        }

        private double ParseFactor()
        {
            SkipWs();
            if (_pos >= _s.Length) throw new FormatException("Incomplete expression.");
            char c = _s[_pos];
            if (c == '+') { _pos++; return ParseFactor(); }
            if (c == '-') { _pos++; return -ParseFactor(); }
            if (c == '(')
            {
                _pos++;
                var v = ParseExpr();
                SkipWs();
                if (_pos >= _s.Length || _s[_pos] != ')') throw new FormatException("Missing ')'.");
                _pos++;
                return v;
            }
            int start = _pos;
            while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
            if (start == _pos) throw new FormatException($"Unexpected '{c}' in expression.");
            return double.Parse(_s[start.._pos], CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Persisted dice settings (the website stored these in cookies).</summary>
public sealed class CardRollSettings
{
    public int Normal1 { get; set; } = 1;
    public int Normal2 { get; set; } = 33;
    public int Normal3 { get; set; } = 66;
    public int Shiny1 { get; set; } = 22;
    public int Shiny2 { get; set; } = 44;
    public int Platine1 { get; set; } = 100;
    public int Dexnav { get; set; }
}
