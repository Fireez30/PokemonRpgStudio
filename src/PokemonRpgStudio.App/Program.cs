using Avalonia;

namespace PokemonRpgStudio.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine($"Pokemon RPG Studio {typeof(Program).Assembly.GetName().Version?.ToString(3)}");
            return;
        }
        if (args.Contains("--check"))
        {
            // Packaging self-test: loads the bundled data without opening a window.
            try
            {
                var services = Services.AppServices.Initialize();
                var images = Core.Data.ImageResolver.CountImages(services.ImagesDir);
                Console.WriteLine($"OK: {services.Data.Pokemon.Count} Pokémon, {services.Data.Moves.Count} moves, " +
                                  $"{services.Data.Abilities.Count} abilities, {images} sprites");
                Console.WriteLine($"Data folder: {services.DataDir}" +
                                  (services.SeededFiles.Count > 0 ? $" (just copied from the defaults: {string.Join(", ", services.SeededFiles)})" : ""));
                Console.WriteLine($"Sprites folder: {services.ImagesDir}" +
                                  (services.SeededImages > 0 ? $" ({services.SeededImages} sprites just copied from the defaults)" : ""));
                Console.WriteLine($"User folder: {services.Library.Paths.Root}");
                Environment.Exit(images > 0 ? 0 : 2);
            }
            catch (Core.Data.MissingDataException ex)
            {
                Console.Error.WriteLine($"MISSING DATA: {string.Join(", ", ex.Missing)} in {ex.DataDir} " +
                                        "(put the PokemonRpgStudio-data folder next to the application, or start it to pick the files)");
                Environment.Exit(3);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAILED: " + ex.Message);
                Environment.Exit(1);
            }
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
