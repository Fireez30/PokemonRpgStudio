using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>An item of the wiki: name, section, effect, description and an optional picture.</summary>
public sealed partial class WikiItemVm : ObservableObject
{
    private readonly ItemWikiViewModel _owner;
    private string _soldIn = "";

    public WikiItem Item { get; }

    public WikiItemVm(ItemWikiViewModel owner, WikiItem item)
    {
        _owner = owner;
        Item = item;
    }

    public string Name
    {
        get => Item.Name;
        set
        {
            if (Item.Name == (value ?? "")) return;
            Item.Name = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IsNameMissing));
            _owner.ItemRenamed(this);
        }
    }

    public string DisplayName => Item.DisplayName;
    public bool IsNameMissing => string.IsNullOrWhiteSpace(Item.Name);

    [ObservableProperty] private bool _isDuplicate;

    /// <summary>Section shown in the section picker ("Unsorted" for none). Moves the item.</summary>
    public string? SectionChoice
    {
        get => Item.Section.Length == 0 ? ItemWikiViewModel.UnsortedLabel : Item.Section;
        set
        {
            // The picker clears its selection when its list is rebuilt: not a move.
            if (value is null) return;
            _owner.MoveItem(this, value == ItemWikiViewModel.UnsortedLabel ? "" : value, refilter: false);
        }
    }

    public string SectionLabel => Item.Section.Length == 0 ? ItemWikiViewModel.UnsortedLabel : Item.Section;

    public void SectionChanged()
    {
        OnPropertyChanged(nameof(SectionChoice));
        OnPropertyChanged(nameof(SectionLabel));
    }

    public string Effect
    {
        get => Item.Effect;
        set
        {
            if (Item.Effect == (value ?? "")) return;
            Item.Effect = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEffect));
            _owner.Changed();
        }
    }

    public bool HasEffect => !string.IsNullOrWhiteSpace(Item.Effect);

    public string Description
    {
        get => Item.Description;
        set
        {
            if (Item.Description == (value ?? "")) return;
            Item.Description = value ?? "";
            OnPropertyChanged();
            _owner.Changed();
        }
    }

    /// <summary>The shops selling the item (same name), with price and stock; empty when none.</summary>
    public string SoldIn
    {
        get => _soldIn;
        set
        {
            if (SetProperty(ref _soldIn, value)) OnPropertyChanged(nameof(IsSold));
        }
    }

    public bool IsSold => _soldIn.Length > 0;

    public string? PicturePath => _owner.Store.ImagePath(Item.Picture);
    public bool HasPicture => Item.Picture is not null;

    public void SetPicture(string? fileName)
    {
        Item.Picture = fileName;
        OnPropertyChanged(nameof(PicturePath));
        OnPropertyChanged(nameof(HasPicture));
        _owner.Changed();
    }

    [RelayCommand]
    private Task ChoosePicture() => _owner.ChoosePictureAsync(this);

    [RelayCommand]
    private void RemovePicture() => SetPicture(null);
}

/// <summary>An entry of the sections column: every item, the unsorted items, or one section.</summary>
public sealed partial class WikiSectionVm : ObservableObject
{
    /// <summary>null: every item; "": unsorted items; otherwise the section's name.</summary>
    public string? Section { get; }
    public string Glyph { get; }
    public string Label { get; }
    public bool IsUserSection => !string.IsNullOrEmpty(Section);

    [ObservableProperty] private int _count;

    public WikiSectionVm(string? section)
    {
        Section = section;
        Glyph = section is null ? "📚" : section.Length == 0 ? "📥" : "📁";
        Label = section is null ? "All items" : section.Length == 0 ? ItemWikiViewModel.UnsortedLabel : section;
    }

    /// <summary>True when items can be dropped on this entry (a section or "Unsorted").</summary>
    public bool AcceptsItems => Section is not null;
}

/// <summary>
/// The item wiki (Tools menu): every item of the game, sorted in sections. It holds its own items, and the
/// items sold in the shops are added to it automatically (each time the page is shown) when it doesn't have them.
/// Saved in <c>wiki/items.json</c>.
/// </summary>
public sealed partial class ItemWikiViewModel : PageViewModel
{
    public const string UnsortedLabel = "Unsorted";

    private readonly AppServices _services;
    private readonly ShopsViewModel _shops;
    private readonly DispatcherTimer _saveTimer;
    private readonly List<WikiItemVm> _items;
    private Dictionary<string, string> _soldIn = new();
    private bool _dirty;

