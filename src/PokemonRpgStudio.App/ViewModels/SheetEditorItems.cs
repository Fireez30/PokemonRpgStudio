using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Rules;

namespace PokemonRpgStudio.App.ViewModels;

/// <param name="SearchIndex">Searchable text of the sheet (see <see cref="Core.Storage.SheetSearch"/>).</param>
/// <param name="Issues">What is missing or wrong on the sheet (see <see cref="SheetValidation"/>).</param>
public sealed record SheetListItem(SheetRecord Record, string SearchIndex = "", SheetIssues? Issues = null)
{
    public SheetIssues Problems => Issues ?? SheetIssues.None;
    public bool HasIssues => Problems.HasIssues;
    public bool HasErrors => Problems.IsIncorrect;
    /// <summary>Tooltip of the ⚠ marker: every problem, errors first.</summary>
    public string IssuesTip => HasIssues
        ? (HasErrors ? "Not correct:\n• " + string.Join("\n• ", Problems.Errors) : "")
          + (HasErrors && Problems.IsUnfinished ? "\n\n" : "")
          + (Problems.IsUnfinished ? "Not finished:\n• " + string.Join("\n• ", Problems.Missing) : "")
        : "";
    public string Id => Record.Id;
    public string Nickname => string.IsNullOrWhiteSpace(Record.Nickname) ? "(no nickname)" : Record.Nickname;
    public string PokemonName => Record.PokemonName;
    public string Subtitle => $"{Formatting.TitleCase(Record.PokemonName)} · Lv. {(Record.State.Level > 0 ? SheetRules.FormatLevel(Record.State.Level) : "—")}" +
                              (Record.State.Rarity is "Shiny" or "Platine" ? $" · {Record.State.Rarity}" : "") +
                              $" · {Record.Status}" +
                              (string.IsNullOrWhiteSpace(Record.Folder) ? "" : $" · 📁 {Core.Storage.SheetFolders.Name(Core.Storage.SheetFolders.Normalize(Record.Folder))}");
    public string Updated => Record.UpdatedAt.ToLocalTime().ToString("g");
}

public sealed partial class AbilityOption : ObservableObject
{
    private readonly Action<AbilityOption> _onToggle;
    public string Name { get; }
    public string Effect { get; }
    public string Tip => string.IsNullOrWhiteSpace(Effect) ? $"No data for \"{Name}\"" : Effect;

    [ObservableProperty] private bool _isSelected;

    public AbilityOption(string name, string effect, bool selected, Action<AbilityOption> onToggle)
    {
        Name = name;
        Effect = effect;
        _isSelected = selected;
        _onToggle = onToggle;
    }

    partial void OnIsSelectedChanged(bool value) => _onToggle(this);
}

/// <summary>One ability of abilities.json in the "add extra abilities" picker (checkable, several at once).</summary>
public sealed partial class AbilityPickVm : ObservableObject
{
    private readonly Action _onCheckedChanged;
    public string Name { get; }
    public string Effect { get; }
    public string Tip => string.IsNullOrWhiteSpace(Effect) ? $"No data for \"{Name}\"" : Effect;
    /// <summary>First line of the effect, shown next to the name in the picker.</summary>
    public string Summary { get; }

    [ObservableProperty] private bool _isChecked;

    public AbilityPickVm(string name, string effect, Action onCheckedChanged)
    {
        Name = name;
        Effect = effect;
        Summary = effect.Split('\n', 2)[0].Trim();
        _onCheckedChanged = onCheckedChanged;
    }

    partial void OnIsCheckedChanged(bool value) => _onCheckedChanged();
}

public sealed class AbilityTierVm
{
    public string Label { get; }
    public IReadOnlyList<AbilityOption> Options { get; }
    public IRelayCommand RollCommand { get; }
    public bool IsEmpty => Options.Count == 0;
    /// <summary>"usually Lv 40 · tutor" when the tier is taken before its usual level.</summary>
    public string Note { get; }
    public bool IsEarly => Note.Length > 0;

    public AbilityTierVm(string label, IReadOnlyList<AbilityOption> options, Action roll, Func<bool> canRoll, string note = "")
    {
        Label = label;
        Note = note;
        Options = options;
        RollCommand = new RelayCommand(roll, canRoll);
    }
}

public sealed partial class StatRowVm : ObservableObject
{
    private readonly Action<StatRowVm, int> _onPointsChanged;
    public Stat Stat { get; }
    public string Label { get; }

