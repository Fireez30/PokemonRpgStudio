namespace PokemonRpgStudio.App.Services;

/// <summary>What the platform offers (desktop or Android), for the few features that differ.</summary>
public static class PlatformInfo
{
    public static bool IsMobile { get; } = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    /// <summary>Opening a folder in a file manager (desktop only: Android apps can't show their private folders).</summary>
    public static bool CanOpenFolders { get; } = !IsMobile;
}
