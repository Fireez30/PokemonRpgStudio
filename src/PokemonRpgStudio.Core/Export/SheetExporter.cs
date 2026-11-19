using System.Net;
using System.Text;
using PokemonRpgStudio.Core.Data;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.Core.Export;

/// <summary>Everything needed to print a sheet, computed once.</summary>
public sealed class SheetSnapshot
{
    public required PokemonSpecies Species { get; init; }
    public required SheetState State { get; init; }
    public required string Notes { get; init; }
    public required string Nickname { get; init; }
    public required Dictionary<string, int> Buffs { get; init; }
    public required Dictionary<string, int> FinalStats { get; init; }
    public required List<MoveData> SheetMoves { get; init; }
    public required List<AbilityData> Abilities { get; init; }
    public required string Nature { get; init; }
    public int HitPoints => SheetRules.HitPoints(State.Level, FinalStats["HP"]);
    public int PhysEvade => SheetRules.Evade(FinalStats["DEF"]);
    public int SpecEvade => SheetRules.Evade(FinalStats["SPDEF"]);
    public int SpeedEvade => SheetRules.Evade(FinalStats["SPD"]);
    public int TutorPoints => SheetRules.TutorPoints(State.Level, State.TutorPointsAdjustment);

    public static SheetSnapshot Create(SheetState state, PokemonSpecies species, GameData data, string notes = "", string nickname = "")
    {
        var abilities = new List<AbilityData>();
        foreach (var name in state.BaseAbilities.Concat(state.AdvancedAbilities).Concat(state.HighAbilities).Concat(state.CustomAbilities).Concat(state.ExtraAbilities)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            abilities.Add(data.FindAbility(name) ?? new AbilityData { Name = name, Effect = "" });
        }

        return new SheetSnapshot
        {
            Species = species,
            State = state,
            Notes = notes,
            Nickname = nickname,
            Buffs = SheetRules.ComputeStatsBuff(state.Card, state.Rarity, state.BuffedStat, state.LoweredStat),
            FinalStats = SheetRules.ComputeFinalStats(species, state.Card, state.Rarity, state.BuffedStat, state.LoweredStat, state.PointsByStat),
            SheetMoves = SheetRules.PickMoves(state, species, data.FindMove),
            Abilities = abilities,
            Nature = SheetRules.NatureName(state.BuffedStat, state.LoweredStat),
        };
    }
}

/// <summary>Produces the Markdown and HTML ("Google Doc") versions of a sheet.</summary>
public static class SheetExporter
{
    public const string DefaultImageBaseUrl = "https://faeresdev.site";

    private static readonly (string Label, Stat Stat, string Key)[] StatRows =
    {
        ("HP", Stat.HP, "HP"), ("ATK", Stat.ATK, "ATK"), ("DEF", Stat.DEF, "DEF"),
        ("SPATK", Stat.SPATK, "SPATK"), ("SPDEF", Stat.SPDEF, "SPDEF"), ("SPEED", Stat.SPD, "SPD"),
    };

    public static string ImageUrl(string imageBaseUrl, string pokemonName) =>
        $"{imageBaseUrl.TrimEnd('/')}/images/{ImageResolver.WebsiteImageName(pokemonName)}";

    private static string MoveTypes(MoveData m) => string.Join(" /", m.Types.Select(t => t.Trim())).Trim();

    private static string MoveRoll(MoveData m)
    {
        var roll = m.Roll ?? "";
        return roll.Contains('/') ? roll.Split('/')[0] : roll;
    }

    private static string MoveAc(MoveData m) => string.Equals(m.AC, "none", StringComparison.OrdinalIgnoreCase) ? "/" : m.AC ?? "";

    private static string StatFormula(SheetSnapshot s, Stat stat, string key) =>
        $"{s.Species.BaseStat(stat)}+{s.Buffs[key]}+{s.State.Points(stat)}={s.FinalStats[key]}".Replace("+-", "-");

    private static string NamesOrNone(IReadOnlyCollection<string> names) =>
        names.Count == 0 ? "None" : string.Join(",", names);

