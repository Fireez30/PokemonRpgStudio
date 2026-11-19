using System.Globalization;
using System.Text.RegularExpressions;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.Core.Storage;

public sealed record MarkdownImportResult(SheetRecord Sheet, IReadOnlyList<string> Warnings);

/// <summary>
/// Rebuilds a sheet from the Markdown file exported by the website's Pokemon Generator
/// ("Generate Markdown") — or by this application, which uses the same layout.
/// </summary>
/// <remarks>
/// Recovered: Pokémon, nickname (when present in the title), card, gender, nature, level, rarity,
/// abilities (tier found from the Pokédex), level-up points (from the stat formulas), the move
/// table (level-up moves, locked egg / TM / tutor moves), DB buffs (roll above the expected one),
/// the egg / TM / tutor move lists and the free notes.
/// </remarks>
public static partial class MarkdownSheetImporter
{
    [GeneratedRegex(@"^#\s+(?<name>.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex TitleRegex();

    // "**Max ATK**: 5+3+6=14", "**Max Atk :** 6+3+1=10", "**Max SpDef**: 13+2+11+2=28", "**Max HP**: 7+8=15"
    [GeneratedRegex(@"\*\*Max\s+(?<stat>[A-Za-z. ]+?)\s*:?\s*\*\*\s*:?\s*(?<expr>-?\s*\d+(?:\s*[+-]\s*\d+)*)\s*=\s*(?:\*\*)?\s*(?<final>-?\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StatRegex();

    [GeneratedRegex(@"[+-]?\s*\d+")]
    private static partial Regex TermRegex();

    // Spaces and dots removed first: "(+SP DEF / - SPD)", "(+HP -SPATK)" → "(+SPDEF/-SPD)", "(+HP-SPATK)"
    [GeneratedRegex(@"\(\+(?<buff>[A-Z]+)/?-(?<low>[A-Z]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex NatureRegex();

    [GeneratedRegex(@"^NEUTRAL\((?<stat>[A-Z]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex NeutralNatureRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();

    /// <summary>Lines of an ability description that hand-edited sheets put in the name column.</summary>
    [GeneratedRegex(@"^(Keywords?|Trigger|Effect|Bonus|Target|Connection|Condition|Frequency|Note)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AbilityProseRegex();

    [GeneratedRegex(@"/images/(?<file>[^)\s]+)\.png", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();

    /// <summary>Quick check used to recognise website sheets among other Markdown files.</summary>
    public static bool LooksLikeSheet(string markdown) =>
        (markdown.Contains("## **Moves**", StringComparison.OrdinalIgnoreCase) || markdown.Contains("## **Attack", StringComparison.OrdinalIgnoreCase))
        && StatRegex().IsMatch(markdown);

    public static MarkdownImportResult Parse(string markdown, GameData data, string? fileName = null)
    {
        var warnings = new List<string>();
        var text = markdown.Replace("\r\n", "\n");
        var lines = text.Split('\n');

        // ── Pokémon & nickname ──────────────────────────────────────────────
        string title = TitleRegex().Match(text) is { Success: true } t ? t.Groups["name"].Value.Trim() : "";
        string nickname = "";
        var dash = title.IndexOf(" — ", StringComparison.Ordinal);
        if (dash > 0)
        {
            nickname = title[(dash + 3)..].Trim();
            title = title[..dash].Trim();
        }
        var species = data.FindPokemon(title);
        if (species is null && ImageRegex().Match(text) is { Success: true } img)
        {
            var file = Uri.UnescapeDataString(img.Groups["file"].Value);
            species = data.Pokemon.FirstOrDefault(p => Path.GetFileNameWithoutExtension(ImageResolver.WebsiteImageName(p.Name)) == file);
        }
        if (species is null && fileName != null)
            species = data.FindPokemon(Path.GetFileNameWithoutExtension(fileName).Replace('_', ' '));
        if (species is null)
            throw new InvalidDataException($"Unknown Pokémon \"{title}\" — is this a sheet exported by the website?");

        var state = SheetRules.NewSheet(species);
        state.RemainingRolls = 0; // the nature is already decided

        // ── Header fields ──────────────────────────────────────────────────
        // "Level: 12", "Level : 12", "**Level**: 12"; the first matching name wins.
        string Field(params string[] names)
        {
            foreach (var name in names)
                foreach (var raw in lines)
                {
                    var l = raw.Replace("*", "").Trim();
                    if (l.StartsWith('|') || l.StartsWith("\\|")) continue;
                    // "Level: 12" — or "Male Nature: Naughty" when two fields share a line.
                    var m = Regex.Match(l, $@"(?:^|\s){Regex.Escape(name)}\s*:(?<v>.*)$", RegexOptions.IgnoreCase);
                    if (m.Success) return m.Groups["v"].Value.Trim();
                }
            return "";
        }

        state.Card = Pick(Field("Card"), SheetRules.Cards, "None", "card", warnings);
        state.Rarity = Pick(Field("Rarity"), SheetRules.Rarities, "Normal", "rarity", warnings);
        var gender = Field("Gender");
        state.Gender = SheetRules.Genders.FirstOrDefault(g => g.Equals(gender, StringComparison.OrdinalIgnoreCase)) ?? state.Gender;
        if (NumberRegex().Match(Field("Level", "Niveau")) is { Success: true } lv
            && double.TryParse(lv.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var level))
            state.Level = Math.Clamp(level, 1, SheetRules.MaxLevel);
        else warnings.Add("Level not found: set to 1.");
        // "Loyalty: 7" (written by the desktop exports); older sheets have none and keep 1.
        if (NumberRegex().Match(Field("Loyalty", "Loyauté")) is { Success: true } loy
            && double.TryParse(loy.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var loyalty))
            state.Loyalty = (int)Math.Round(loyalty);
        ParseNature(Field("Nature"), state, warnings);

        // ── Level-up points from "**Max ATK**: 5+3+6=14" ────────────────────
        // Two readings, the one that adds up to the level wins:
        //  • from the final value: points = final - base - buffs (the stats then match the sheet exactly;
        //    this is the website layout "base + buff + points = final");
        //  • from the written terms (hand-edited sheets with other rules): after the base, the biggest
        //    term above 3 is the points (smaller ones are nature / card buffs), else the last positive one.
        var buffs = SheetRules.ComputeStatsBuff(state.Card, state.Rarity, state.BuffedStat, state.LoweredStat);
        var fromFinal = new Dictionary<string, int>();
        var fromTerms = new Dictionary<string, int>();
        var statLines = new List<string>();
        foreach (Match m in StatRegex().Matches(text))
        {
            var key = SheetRules.NormalizeStatKey(m.Groups["stat"].Value);
            if (key is null || fromFinal.ContainsKey(key)) continue; // first block wins (e.g. a sheet listing two forms)
            var terms = TermRegex().Matches(m.Groups["expr"].Value)
                .Select(t => int.Parse(t.Value.Replace(" ", ""), CultureInfo.InvariantCulture)).ToList();
            var final = int.Parse(m.Groups["final"].Value, CultureInfo.InvariantCulture);
            var baseStat = species.BaseStat(Enum.Parse<Stat>(key));
            fromFinal[key] = Math.Max(0, final - baseStat - buffs[key]);
            var rest = terms.Skip(1).ToList();
            fromTerms[key] = rest.Where(t => t > 3).DefaultIfEmpty(rest.LastOrDefault(t => t > 0)).Max();
            statLines.Add($"{key} {m.Groups["expr"].Value.Replace(" ", "")}={final}");
        }
        if (fromFinal.Count > 0)
        {
            int expected = SheetRules.AvailablePoints(state.Level, new Dictionary<string, int>());
            int Gap(Dictionary<string, int> p) => Math.Abs(expected - p.Values.Sum());
            var chosen = Gap(fromFinal) <= Gap(fromTerms) ? fromFinal : fromTerms;
            foreach (var (k, v) in chosen) state.PointsByStat[k] = v;
            if (Gap(chosen) != 0) statLines.Insert(0, "Stats on the imported sheet (level-up points to check):");
            else statLines.Clear();
        }

        // ── Tables ─────────────────────────────────────────────────────────
        var sections = SplitSections(lines);

        foreach (var name in TableRows(sections.GetValueOrDefault("abilities")).Select(r => r[0]).Where(n => n.Length > 0))
        {
            var clean = name.Trim().TrimEnd('}').Trim();
            // Hand-edited sheets sometimes wrap the description over extra rows of the name column.
            var head = clean.Split('(')[0].Trim(); // a remark in parentheses doesn't make it a description
            if (data.FindAbility(clean) is null && (head.Length == 0 || AbilityProseRegex().IsMatch(head) || head.Contains(':')
                    || head.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5 || head.EndsWith('.')
                    || !char.IsLetter(head[0]) || char.IsLower(head[0])))
                continue;
            // "Immolate(disabled)", "Dazzling (disabled tant que …)" → the ability name; the remark goes to the warnings.
            var paren = clean.IndexOf('(');
            if (paren > 0 && data.FindAbility(clean) is null)
            {
                warnings.Add($"Ability \"{clean}\" imported as \"{clean[..paren].Trim()}\".");
                clean = clean[..paren].Trim();
            }
            bool In(IEnumerable<AbilityRef> list) => list.Any(a => a.Name.Trim().Equals(clean, StringComparison.OrdinalIgnoreCase));
            if (In(species.BaseAbilities)) state.BaseAbilities.Add(clean);
            else if (In(species.AdvancedAbilities)) state.AdvancedAbilities.Add(clean);
            else if (In(species.HighAbilities)) state.HighAbilities.Add(clean);
            else if (species.CustomAbilities?.Values.Any(a => a.Name.Trim().Equals(clean, StringComparison.OrdinalIgnoreCase)) == true) state.CustomAbilities.Add(clean);
            else
            {
                state.ExtraAbilities.Add(clean);
                warnings.Add($"Ability \"{clean}\" is not in {species.DisplayName}'s list: kept as an extra ability.");
            }
        }

        // Hand-written names: "Mud Slap" → Mud-Slap, "Safe guard" → Safeguard, "Protect (used)" → Protect.
        static string Letters(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var movesByLetters = data.Moves.GroupBy(m => Letters(m.Name)).ToDictionary(g => g.Key, g => g.First().Name);
        string? MatchMove(string n)
        {
            if (data.FindMove(n) is { } exact) return exact.Name;
            if (movesByLetters.TryGetValue(Letters(n), out var loose)) return loose;
            var paren = n.IndexOf('(');
            if (paren > 0 && MatchMove(n[..paren].Trim()) is { } bare)
            {
                warnings.Add($"Move \"{n}\" imported as \"{bare}\".");
                return bare;
            }
            return null;
        }

        // Notes: "egg moves : A,B" / "TM moves : …" / "tutor moves : …" then free text.
        // Hand-edited sheets also write "Egg move : A", "egg move list : A, B" or placeholders ("?", "todo", "/").
        var notesLines = sections.GetValueOrDefault("notes") ?? new List<string>();
        // "object held : Leftovers" (written by the desktop exports).
        var heldPattern = new Regex(@"^(object|item)\s+held\s*:(?<v>.*)$", RegexOptions.IgnoreCase);
        if (notesLines.Select(l => heldPattern.Match(l.Trim())).FirstOrDefault(m => m.Success) is { } held)
            state.HeldObject = held.Groups["v"].Value.Trim();
        notesLines = notesLines.Where(l => !heldPattern.IsMatch(l.Trim())).ToList();
        List<string> NameList(string kind)
        {
            foreach (var l in notesLines)
            {
                var m = Regex.Match(l.Trim(), $@"^{kind}\s+moves?\b[^:]*:(?<v>.*)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                return m.Groups["v"].Value.Split(',').Select(v => v.Trim())
                    .Where(v => v.Length > 0 && !Regex.IsMatch(v, @"^(none|todo|\?+|/|-+)$", RegexOptions.IgnoreCase))
                    .Select(v => MatchMove(v) ?? v)
                    .ToList();
            }
            return new();
        }
        var eggNames = NameList("egg");
        var tmNames = NameList("TM");
        var tutorNames = NameList("tutor");
        MoveData Resolve(string name, IReadOnlyList<string>? types = null)
        {
            var found = data.FindMove(name)?.Clone();
            if (found != null) return found;
            var placeholder = MoveData.Placeholder(name);
            if (types != null) placeholder.Types = types.ToList();
            return placeholder;
        }
        // Only names are stored; canonical spelling from moves.json when the move exists.
        string Canonical(string n) => data.FindMove(n)?.Name ?? n;
        state.EggMoves = eggNames.Select(Canonical).ToList();
        state.TmMoves = tmNames.Select(Canonical).ToList();
        state.TutorMoves = tutorNames.Select(Canonical).ToList();
        // Website sheets: free notes follow the "tutor moves" line. Hand-edited sheets: the whole section,
        // plus the free text written around the move table.
        var hasTutorLine = notesLines.Any(l => l.Trim().StartsWith("tutor moves", StringComparison.OrdinalIgnoreCase));
        var freeNotes = hasTutorLine
            ? notesLines.SkipWhile(l => !l.Trim().StartsWith("tutor moves", StringComparison.OrdinalIgnoreCase)).Skip(1).ToList()
            : notesLines;
        var notes = string.Join("\n", freeNotes).Trim();
        if (!hasTutorLine)
        {
            var moveText = new[] { "moves", "attack" }.SelectMany(k => sections.GetValueOrDefault(k) ?? new List<string>())
                .Select(l => System.Net.WebUtility.HtmlDecode(l).Replace("**", "").Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('|') && !l.StartsWith("Tutor points", StringComparison.OrdinalIgnoreCase))
                .TakeWhile(l => !l.StartsWith("Tutor notes", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (moveText.Count > 0) notes = (string.Join("\n", moveText) + "\n\n" + notes).Trim();
        }
        // "Tutor points = 6" (total written on the sheet: anything above / below the level ones was set by hand)
        // and the "Tutor notes :" lines that follow it.
        var moveSectionLines = new[] { "moves", "attack" }.SelectMany(k => sections.GetValueOrDefault(k) ?? new List<string>())
            .Select(l => l.Replace("**", "").Trim()).ToList();
        if (moveSectionLines.Select(l => Regex.Match(l, @"^Tutor points\s*[=:]\s*(?<n>-?\d+)", RegexOptions.IgnoreCase))
                .FirstOrDefault(m => m.Success) is { } tp)
            state.TutorPointsAdjustment = int.Parse(tp.Groups["n"].Value, CultureInfo.InvariantCulture) - SheetRules.TutorPoints(state.Level);
        var tutorNotesStart = moveSectionLines.FindIndex(l => l.StartsWith("Tutor notes", StringComparison.OrdinalIgnoreCase));
        if (tutorNotesStart >= 0)
            state.TutorNotes = string.Join("\n", moveSectionLines.Skip(tutorNotesStart + 1).Where(l => !l.StartsWith('|'))).Trim();
        if (statLines.Count > 0) notes = (notes + "\n\n" + string.Join("\n", statLines)).Trim();

        // Moves table: | Move | Freq | AC | Type | Roll | Dmg | Range | Effect |
        var moveRows = TableRows(sections.GetValueOrDefault("moves")).Concat(TableRows(sections.GetValueOrDefault("attack")));
        foreach (var row in moveRows)
        {
            var name = row[0].Trim();
            if (name.Length == 0) continue;
            name = MatchMove(name) ?? name;
            // A description wrapped onto its own row (only the first cell filled) isn't a move.
            if (row.Skip(1).All(c => c.Length == 0 || c is "/" or "-" or "--") && data.FindMove(name) is null) continue;
            var types = row.Count > 3 ? row[3].Split('/').Select(x => x.Trim()).Where(x => x.Length > 0).ToList() : new List<string>();
            bool Has(List<string> l) => l.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (Has(eggNames)) state.LockedEggMoves.Add(name);
            else if (Has(tmNames)) state.LockedTmMoves.Add(name);
            else if (Has(tutorNames)) state.LockedTutorMoves.Add(name);
            else state.ChosenMoves.Add(Canonical(name));

            // DB buff = table roll above the roll expected with STAB.
            var tableRoll = row.Count > 4 ? row[4].Trim() : "";
            var move = Resolve(name, types);
            var expected = SheetRules.UpdatedRoll(species, new Dictionary<string, int>(), move);
            int? tableDb = DbOf(tableRoll), expectedDb = DbOf(expected);
            if (SheetRules.CanHaveDbBuff(move) && tableDb is { } a && expectedDb is { } b && a > b) state.DbBuffByMove[move.Name] = a - b;
        }

        var unknown = state.ChosenMoves.Where(m => data.FindMove(m) is null).ToList();
        if (unknown.Count > 0) warnings.Add("Moves not in the move database (kept as written): " + string.Join(", ", unknown) + ".");
        var available = SheetRules.AvailablePoints(state.Level, state.PointsByStat);
        if (available != 0) warnings.Add($"Level-up points don't add up ({available:+#;-#} for level {SheetRules.FormatLevel(state.Level)}): check them in the editor.");

        var record = new SheetRecord
        {
            Nickname = nickname.Length > 0 ? nickname : species.DisplayName,
            PokemonName = species.Name,
            Notes = notes,
            State = state,
        };
        return new MarkdownImportResult(record, warnings);
    }

    /// <summary>
    /// "Timid (+SPD/-ATK)", "Sassy (+SP DEF / - SPD)", "Proud (+HP -SPATK)", "Neutral (SPD)", "Neutral", "Timid".
    /// A neutral nature has no effect on the stats, so the stat is only a label (HP when not given).
    /// </summary>
    private static void ParseNature(string field, SheetState state, List<string> warnings)
    {
        var compact = new string(field.Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '_').ToArray()).ToUpperInvariant();
        string? buffed = null, lowered = null;
        if (NatureRegex().Match(compact) is { Success: true } m)
        {
            buffed = SheetRules.NormalizeStatKey(m.Groups["buff"].Value);
            lowered = SheetRules.NormalizeStatKey(m.Groups["low"].Value);
        }
        else if (NeutralNatureRegex().Match(compact) is { Success: true } n)
            buffed = lowered = SheetRules.NormalizeStatKey(n.Groups["stat"].Value);
        else if (SheetRules.TryNatureStats(field.Split('(')[0], out var b, out var l))
            (buffed, lowered) = (b, l);
        else if (compact.StartsWith("NEUTRAL"))
        {
            buffed = lowered = "HP";
            warnings.Add("Neutral nature without a stat: HP chosen (a neutral nature doesn't change the stats).");
        }

        if (buffed is null || lowered is null)
        {
            warnings.Add(field.Length > 0 ? $"Nature \"{field}\" not understood." : "Nature not found.");
            return;
        }
        state.BuffedStat = buffed;
        state.LoweredStat = lowered;
    }

    private static string Pick(string value, IReadOnlyList<string> options, string fallback, string label, List<string> warnings)
    {
        var found = options.FirstOrDefault(o => o.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found is null && value.Trim().Length > 0) warnings.Add($"Unknown {label} \"{value}\": set to {fallback}.");
        return found ?? fallback;
    }

    private static int? DbOf(string roll)
    {
        roll = roll.Trim();
        foreach (var (db, r) in SheetRules.DamageBaseToRoll)
            if (r.Equals(roll, StringComparison.OrdinalIgnoreCase)) return db;
        return SheetRules.RollToDamageBase.TryGetValue(roll, out var v) ? v : null;
    }

    /// <summary>Groups lines under their "## **Title**" heading (lower-case key, first word).</summary>
    private static Dictionary<string, List<string>> SplitSections(string[] lines)
    {
        var result = new Dictionary<string, List<string>>();
        List<string>? current = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            string? key = null;
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                key = trimmed[3..].Replace("*", "").Replace(":", "").Trim().Split(' ')[0].ToLowerInvariant();
            // Hand-edited sheets: a bold "**Attacks**" line instead of a heading starts the move table.
            else if (Regex.IsMatch(trimmed, @"^\*\*\s*Attacks?\b", RegexOptions.IgnoreCase))
                key = "attack";
            if (key != null)
            {
                // A repeated heading (e.g. "Stats Solo form" / "Stats Schooled form") adds to the first one.
                if (!result.TryGetValue(key, out current)) result[key] = current = new List<string>();
                continue;
            }
            current?.Add(line);
        }
        return result;
    }

    /// <summary>Data rows of the Markdown tables in a section (headers and separators skipped).</summary>
    private static IEnumerable<List<string>> TableRows(List<string>? lines)
    {
        if (lines is null) yield break;
        static bool IsSeparator(string l) => Regex.IsMatch(l, @"^\|[\s\-:|]+\|?$"); // --- separators and empty rows
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("\\|")) line = line[1..];   // a row escaped by the editor: "\| Damp | Static"
            if (!line.StartsWith('|') || IsSeparator(line)) continue;
            // Header rows start with a column title (hand-edited sheets bold data rows too, and give
            // single moves their own small table, so neither bold nor a following separator means "header").
            if (Regex.IsMatch(line, @"^\|\s*(\*\*)?\s*(Moves?|Abilit(y|ies)|Capabilit(y|ies)|Skills?)\s*(\*\*)?\s*\|", RegexOptions.IgnoreCase)) continue;
            var cells = Regex.Split(line.Trim('|'), @"(?<!\\)\|")
                .Select(c => System.Net.WebUtility.HtmlDecode(c.Replace("\\|", "|").Replace("\\_", "_").Replace("**", "")).Trim())
                .ToList();
            yield return cells;
        }
    }
}
