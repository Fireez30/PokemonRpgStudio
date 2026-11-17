using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokemonRpgStudio.App.Services;
using PokemonRpgStudio.Core.Models;
using PokemonRpgStudio.Core.Storage;

namespace PokemonRpgStudio.App.ViewModels;

/// <summary>One row of a shop: name, quantity and price (mandatory), an optional picture and an optional effect.</summary>
public sealed partial class ShopItemVm : ObservableObject
{
    private readonly ShopsViewModel _owner;

    public ShopItem Item { get; }

    public ShopItemVm(ShopsViewModel owner, ShopItem item)
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
            OnPropertyChanged(nameof(IsNameMissing));
            OnPropertyChanged(nameof(DisplayName));
            _owner.ItemChanged();
        }
    }

    public bool IsNameMissing => string.IsNullOrWhiteSpace(Item.Name);

    /// <summary>NumericUpDown value (null when the field is cleared: read as 0).</summary>
    public decimal? Quantity
    {
        get => Item.Quantity;
        set
        {
            var v = (int)Math.Clamp(Math.Floor(value ?? 0), 0, int.MaxValue);
            if (Item.Quantity == v && value == v) return;
            Item.Quantity = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSoldOut));
            OnPropertyChanged(nameof(StockText));
            _owner.ItemChanged();
        }
    }

    public decimal? Price
    {
        get => Item.Price;
        set
        {
            var v = Math.Max(0, value ?? 0);
            if (Item.Price == v && value == v) return;
            Item.Price = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PriceText));
            _owner.ItemChanged();
        }
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
            _owner.ItemChanged();
        }
    }

    public bool HasEffect => !string.IsNullOrWhiteSpace(Item.Effect);

    public bool IsSoldOut => Item.Quantity == 0;

    public string DisplayName => string.IsNullOrWhiteSpace(Item.Name) ? "Unnamed item" : Item.Name.Trim();
    public string PriceText => ShopsViewModel.FormatPrice(Item.Price) + " ₽";
    public string StockText => Item.Quantity == 0 ? "Sold out" : $"{Item.Quantity} in stock";

    public string? PicturePath => _owner.Store.ImagePath(Item.Picture);
    public bool HasPicture => Item.Picture is not null;

    public void SetPicture(string? fileName)
    {
        Item.Picture = fileName;
        OnPropertyChanged(nameof(PicturePath));
        OnPropertyChanged(nameof(HasPicture));
        _owner.ItemChanged();
    }

    [RelayCommand]
    private Task ChoosePicture() => _owner.ChoosePictureAsync(this);

    [RelayCommand]
    private void RemovePicture() => SetPicture(null);

    [RelayCommand]
    private void Remove() => _owner.RemoveItem(this);
}

/// <summary>A shop of the list (left column) with its items.</summary>
public sealed partial class ShopVm : ObservableObject
{
    private readonly ShopsViewModel _owner;

    public Shop Shop { get; }
    public ObservableCollection<ShopItemVm> Items { get; }

    public ShopVm(ShopsViewModel owner, Shop shop)
    {
        _owner = owner;
        Shop = shop;
        Items = new ObservableCollection<ShopItemVm>(shop.Items.Select(i => new ShopItemVm(owner, i)));
    }

    public string Name
    {
        get => Shop.Name;
        set
        {
            if (Shop.Name == (value ?? "")) return;
            Shop.Name = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            _owner.ItemChanged();
        }
    }

    public string DisplayName => Shop.DisplayName;

    public string Summary
    {
        get
        {
            int count = Shop.Items.Count;
            return count == 1 ? "1 item" : $"{count} items";
        }
    }

    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));
}

