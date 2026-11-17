using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace PokemonRpgStudio.App.Services;

/// <summary>File pickers, confirmations, notifications and clipboard access.</summary>
public sealed class DialogService
{
    private WindowNotificationManager? _notifications;
    private Panel? _overlayHost;
    private Action? _cancelOverlay;

    /// <summary>Desktop: the main window (dialogs are separate windows).</summary>
    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>The top level of the application: the main window, or the single view (Android).</summary>
    private static TopLevel? Top => MainWindow ?? TopLevel.GetTopLevel(
        (Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView);

    /// <summary>
    /// Shows notifications on <paramref name="topLevel"/>. <paramref name="overlayHost"/>: panel covering the
    /// application where dialogs are shown when there are no windows (Android).
    /// </summary>
    public void Attach(TopLevel topLevel, Panel? overlayHost = null)
    {
        _overlayHost = overlayHost;
        _notifications = new WindowNotificationManager(topLevel)
        {
            Position = PlatformInfo.IsMobile ? NotificationPosition.BottomCenter : NotificationPosition.BottomRight,
            MaxItems = 3,
        };
    }

    /// <summary>True while an in-app dialog is shown.</summary>
    public bool IsDialogOpen => _cancelOverlay != null;

    /// <summary>Closes the in-app dialog as if Cancel was pressed (Android back button).</summary>
    public bool CancelDialog()
    {
        if (_cancelOverlay is not { } cancel) return false;
        cancel();
        return true;
    }

    public void Notify(string message, bool isError = false, string? title = null)
    {
        _notifications?.Show(new Notification(
            title ?? (isError ? "Error" : "Pokémon RPG Studio"),
            message,
            isError ? NotificationType.Error : NotificationType.Information,
            TimeSpan.FromSeconds(isError || message.Length > 120 ? 8 : 3)));
    }

    public static readonly FilePickerFileType JsonFiles = new("JSON files") { Patterns = new[] { "*.json" }, MimeTypes = new[] { "application/json" } };
    public static readonly FilePickerFileType SheetFiles = new("Sheets (.json, .md)") { Patterns = new[] { "*.json", "*.md", "*.markdown" } };
    public static readonly FilePickerFileType MarkdownFiles = new("Markdown") { Patterns = new[] { "*.md" }, MimeTypes = new[] { "text/markdown" } };
    public static readonly FilePickerFileType HtmlFiles = new("HTML page") { Patterns = new[] { "*.html" }, MimeTypes = new[] { "text/html" } };
    public static readonly FilePickerFileType PngFiles = new("PNG image") { Patterns = new[] { "*.png" }, MimeTypes = new[] { "image/png" } };
    public static readonly FilePickerFileType ImageFiles = new("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" } };