    [ObservableProperty] private int _base;
    [ObservableProperty] private int _points;
    [ObservableProperty] private string _buff = "";
    [ObservableProperty] private string _final = "";
    [ObservableProperty] private bool _isBuffed;
    [ObservableProperty] private bool _isLowered;

    public StatRowVm(Stat stat, string label, Action<StatRowVm, int> onPointsChanged)
    {
        Stat = stat;
        Label = label;
        _onPointsChanged = onPointsChanged;
    }

    /// <summary>Updates the value without notifying the parent (used by refreshes).</summary>
    public void SetPointsSilently(int value)
    {
        _suppress = true;
        Points = value;
        _suppress = false;
    }

    private bool _suppress;
    partial void OnPointsChanged(int value)
    {
        if (!_suppress) _onPointsChanged(this, value);
    }
}

public enum MoveKind { LevelUp, Egg, Tutor, Tm }

public sealed partial class MoveRowVm : ObservableObject
{
    private readonly Action<MoveRowVm, int> _onDbChanged;
    public MoveKind Kind { get; }
    public MoveData Move { get; }
    public string Name => Move.Name;
    public IReadOnlyList<string> Types => Move.Types;
    public string Frequency => Move.Frequency ?? "";
    public string AC => Move.AC ?? "";
    public string Class => Move.MoveClass ?? "";
    public string Range => Move.Range ?? "";
    public string Effect => Move.Effect ?? "";
    public bool CanBeLocked => Kind != MoveKind.LevelUp;
    /// <summary>Status moves can't get a DB bonus: the field is disabled for them.</summary>
    public bool CanHaveDbBuff => SheetRules.CanHaveDbBuff(Move);
    public double DbOpacity => CanHaveDbBuff ? 1 : 0.4;
    public string DbTip => CanHaveDbBuff
        ? "Extra damage base for this move (−1 / +1 buttons, arrow keys or mouse wheel)"
        : "Status moves can't get a damage base bonus";
    /// <summary>True for the 7th level-up move: a line separates the first six.</summary>
    public bool StartsSecondGroup { get; init; }

    [ObservableProperty] private string _finalRoll = "";
    [ObservableProperty] private int _dbBuff;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _canLock;

    public IRelayCommand RemoveCommand { get; }
    public IRelayCommand ToggleLockCommand { get; }
    public IRelayCommand DecreaseDbCommand { get; }
    public IRelayCommand IncreaseDbCommand { get; }
    public string LockLabel => IsLocked ? "Unlock" : "Lock";

    public MoveRowVm(MoveKind kind, MoveData move, int dbBuff, Action<MoveRowVm> remove, Action<MoveRowVm> toggleLock, Action<MoveRowVm, int> onDbChanged)
    {
        Kind = kind;
        Move = move;
        _dbBuff = SheetRules.CanHaveDbBuff(move) ? dbBuff : 0;
        _onDbChanged = onDbChanged;
        RemoveCommand = new RelayCommand(() => remove(this));
        ToggleLockCommand = new RelayCommand(() => toggleLock(this), () => IsLocked || CanLock);
        DecreaseDbCommand = new RelayCommand(() => DbBuff--, () => CanHaveDbBuff && DbBuff > 0);
        IncreaseDbCommand = new RelayCommand(() => DbBuff++, () => CanHaveDbBuff && DbBuff < SheetRules.MaxDbBuff);
    }

    partial void OnIsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(LockLabel));
        ToggleLockCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanLockChanged(bool value) => ToggleLockCommand.NotifyCanExecuteChanged();
    partial void OnDbBuffChanged(int value)
    {
        _onDbChanged(this, value);
        DecreaseDbCommand.NotifyCanExecuteChanged();
        IncreaseDbCommand.NotifyCanExecuteChanged();
    }
}

public sealed record MoveOption(string Name, string Label, IReadOnlyList<string> Types)
{
    public override string ToString() => Label;
}

public sealed record SheetMovePreview(int Index, MoveData Move)
{
    public string Number => (Index + 1).ToString();
    public string Types => Move.TypesLabel;
    public string Class => Core.Rules.SheetRules.FormatClass(Move.MoveClass);
    public string AC => string.Equals(Move.AC, "none", StringComparison.OrdinalIgnoreCase) ? "/" : Move.AC ?? "";
    public bool StartsSecondTable => Index == Core.Rules.SheetRules.FirstTableMoves;
}

public sealed record TeamSlotOption(int Index, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A nature rolled for the sheet; click to use it again.</summary>
public sealed record NatureRollVm(int Number, string Name, bool IsCurrent, ICommand Pick)
{
    public string Label => $"Roll {Number}: {Name}";
}
