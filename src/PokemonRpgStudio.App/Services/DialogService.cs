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

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public void Attach(Window window)
    {
        _notifications = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 3,
        };
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

    public async Task<string?> SaveFileAsync(string title, string suggestedName, FilePickerFileType type)
    {
        var window = MainWindow;
        if (window is null) return null;
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = type.Patterns?.FirstOrDefault()?.TrimStart('*', '.'),
            FileTypeChoices = new[] { type },
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> OpenFileAsync(string title, params FilePickerFileType[] types)
    {
        var files = await OpenFilesAsync(title, false, types);
        return files.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, bool multiple, params FilePickerFileType[] types)
    {
        var window = MainWindow;
        if (window is null) return Array.Empty<string>();
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = types.Length > 0 ? types : null,
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
    }

    public async Task<string?> OpenFolderAsync(string title)
    {
        var window = MainWindow;
        if (window is null) return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Delete")
    {
        var owner = MainWindow;
        if (owner is null) return false;
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
        if (owner is null) return null;
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

    private static IClipboard? Clipboard => MainWindow?.Clipboard;

    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(text);
    }

    /// <summary>Copies rich HTML (for pasting tables into Google Docs / Word) with a plain-text fallback.</summary>
    public async Task CopyHtmlAsync(string htmlFragment, string plainText)
    {
        if (Clipboard is not { } cb) return;
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