    /// <summary>
    /// Asks where to save, then calls <paramref name="write"/> with a local path to write the file to.
    /// When the picked file has no local path (Android documents), it is written to a temporary file that is
    /// then copied into the picked one. Returns false when cancelled.
    /// </summary>
    public async Task<bool> SaveFileAsync(string title, string suggestedName, FilePickerFileType type, Func<string, Task> write)
    {
        if (Top is not { } top) return false;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = type.Patterns?.FirstOrDefault()?.TrimStart('*', '.'),
            FileTypeChoices = new[] { type },
            ShowOverwritePrompt = true,
        });
        if (file is null) return false;
        using (file)
        {
            if (file.TryGetLocalPath() is { } local)
            {
                await write(local);
                return true;
            }
            var temp = Path.Combine(TempFolder("export"), SafeName(file.Name, suggestedName));
            try
            {
                await write(temp);
                await using var source = File.OpenRead(temp);
                await using var target = await file.OpenWriteAsync();
                await source.CopyToAsync(target);
            }
            finally
            {
                TryDelete(temp);
            }
            return true;
        }
    }

    /// <inheritdoc cref="SaveFileAsync(string, string, FilePickerFileType, Func{string, Task})"/>
    public Task<bool> SaveFileAsync(string title, string suggestedName, FilePickerFileType type, Action<string> write) =>
        SaveFileAsync(title, suggestedName, type, path => { write(path); return Task.CompletedTask; });

    public async Task<string?> OpenFileAsync(string title, params FilePickerFileType[] types)
    {
        var files = await OpenFilesAsync(title, false, types);
        return files.FirstOrDefault();
    }

    /// <summary>
    /// Picks files and returns local paths. Files without one (Android documents) are copied to a temporary
    /// folder under their own name (the name matters: sprites, imports).
    /// </summary>
    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, bool multiple, params FilePickerFileType[] types)
    {
        if (Top is not { } top) return Array.Empty<string>();
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            // Android pickers filter on MIME types, which many of these files don't have (.md, .json from other apps).
            FileTypeFilter = types.Length > 0 && !PlatformInfo.IsMobile ? types : null,
        });
        var paths = new List<string>();
        string? tempDir = null;
        foreach (var file in files)
        {
            using (file)
            {
                if (file.TryGetLocalPath() is { } local)
                {
                    paths.Add(local);
                    continue;
                }
                tempDir ??= TempFolder("import");
                var copy = UniquePath(tempDir, SafeName(file.Name, "file"));
                await using var source = await file.OpenReadAsync();
                await using var target = File.Create(copy);
                await source.CopyToAsync(target);
                paths.Add(copy);
            }
        }
        return paths;
    }

    /// <summary>Picks a folder; on Android its JSON / Markdown files (sub-folders included) are copied to a temporary folder.</summary>
    public async Task<string?> OpenFolderAsync(string title)
    {
        if (Top is not { } top) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        var folder = folders.FirstOrDefault();
        if (folder is null) return null;
        if (folder.TryGetLocalPath() is { } local) return local;
        var copy = TempFolder("import-folder");
        await CopyFolderAsync(folder, copy, depth: 0);
        return copy;
    }

    private static async Task CopyFolderAsync(IStorageFolder folder, string target, int depth)
    {
        Directory.CreateDirectory(target);
        await foreach (var item in folder.GetItemsAsync())
        {
            if (item is IStorageFolder sub)
            {
                if (depth < 4) await CopyFolderAsync(sub, Path.Combine(target, SafeName(sub.Name, "folder")), depth + 1);
            }
            else if (item is IStorageFile file && Path.GetExtension(file.Name).ToLowerInvariant() is ".json" or ".md" or ".markdown")
            {
                await using var source = await file.OpenReadAsync();
                await using var output = File.Create(Path.Combine(target, SafeName(file.Name, "file")));
                await source.CopyToAsync(output);
            }
            item.Dispose();
        }
    }

    /// <summary>A new empty folder in the temporary folder (picked files are copied there).</summary>
    private static string TempFolder(string kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "PokemonRpgStudio", kind);
        // Copies of earlier picks are no longer needed.
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (Exception) { /* in use: a new sub-folder is used anyway */ }
        var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SafeName(string? name, string fallback)
    {
        name = Path.GetFileName(name ?? "");
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string UniquePath(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { /* temporary file */ }
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Delete")
    {
        var owner = MainWindow;
        if (owner is null)
        {
            var confirm = new Button { Content = confirmText, Classes = { "danger" }, MinWidth = 90 };
            return await ShowOverlayAsync(title, new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                confirm, () => true, cancelResult: false);
        }
        var result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var ok = new Button { Content = confirmText, Classes = { "danger" }, MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Asks for a line of text (null when cancelled or left empty).</summary>
    public async Task<string?> PromptAsync(string title, string message, string initial = "", string confirmText = "OK", string? watermark = null)
    {
        var owner = MainWindow;
        if (owner is null)
        {
            var input = new TextBox { Text = initial, Watermark = watermark };
            var confirm = new Button { Content = confirmText, Classes = { "accent" }, MinWidth = 90, IsDefault = true };
            var body = new StackPanel
            {
                Spacing = 12,
                Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, input },
            };
            input.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { input.Focus(); input.SelectAll(); });
            var text = await ShowOverlayAsync(title, body, confirm, () => input.Text, cancelResult: null);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        string? result = null;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var box = new TextBox { Text = initial, Watermark = watermark };
        var ok = new Button { Content = confirmText, Classes = { "accent" }, MinWidth = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    /// <summary>
    /// Dialog drawn inside the application (no windows on Android): a card over a dimmed background,
    /// closed by its buttons, a tap outside or the back button (cancel).
    /// </summary>
    private async Task<T> ShowOverlayAsync<T>(string title, Control body, Button confirm, Func<T> confirmResult, T cancelResult)
    {
        if (_overlayHost is not { } host) return cancelResult;
        _cancelOverlay?.Invoke(); // one dialog at a time
        var done = new TaskCompletionSource<T>();
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        var card = new Border
        {
            Classes = { "card", "dialog" },
            MaxWidth = 460,
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    body,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, confirm },
                    },
                },
            },
        };
        var scrim = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x99, 0, 0, 0)) };
        var layer = new Panel { Children = { scrim, card } };

        void Close(T value)
        {
            if (done.Task.IsCompleted) return;
            host.Children.Remove(layer);
            host.IsVisible = host.Children.Count > 0;
            _cancelOverlay = null;
            done.TrySetResult(value);
        }
        confirm.Click += (_, _) => Close(confirmResult());
        cancel.Click += (_, _) => Close(cancelResult);
        scrim.PointerPressed += (_, _) => Close(cancelResult);
        layer.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(cancelResult); e.Handled = true; }
            else if (e.Key == Key.Enter && confirm.IsDefault) { Close(confirmResult()); e.Handled = true; }
        };
        _cancelOverlay = () => Close(cancelResult);
        host.Children.Add(layer);
        host.IsVisible = true;
        return await done.Task;
    }

    private static IClipboard? Clipboard => Top?.Clipboard;

    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(text);
    }

    /// <summary>Copies rich HTML (for pasting tables into Google Docs / Word) with a plain-text fallback.</summary>
    public async Task CopyHtmlAsync(string htmlFragment, string plainText)
    {
        if (Clipboard is not { } cb) return;
        if (PlatformInfo.IsMobile)
        {
            // Android's clipboard takes plain text only.
            await cb.SetTextAsync(plainText);
            return;
        }
        var item = new DataTransferItem();
        item.SetText(plainText);
        // Windows expects the CF_HTML envelope; X11/Wayland/macOS use plain text/html.
        if (OperatingSystem.IsWindows())
            item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"),
                Encoding.UTF8.GetBytes(Core.Export.SheetExporter.ToWindowsClipboardHtml(htmlFragment)));
        else
            item.Set(DataFormat.CreateBytesPlatformFormat("text/html"),
                Encoding.UTF8.GetBytes("<meta charset=\"utf-8\">" + htmlFragment));
        var transfer = new DataTransfer();
        transfer.Add(item);
        await cb.SetDataAsync(transfer);
    }

    public void OpenInFileManager(string path)
    {
        if (!PlatformInfo.CanOpenFolders)
        {
            Notify("Folder: " + path);
            return;
        }
        try
        {
            var psi = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"")
                : OperatingSystem.IsMacOS()
                    ? new System.Diagnostics.ProcessStartInfo("open", $"\"{path}\"")
                    : new System.Diagnostics.ProcessStartInfo("xdg-open", $"\"{path}\"");
            psi.UseShellExecute = false;
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Notify("Could not open the folder: " + ex.Message, isError: true);
        }
    }
}