/// <summary>
/// The shops (lists of items: name, quantity, price, optional picture) saved in <c>shops/shops.json</c>,
/// exported / imported as JSON files with embedded pictures. Shared by the two views of the Shops section
/// (<see cref="ShopViewerViewModel"/>, <see cref="ShopEditorViewModel"/>), which show the same selected shop.
/// </summary>
public sealed partial class ShopsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    public ShopStore Store { get; }
    public ShopBook Book { get; }

    public ObservableCollection<ShopVm> Shops { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShop), nameof(Items), nameof(StockSummary), nameof(HasNoItems))]
    [NotifyCanExecuteChangedFor(nameof(DeleteShopCommand), nameof(ExportShopCommand), nameof(AddItemCommand), nameof(DuplicateShopCommand))]
    private ShopVm? _selectedShop;

    public bool HasShop => SelectedShop is not null;
    public bool HasNoShops => Shops.Count == 0;
    public ObservableCollection<ShopItemVm>? Items => SelectedShop?.Items;
    public bool HasNoItems => SelectedShop is { Items.Count: 0 };

    public string StockSummary
    {
        get
        {
            if (SelectedShop is null) return "";
            var items = SelectedShop.Shop.Items;
            var units = items.Sum(i => (long)i.Quantity);
            var value = items.Sum(i => i.Quantity * i.Price);
            return $"{items.Count} item(s) · {units} in stock · stock value {FormatPrice(value)} ₽";
        }
    }

    public static string FormatPrice(decimal value) => value.ToString("#,0.##", CultureInfo.CurrentCulture);

    public ShopsViewModel(AppServices services)
    {
        _services = services;
        Store = new ShopStore(services.Library.Paths.Shops);
        Book = Store.Load();
        try { Store.DeleteUnusedImages(Book); }
        catch (Exception) { /* cleanup only */ }
        Shops = new ObservableCollection<ShopVm>(Book.Shops.Select(s => new ShopVm(this, s)));
        Shops.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoShops));
            ExportAllCommand.NotifyCanExecuteChanged();
        };
        _selectedShop = Shops.FirstOrDefault();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => Flush();
    }

    /// <summary>Called after any edit: refreshes the totals and saves shortly after.</summary>
    public void ItemChanged()
    {
        SelectedShop?.RefreshSummary();
        OnPropertyChanged(nameof(StockSummary));
        OnPropertyChanged(nameof(HasNoItems));
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
        catch (Exception ex) { _services.Dialogs.Notify("Could not save the shops: " + ex.Message, isError: true); }
    }

    // ---- Shops ----

    [RelayCommand]
    private void NewShop()
    {
        var shop = new Shop { Name = "New shop" };
        AddShops(new[] { shop });
    }

    private void AddShops(IReadOnlyList<Shop> shops)
    {
        ShopVm? first = null;
        foreach (var shop in shops)
        {
            Book.Shops.Add(shop);
            var vm = new ShopVm(this, shop);
            Shops.Add(vm);
            first ??= vm;
        }
        if (first is not null) SelectedShop = first;
        ItemChanged();
    }

    [RelayCommand(CanExecute = nameof(HasShop))]
    private void DuplicateShop()
    {
        if (SelectedShop is not { } source) return;
        var copy = new Shop
        {
            Name = source.DisplayName + " (copy)",
            Items = source.Shop.Items.Select(i =>
            {
                var c = i.Clone();
                // Each item owns its picture file (unused files are deleted at the next start).
                var path = Store.ImagePath(i.Picture);
                c.Picture = path is not null && File.Exists(path) ? Store.ImportImage(path) : null;
                return c;
            }).ToList(),
        };
        AddShops(new[] { copy });
    }

    [RelayCommand(CanExecute = nameof(HasShop))]
    private async Task DeleteShop()
    {
        if (SelectedShop is not { } shop) return;
        if (shop.Items.Count > 0 &&
            !await _services.Dialogs.ConfirmAsync("Delete shop", $"Delete the shop \"{shop.DisplayName}\" and its {shop.Summary}? This cannot be undone."))
            return;
        int index = Shops.IndexOf(shop);
        Book.Shops.Remove(shop.Shop);
        Shops.Remove(shop);
        SelectedShop = Shops.Count == 0 ? null : Shops[Math.Clamp(index, 0, Shops.Count - 1)];
        ItemChanged();
        Flush();
        try { Store.DeleteUnusedImages(Book); }
        catch (Exception) { /* cleanup only */ }
    }

    // ---- Items ----

    [RelayCommand(CanExecute = nameof(HasShop))]
    private void AddItem()
    {
        if (SelectedShop is not { } shop) return;
        var item = new ShopItem { Quantity = 1 };
        shop.Shop.Items.Add(item);
        shop.Items.Add(new ShopItemVm(this, item));
        ItemChanged();
    }

    public void RemoveItem(ShopItemVm item)
    {
        if (SelectedShop is not { } shop) return;
        shop.Shop.Items.Remove(item.Item);
        shop.Items.Remove(item);
        ItemChanged();
    }

    public async Task ChoosePictureAsync(ShopItemVm item)
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

    // ---- Export / import ----

    [RelayCommand(CanExecute = nameof(HasShop))]
    private Task ExportShop() =>
        SelectedShop is { } shop ? ExportAsync(new[] { shop.Shop }, SafeFileName(shop.DisplayName)) : Task.CompletedTask;

    private bool HasAnyShop() => Shops.Count > 0;

    [RelayCommand(CanExecute = nameof(HasAnyShop))]
    private Task ExportAll() => ExportAsync(Book.Shops, "shops");

    private async Task ExportAsync(IReadOnlyList<Shop> shops, string suggestedName)
    {
        var unnamed = shops.SelectMany(s => s.Items.Where(i => string.IsNullOrWhiteSpace(i.Name)).Select(_ => s.DisplayName)).ToList();
        if (unnamed.Count > 0)
        {
            _services.Dialogs.Notify($"Every item needs a name before exporting ({unnamed.Count} unnamed, in {string.Join(", ", unnamed.Distinct())}).", isError: true);
            return;
        }
        try
        {
            Flush();
            var json = Store.Export(shops);
            if (!await _services.Dialogs.SaveFileAsync("Export shops", suggestedName + ".json", DialogService.JsonFiles,
                    path => File.WriteAllTextAsync(path, json))) return;
            _services.Dialogs.Notify(shops.Count == 1 ? "Shop exported." : $"{shops.Count} shops exported.");
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Could not export: " + ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        var path = await _services.Dialogs.OpenFileAsync("Import shops (.json)", DialogService.JsonFiles);
        if (path is null) return;
        var warnings = new List<string>();
        try
        {
            var shops = Store.Import(await File.ReadAllTextAsync(path), Path.GetFileNameWithoutExtension(path),
                Path.GetDirectoryName(path), warnings);
            if (shops.Count == 0)
            {
                _services.Dialogs.Notify("This file holds no shop.", isError: true);
                return;
            }
            AddShops(shops);
            Flush();
            int items = shops.Sum(s => s.Items.Count);
            var message = $"Imported {shops.Count} shop(s), {items} item(s).";
            if (warnings.Count > 0)
                message += $" {warnings.Count} problem(s):\n" + string.Join("\n", warnings.Take(6)) + (warnings.Count > 6 ? "\n…" : "");
            _services.Dialogs.Notify(message, isError: warnings.Count > 0, title: "Shops import");
        }
        catch (Exception ex)
        {
            _services.Dialogs.Notify("Invalid shops file: " + ex.Message, isError: true);
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "shop" : clean;
    }
}

/// <summary>Page of the Shops section: both views work on the same <see cref="ShopsViewModel"/>.</summary>
public abstract class ShopPageViewModel : PageViewModel
{
    public ShopsViewModel Shops { get; }

    protected ShopPageViewModel(ShopsViewModel shops) => Shops = shops;

    public override void OnDeactivated() => Shops.Flush();
}

/// <summary>Read-only view of a shop: its items as cards with picture, price and stock.</summary>
public sealed partial class ShopViewerViewModel : ShopPageViewModel
{
    private readonly Action _openEditor;

    public override string Title => "Shop view";
    public override string Glyph => "🛒";

    public ShopViewerViewModel(ShopsViewModel shops, Action openEditor) : base(shops) => _openEditor = openEditor;

    [RelayCommand]
    private void Edit() => _openEditor();
}

/// <summary>Editing view of the shops: create, rename, delete, import / export, edit the items.</summary>
public sealed class ShopEditorViewModel : ShopPageViewModel
{
    public override string Title => "Edit shops";
    public override string Glyph => "✏";

    public ShopEditorViewModel(ShopsViewModel shops) : base(shops) { }
}