    private static string Md(string? s) => (s ?? "").Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");

    public static string ToMarkdown(SheetSnapshot s, string imageBaseUrl = DefaultImageBaseUrl)
    {
        var sb = new StringBuilder();
        var st = s.State;
        var sp = s.Species;
        sb.Append($"![]({ImageUrl(imageBaseUrl, sp.Name)})\n");
        sb.Append($"# {Formatting.Capitalize(sp.Name)}");
        if (!string.IsNullOrWhiteSpace(s.Nickname) && !string.Equals(s.Nickname, sp.Name, StringComparison.OrdinalIgnoreCase))
            sb.Append($" — {s.Nickname}");
        sb.Append('\n');
        sb.Append($"Card: {st.Card}\n");
        sb.Append($"Gender: {st.Gender}\n");
        sb.Append("Item: \n");
        sb.Append($"Nature: {s.Nature}\n");
        sb.Append($"Level: {SheetRules.FormatLevel(st.Level)}\n");
        sb.Append($"Type: {string.Join(",", sp.PokemonTypes)}\n");
        sb.Append($"Rarity: {st.Rarity}\n");
        sb.Append($"Weight: {sp.Weight}\n");
        sb.Append($"Height: {sp.Height}\n\n");

        sb.Append("## **Abilities**\n\n| **Ability** | **Effect** |\n| ---------------- | ------------------ |\n");
        foreach (var a in s.Abilities) sb.Append($"| {Md(a.Name)} |  {Md(a.Effect)} | \n");
        sb.Append("\n\n");

        sb.Append("## **Capabilities**\n\n| **Capability** | **Value** |\n| ---------------- | ------------------ |\n");
        foreach (var c in sp.Capabilities.Where(c => c.Name != "")) sb.Append($"| {Md(c.Name)} | {Md(c.Value)} | \n");
        sb.Append("\n\n");

        sb.Append("## **Skills**\n\n| **Skill** | **Roll** |\n| ---------------- | ------------------ |\n");
        foreach (var c in sp.Skills.Where(c => c.Name != "")) sb.Append($"| {Md(c.Name)} | {Md(c.Roll)} | \n");
        sb.Append("\n\n");

        sb.Append("## **Stats**\n\n");
        sb.Append($"| **Hit Points Max :{s.HitPoints}**                           | **Hit Points: {s.HitPoints}/{s.HitPoints}** |\n");
        sb.Append("|-------------------------------------------------------------|---------------------------------------------|\n");
        foreach (var (label, stat, key) in StatRows)
            sb.Append($"| **Max {label}**: {StatFormula(s, stat, key)}              | **Current {label}**:{s.FinalStats[key]}                 |\n");
        sb.Append('\n');
        sb.Append("| **Derived stats** |                |\n|-------------------|----------------|\n");
        sb.Append($"| Phys Evade        | {s.PhysEvade}    |\n");
        sb.Append($"| Spec Evade        | {s.SpecEvade}    |\n");
        sb.Append($"| Speed Evade       | {s.SpeedEvade}  |\n");
        sb.Append("| Injuries          | 0     |\n\n");
        sb.Append("*Pokémon Hit Points = Pokémon Level + (HP x3) + 10*\n\n");

        const string moveHeader = "| **Move**    | **Freq**         | **AC**    | **Type**    | **Roll**    | **Dmg. Type**      | **Range**    | **Special Effect** |\n";
        sb.Append("## **Moves** :\n\n");
        sb.Append(moveHeader);
        sb.Append("|-------------|------------------|-----------|-------------|-------------|--------------------|--------------|-------------------|\n");
        int added = 0;
        bool split = false;
        void Split()
        {
            sb.Append('\n').Append(moveHeader).Append("|---------------|---|---|---|---|---|---|---|\n");
            split = true;
        }
        foreach (var m in s.SheetMoves)
        {
            if (added == SheetRules.FirstTableMoves) Split();
            sb.Append($"|{Md(m.Name)}|{Md(m.Frequency)}|{Md(MoveAc(m))}|{MoveTypes(m)}|{MoveRoll(m)}|{SheetRules.FormatClass(m.MoveClass)}|{Md(m.Range)}|{Md(m.Effect)}|\n");
            added++;
        }
        while (added < SheetRules.MinSheetRows)
        {
            if (added == SheetRules.FirstTableMoves && !split) Split();
            sb.Append("|               |   |   |   |   |   |   |   |\n");
            added++;
        }
        sb.Append('\n');
        sb.Append($"Tutor points = {s.TutorPoints}\n\n");
        if (!string.IsNullOrWhiteSpace(st.TutorNotes)) sb.Append("Tutor notes :\n").Append(st.TutorNotes.Trim()).Append("\n\n");
        sb.Append('\n');
        sb.Append("## **Notes**\n\n");
        if (!string.IsNullOrWhiteSpace(st.HeldObject)) sb.Append($"object held : {OneLine(st.HeldObject)}\n");
        sb.Append($"egg moves : {NamesOrNone(st.EggMoves)}\n");
        sb.Append($"TM moves : {NamesOrNone(st.TmMoves)}\n");
        sb.Append($"tutor moves : {NamesOrNone(st.TutorMoves)}\n");
        if (!string.IsNullOrWhiteSpace(s.Notes)) sb.Append("\n\n").Append(s.Notes.Trim());
        return sb.ToString();
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
    /// <summary>Text on one line (Markdown "key : value" lines).</summary>
    private static string OneLine(string s) => string.Join(" ", s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>HTML fragment pasted into Google Docs / Word (website "Copy google doc version").</summary>
    public static string ToHtmlFragment(SheetSnapshot s, string imageBaseUrl = DefaultImageBaseUrl)
    {
        var sb = new StringBuilder();
        var st = s.State;
        var sp = s.Species;
        const string table = "<table border=\"1\" style=\"border-collapse:collapse\">";
        sb.Append($"<img src=\"{H(ImageUrl(imageBaseUrl, sp.Name))}\" width=\"200\">");
        sb.Append($"<h1>{H(Formatting.Capitalize(sp.Name))}</h1>");
        sb.Append("<p>")
          .Append($"Card: {H(st.Card)}<br>Gender: {H(st.Gender)}<br>Item:<br>Nature: {H(s.Nature)}<br>")
          .Append($"Level: {SheetRules.FormatLevel(st.Level)}<br>Type: {H(string.Join(",", sp.PokemonTypes))}<br>Rarity: {H(st.Rarity)}<br>")
          .Append($"Weight: {H(sp.Weight)}<br>Height: {H(sp.Height)}</p>");

        sb.Append("<h2>Abilities</h2>").Append(table).Append("<tr><th>Ability</th><th>Effect</th></tr>");
        foreach (var a in s.Abilities) sb.Append($"<tr><td>{H(a.Name)}</td><td>{H(a.Effect).Replace("\n", "<br>")}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Capabilities</h2>").Append(table).Append("<tr><th>Capability</th><th>Value</th></tr>");
        foreach (var c in sp.Capabilities.Where(c => c.Name != "")) sb.Append($"<tr><td>{H(c.Name)}</td><td>{H(c.Value)}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Skills</h2>").Append(table).Append("<tr><th>Skill</th><th>Roll</th></tr>");
        foreach (var c in sp.Skills.Where(c => c.Name != "")) sb.Append($"<tr><td>{H(c.Name)}</td><td>{H(c.Roll)}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Stats</h2>").Append(table);
        sb.Append($"<tr><td><b>Hit Points Max: {s.HitPoints}</b></td><td><b>Hit Points: {s.HitPoints}/{s.HitPoints}</b></td></tr>");
        foreach (var (label, stat, key) in StatRows)
            sb.Append($"<tr><td><b>Max {label}</b>: {StatFormula(s, stat, key)}</td><td><b>Current {label}</b>: {s.FinalStats[key]}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h3>Derived Stats</h3>").Append(table)
          .Append($"<tr><td>Phys Evade</td><td>{s.PhysEvade}</td></tr>")
          .Append($"<tr><td>Spec Evade</td><td>{s.SpecEvade}</td></tr>")
          .Append($"<tr><td>Speed Evade</td><td>{s.SpeedEvade}</td></tr>")
          .Append("<tr><td>Injuries</td><td>0</td></tr></table>");

        const string moveHeader = "<tr><th>Move</th><th>Freq</th><th>AC</th><th>Type</th><th>Roll</th><th>Dmg Type</th><th>Range</th><th>Special Effect</th></tr>";
        sb.Append("<h2>Moves</h2>").Append(table).Append(moveHeader);
        int added = 0;
        // Second table after the 6th move, like the Markdown export.
        void SplitTable() => sb.Append("</table><br>").Append(table).Append(moveHeader);
        foreach (var m in s.SheetMoves)
        {
            if (added == SheetRules.FirstTableMoves) SplitTable();
            sb.Append($"<tr><td>{H(m.Name)}</td><td>{H(m.Frequency)}</td><td>{H(MoveAc(m))}</td><td>{H(MoveTypes(m))}</td>")
              .Append($"<td>{H(MoveRoll(m))}</td><td>{H(SheetRules.FormatClass(m.MoveClass))}</td><td>{H(m.Range)}</td><td>{H(m.Effect)}</td></tr>");
            added++;
        }
        for (; added < SheetRules.MinSheetRows; added++)
        {
            if (added == SheetRules.FirstTableMoves) SplitTable();
            sb.Append("<tr>").Append(string.Concat(Enumerable.Repeat("<td></td>", 8))).Append("</tr>");
        }
        sb.Append("</table>");

        sb.Append($"<p><b>Tutor points:</b> {s.TutorPoints}</p>");
        if (!string.IsNullOrWhiteSpace(st.TutorNotes)) sb.Append($"<p><b>Tutor notes:</b><br>{H(st.TutorNotes.Trim()).Replace("\n", "<br>")}</p>");
        sb.Append("<h2>Notes</h2>");
        if (!string.IsNullOrWhiteSpace(st.HeldObject)) sb.Append($"<p><b>Object held:</b> {H(st.HeldObject.Trim()).Replace("\n", "<br>")}</p>");
        sb.Append($"<p>Egg moves: {H(NamesOrNone(st.EggMoves).Replace(",", ", "))}</p>");
        sb.Append($"<p>TM moves: {H(NamesOrNone(st.TmMoves).Replace(",", ", "))}</p>");
        sb.Append($"<p>Tutor moves: {H(NamesOrNone(st.TutorMoves).Replace(",", ", "))}</p>");
        if (!string.IsNullOrWhiteSpace(s.Notes)) sb.Append($"<p>{H(s.Notes.Trim()).Replace("\n", "<br>")}</p>");
        return sb.ToString();
    }

    /// <summary>Full standalone HTML document (for "Export HTML" and printing).</summary>
    public static string ToHtmlDocument(SheetSnapshot s, string imageBaseUrl = DefaultImageBaseUrl)
    {
        var title = H(string.IsNullOrWhiteSpace(s.Nickname) ? Formatting.Capitalize(s.Species.Name) : s.Nickname);
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + title + "</title>" +
               "<style>body{font-family:sans-serif;color:#111;max-width:960px;margin:24px auto;padding:0 16px}" +
               "td,th{padding:4px 8px;vertical-align:top}table{margin-bottom:12px}</style></head><body>" +
               ToHtmlFragment(s, imageBaseUrl) + "</body></html>";
    }

    /// <summary>Wraps an HTML fragment in the Windows "HTML Format" clipboard envelope (CF_HTML).</summary>
    public static string ToWindowsClipboardHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:0000000000}\r\nEndHTML:{1:0000000000}\r\nStartFragment:{2:0000000000}\r\nEndFragment:{3:0000000000}\r\n";
        const string pre = "<html><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body></html>";
        int headerLength = string.Format(header, 0, 0, 0, 0).Length;
        int startHtml = headerLength;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(pre);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(post);
        return string.Format(header, startHtml, endHtml, startFragment, endFragment) + pre + fragment + post;
    }
}
