namespace PokemonRpgStudio.Core.Rules;

/// <summary>Hit points of a Pokémon once injuries are taken into account.</summary>
/// <param name="BaseMaxHp">Max HP without injuries (sheet value + temporary bonus / malus).</param>
/// <param name="CurrentHp">Current HP (can be negative).</param>
/// <param name="Injuries">Injuries the Pokémon has. Damage adds them; healing never removes them (only by hand).</param>
/// <param name="MaxHp">Max HP reduced by 1/10 per injury.</param>
/// <param name="RealMaxHp">Theoretical max HP (sheet value): the 25 % steps of the injuries are based on it.</param>
public sealed record HpStatus(int BaseMaxHp, int CurrentHp, int Injuries, int MaxHp, int RealMaxHp)
{
    public int InjuryReduction => BaseMaxHp - MaxHp;
    public bool IsFainted => CurrentHp <= 0;

    /// <summary>HP at or below which the next damage gives an injury (next 25 % marker under the current HP).</summary>
    public int NextInjuryAt =>
        InjuryRules.InjuryThreshold(BaseMaxHp, InjuryRules.MarkersPassed(BaseMaxHp, RealMaxHp, CurrentHp) + 1, RealMaxHp);
}

/// <summary>
/// Injury system. HP markers sit every 25 % of the theoretical max HP (sheet value) below the max:
/// 75 %, 50 %, 25 %, 0 %, −25 %… Taking damage that brings the HP to or past a marker gives one injury
/// per marker passed, and a single hit of more than 50 % of the max HP (massive damage) gives one more.
/// Each injury reduces the max HP by a tenth. Healing never removes injuries: they are removed by hand.
/// </summary>
public static class InjuryRules
{
    /// <summary>Injuries after which the max HP reaches 0.</summary>
    public const int MaxInjuries = 10;

    /// <summary>Fraction of the max HP between two markers (1/4 = 25 %).</summary>
    public const int InjuriesPerMaxHp = 4;

    /// <summary>
    /// Markers at or below <paramref name="currentHp"/>: floor(lost / (real max / 4)), the HP lost being counted
    /// from <paramref name="baseMaxHp"/>. One at 75 % of the max, two at 50 %, three at 25 %, four at 0…
    /// </summary>
    public static int MarkersPassed(int baseMaxHp, int realMaxHp, int currentHp)
    {
        if (realMaxHp <= 0) return 0;
        long lost = (long)baseMaxHp - currentHp;
        if (lost <= 0) return 0;
        return (int)(lost * InjuriesPerMaxHp / realMaxHp);
    }

    /// <summary>Markers passed when the max and the theoretical max are the same.</summary>
    public static int DamageInjuries(int maxHp, int currentHp) => MarkersPassed(maxHp, maxHp, currentHp);

    /// <summary>The HP value of the <paramref name="injury"/>-th marker (25 % steps of <paramref name="realMaxHp"/>).</summary>
    public static int InjuryThreshold(int baseMaxHp, int injury, int realMaxHp) =>
        (int)Math.Floor(baseMaxHp - injury * realMaxHp / (double)InjuriesPerMaxHp);

    /// <summary>The HP value of the <paramref name="injury"/>-th marker when the max and the theoretical max are the same.</summary>
    public static int InjuryThreshold(int maxHp, int injury) => InjuryThreshold(maxHp, injury, maxHp);

    /// <summary>Massive damage: a single hit of more than 50 % of the max HP (before injuries).</summary>
    public static bool IsMassiveDamage(int baseMaxHp, int amount) => baseMaxHp > 0 && (long)amount * 2 > baseMaxHp;

    /// <summary>Max HP reduced by 1/10 per injury (rounded down, never below 0).</summary>
    public static int MaxHpWithInjuries(int baseMaxHp, int injuries) =>
        Math.Max(0, (int)Math.Floor(baseMaxHp * (MaxInjuries - Math.Min(injuries, MaxInjuries)) / (double)MaxInjuries));

    /// <summary>HP state with <paramref name="injuries"/>; the HP are capped to the injured max (this never adds an injury).</summary>
    /// <param name="realMaxHp">Theoretical max HP (sheet value) for the 25 % markers. Null = same as <paramref name="baseMaxHp"/>.</param>
    public static HpStatus Create(int baseMaxHp, int currentHp, int injuries, int? realMaxHp = null)
    {
        injuries = Math.Max(0, injuries);
        var max = MaxHpWithInjuries(baseMaxHp, injuries);
        return new HpStatus(baseMaxHp, Math.Min(currentHp, max), injuries, max, realMaxHp ?? baseMaxHp);
    }

    /// <summary>
    /// Takes one hit: one injury per marker passed (HP can go below 0, each further 25 % is another marker),
    /// plus one for a hit of more than 50 % of the max HP.
    /// </summary>
    public static HpStatus Damage(HpStatus status, int amount)
    {
        amount = Math.Max(0, amount);
        var after = status.CurrentHp - amount;
        var passed = MarkersPassed(status.BaseMaxHp, status.RealMaxHp, after) - MarkersPassed(status.BaseMaxHp, status.RealMaxHp, status.CurrentHp);
        var injuries = status.Injuries + Math.Max(0, passed) + (IsMassiveDamage(status.BaseMaxHp, amount) ? 1 : 0);
        return Create(status.BaseMaxHp, after, injuries, status.RealMaxHp);
    }

    /// <summary>Heals: the injuries stay, so the HP can't go over the injured max.</summary>
    public static HpStatus Heal(HpStatus status, int amount) =>
        Create(status.BaseMaxHp, status.CurrentHp + Math.Max(0, amount), status.Injuries, status.RealMaxHp);

    /// <summary>Changes the injuries by hand (the HP stay, capped to the new max).</summary>
    public static HpStatus WithInjuries(HpStatus status, int injuries) =>
        Create(status.BaseMaxHp, status.CurrentHp, injuries, status.RealMaxHp);

    /// <summary>Full heal: HP to the max allowed by the injuries.</summary>
    public static HpStatus FullHeal(int baseMaxHp, int injuries, int? realMaxHp = null) =>
        Create(baseMaxHp, baseMaxHp, injuries, realMaxHp);
}