    public override string Title => "Item Wiki";
    public override string Glyph => "📖";

    public ItemWikiStore Store { get; }
    public ItemWikiBook Book { get; }

    public ObservableCollection<WikiSectionVm> Sections { get; } = new();
    public ObservableCollection<WikiItemVm> VisibleItems { get; } = new();

    /// <summary>Choices of the section picker of an item: "Unsorted", then the sections.</summary>
    [ObservableProperty] private IReadOnlyList<string> _sectionChoices = Array.Empty<string>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUserSectionSelected), nameof(ListTitle))]
    [NotifyCanExecuteChangedFor(nameof(RenameSectionCommand), nameof(DeleteSectionCommand), nameof(MoveSectionUpCommand), nameof(MoveSectionDownCommand))]
    private WikiSectionVm? _selectedSection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem))]
    [NotifyCanExecuteChangedFor(nameof(DeleteItemCommand))]
    private WikiItemVm? _selectedItem;

    [ObservableProperty] private string _searchText = "";

    public bool HasItem => SelectedItem is not null;
    public bool IsUserSectionSelected => SelectedSection is { IsUserSection: true };
    public bool HasNoVisibleItems => VisibleItems.Count == 0;
    public string ListTitle => SelectedSection?.Label ?? "All items";

    public string CountText
    {
        get
        {
            int n = VisibleItems.Count;
            return n == 1 ? "1 item" : $"{n} items";
        }
    }

    public ItemWikiViewModel(AppServices services, ShopsViewModel shops)
    {
        _services = services;
        _shops = shops;
        Store = new ItemWikiStore(services.Library.Paths.ItemWiki);
        Book = Store.Load();
        try { Store.DeleteUnusedImages(Book); }
        catch (Exception) { /* cleanup only */ }
        _items = Book.Items.Select(i => new WikiItemVm(this, i)).ToList();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => Flush();
        VisibleItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoVisibleItems));
            OnPropertyChanged(nameof(CountText));
        };
        RefreshDuplicates();
        RebuildSections(); // selects "All items", which fills the list
    }

    public override void OnActivated()
    {
        // The shops may have changed since the last visit: add their new items and refresh "sold in".
        int added = AddShopItems();
        if (added > 0)
            _services.Dialogs.Notify(added == 1 ? "1 item added from the shops (in Unsorted)." : $"{added} items added from the shops (in Unsorted).");
    }

    public override void OnDeactivated() => Flush();

    /// <summary>Called after any edit: saves shortly after.</summary>
    public void Changed()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending changes now.</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        try { Store.Save(Book); }
        catch (Exception ex) { _services.Dialogs.Notify("Could not save the item wiki: " + ex.Message, isError: true); }
    }

    // ---- Shops ----

    /// <summary>Adds the shop items the wiki doesn't have, refreshes "sold in"; returns the number of added items.</summary>
    private int AddShopItems()
    {
        var added = Book.AddShopItems(_shops.Book, picture =>
        {
            var path = _shops.Store.ImagePath(picture);
            return path is not null && File.Exists(path) ? Store.ImportImage(path) : null;
        });
        foreach (var item in added) _items.Add(new WikiItemVm(this, item));
        RefreshSoldIn();
        if (added.Count > 0)
        {
            Changed();
            RefreshDuplicates();
            RefreshCounts();
            // The new items are in Unsorted: show them when one of the user's sections is shown.
            if (IsUserSectionSelected) SelectedSection = Sections.First(s => s.Section == "");
            else Refilter();
        }
        return added.Count;
    }

    [RelayCommand]
    private void ScanShops()
    {
        int added = AddShopItems();
        _services.Dialogs.Notify(added switch
        {
            0 => "Every item of the shops is already in the wiki.",
            1 => "1 item added from the shops (in Unsorted).",
            _ => $"{added} items added from the shops (in Unsorted).",
        });
    }

    private void RefreshSoldIn()
    {
        _soldIn = _shops.Book.Shops
            .SelectMany(s => s.Items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).Select(i => (Shop: s, Item: i)))
            .GroupBy(p => ItemWikiBook.KeyOf(p.Item.Name))
            .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(p =>
                $"{p.Shop.DisplayName}: {ShopsViewModel.FormatPrice(p.Item.Price)} ₽ · {(p.Item.Quantity == 0 ? "sold out" : $"{p.Item.Quantity} in stock")}")));
        foreach (var item in _items) UpdateSoldIn(item);
    }

    private void UpdateSoldIn(WikiItemVm item) =>
        item.SoldIn = _soldIn.TryGetValue(ItemWikiBook.KeyOf(item.Name), out var text) ? text : "";

    // ---- Sections ----

    private void RebuildSections()
    {
        var selected = SelectedSection?.Section;
        Sections.Clear();
        Sections.Add(new WikiSectionVm(null));
        Sections.Add(new WikiSectionVm(""));
        foreach (var s in Book.Sections) Sections.Add(new WikiSectionVm(s));
        SectionChoices = new[] { UnsortedLabel }.Concat(Book.Sections).ToList();
        foreach (var item in _items) item.SectionChanged();
        RefreshCounts();
        SelectedSection = Sections.FirstOrDefault(s => s.Section == selected) ?? Sections[0];
    }

    private void RefreshCounts()
    {
        foreach (var s in Sections)
            s.Count = s.Section is null ? _items.Count : _items.Count(i => ItemWikiBook.KeyOf(i.Item.Section) == ItemWikiBook.KeyOf(s.Section));
    }

    partial void OnSelectedSectionChanged(WikiSectionVm? value)
    {
        if (value is null)
        {
            // A list box can briefly clear its selection while rebuilt: fall back to "All items".
            Dispatcher.UIThread.Post(() => { if (SelectedSection is null && Sections.Count > 0) SelectedSection = Sections[0]; });
            return;
        }
        Refilter();
    }

    /// <summary>Asks for a section name; null when cancelled, taken or reserved (the user is told).</summary>
    private async Task<string?> AskSectionNameAsync(string title, string initial, string? current)
    {
        var name = await _services.Dialogs.PromptAsync(title, "Name of the section:", initial, watermark: "e.g. Medicine, Poké Balls, Key items");
        if (name is null) return null;
        if (ItemWikiBook.KeyOf(name) == ItemWikiBook.KeyOf(UnsortedLabel))
        {
            _services.Dialogs.Notify($"\"{UnsortedLabel}\" is reserved for the items without a section.", isError: true);
            return null;
        }
        if (Book.FindSection(name) is { } other && ItemWikiBook.KeyOf(other) != ItemWikiBook.KeyOf(current))
        {
            _services.Dialogs.Notify($"There is already a section named \"{other}\".", isError: true);
            return null;
        }
        return name;
    }

    [RelayCommand]
    private async Task NewSection()
    {
        if (await AskSectionNameAsync("New section", "", null) is not { } name) return;
        Book.Sections.Add(name);
        RebuildSections();
        SelectedSection = Sections.FirstOrDefault(s => s.Section == name);
        Changed();
    }

    [RelayCommand(CanExecute = nameof(IsUserSectionSelected))]
    private async Task RenameSection()
    {
        if (SelectedSection?.Section is not { Length: > 0 } section) return;
        if (await AskSectionNameAsync("Rename section", section, section) is not { } name || name == section) return;
        if (!Book.RenameSection(section, name)) return;
        SelectedSection = null;
        RebuildSections();
        SelectedSection = Sections.FirstOrDefault(s => s.Section == name) ?? Sections[0];
        Changed();
    }

    [RelayCommand(CanExecute = nameof(IsUserSectionSelected))]
    private async Task DeleteSection()
    {
        if (SelectedSection is not { Section: { Length: > 0 } section, Count: var count }) return;
        if (count > 0 && !await _services.Dialogs.ConfirmAsync("Delete section",
                $"Delete the section \"{section}\"? Its {(count == 1 ? "item goes" : $"{count} items go")} to {UnsortedLabel}; no item is deleted."))
            return;
        Book.RemoveSection(section);
        SelectedSection = null;
        RebuildSections();
        Changed();
    }

    private bool CanMoveSectionUp() => IsUserSectionSelected && Book.Sections.IndexOf(SelectedSection!.Section!) > 0;
    private bool CanMoveSectionDown() =>
        IsUserSectionSelected && Book.Sections.IndexOf(SelectedSection!.Section!) is var i && i >= 0 && i < Book.Sections.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveSectionUp))]
    private void MoveSectionUp() => ShiftSection(-1);

    [RelayCommand(CanExecute = nameof(CanMoveSectionDown))]
    private void MoveSectionDown() => ShiftSection(1);

    private void ShiftSection(int delta)
    {
        if (SelectedSection?.Section is not { Length: > 0 } section) return;
        int i = Book.Sections.IndexOf(section), j = i + delta;
        if (i < 0 || j < 0 || j >= Book.Sections.Count) return;
        (Book.Sections[i], Book.Sections[j]) = (Book.Sections[j], Book.Sections[i]);
        RebuildSections();
        MoveSectionUpCommand.NotifyCanExecuteChanged();
        MoveSectionDownCommand.NotifyCanExecuteChanged();
        Changed();
    }

    // ---- Items ----

    partial void OnSearchTextChanged(string value) => Refilter();

    private bool Matches(WikiItemVm item)
    {
        if (SelectedSection is { Section: { } section } && ItemWikiBook.KeyOf(item.Item.Section) != ItemWikiBook.KeyOf(section)) return false;
        var search = SearchText.Trim();
        return search.Length == 0
               || item.Item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || item.Item.Effect.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || item.Item.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Refilter()
    {
        var selected = SelectedItem;
        VisibleItems.Clear();
        foreach (var item in _items.Where(Matches).OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            VisibleItems.Add(item);
        SelectedItem = selected is not null && VisibleItems.Contains(selected) ? selected : VisibleItems.FirstOrDefault();
    }

    [RelayCommand]
    private void NewItem()
    {
        var item = new WikiItem { Section = SelectedSection?.Section ?? "" };
        Book.Items.Add(item);
        var vm = new WikiItemVm(this, item);
        _items.Add(vm);
        // Shown even when a search would hide it, at the top so it's easy to find.
        VisibleItems.Insert(0, vm);
        SelectedItem = vm;
        RefreshCounts();
        Changed();
    }

    [RelayCommand(CanExecute = nameof(HasItem))]
    private async Task DeleteItem()
    {
        if (SelectedItem is not { } item) return;
        var message = $"Delete \"{item.DisplayName}\" from the wiki?";
        if (item.IsSold) message += " A shop still sells it, so it will be added back the next time the shops are scanned.";
        if (!await _services.Dialogs.ConfirmAsync("Delete item", message)) return;
        int index = VisibleItems.IndexOf(item);
        Book.Items.Remove(item.Item);
        _items.Remove(item);
        VisibleItems.Remove(item);
        SelectedItem = VisibleItems.Count == 0 ? null : VisibleItems[Math.Clamp(index, 0, VisibleItems.Count - 1)];
        RefreshCounts();
        RefreshDuplicates();
        Changed();
        Flush();
        try { Store.DeleteUnusedImages(Book); }
        catch (Exception) { /* cleanup only */ }
    }

    /// <summary>
    /// Moves an item to a section ("" for Unsorted). With <paramref name="refilter"/> (drag and drop), the item
    /// leaves the list when it no longer belongs to the section shown; from the item's picker it stays visible.
    /// </summary>
    public void MoveItem(WikiItemVm item, string section, bool refilter)
    {
        if (section.Length > 0) section = Book.FindSection(section) ?? section;
        if (item.Item.Section == section) return;
        if (section.Length > 0 && Book.FindSection(section) is null) return;
        item.Item.Section = section;
        item.SectionChanged();
        RefreshCounts();
        if (refilter && !Matches(item)) VisibleItems.Remove(item);
        Changed();
    }

    /// <summary>Drag and drop of an item (by id) on an entry of the sections column.</summary>
    public void DropItem(string itemId, WikiSectionVm target)
    {
        if (target.Section is not { } section) return;
        if (_items.FirstOrDefault(i => i.Item.Id == itemId) is { } item) MoveItem(item, section, refilter: true);
    }

    public void ItemRenamed(WikiItemVm item)
    {
        UpdateSoldIn(item);
        RefreshDuplicates();
        Changed();
    }

    private void RefreshDuplicates()
    {
        var counts = _items.Where(i => !i.IsNameMissing).GroupBy(i => ItemWikiBook.KeyOf(i.Name)).ToDictionary(g => g.Key, g => g.Count());
        foreach (var item in _items)
            item.IsDuplicate = !item.IsNameMissing && counts.GetValueOrDefault(ItemWikiBook.KeyOf(item.Name)) > 1;
    }

    public async Task ChoosePictureAsync(WikiItemVm item)
    {
        var file = await _services.Dialogs.OpenFileAsync("Choose the item's picture", DialogService.ImageFiles);
        if (file is null) return;
        try
        {
            var name = Store.ImportImage(file);
            if (BitmapCache.Load(Store.ImagePath(name)) is null)
            {
                _services.Dialogs.Notify("This image can't be read.", isError: true);
                return;
            }
            item.SetPicture(name);
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Could not add the picture: " + ex.Message, isError: true);
        }
    }
}
