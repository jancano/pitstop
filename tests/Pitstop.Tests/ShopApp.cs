namespace Pitstop.Tests;

internal static class ShopApp
{
    public static string Version { get; } = AvaloniaApp.Metadata("EfCoreVersion");

    public static string Older { get; } = AvaloniaApp.Metadata("EfCoreOlderVersion");

    public static void Feed()
    {
        const string id = "microsoft.entityframeworkcore";
        string cache = AvaloniaApp.Metadata("NuGetPackageRoot");
        foreach (string version in new[] { Version, Older })
        {
            string name = $"{id}.{version}.nupkg";
            string package = Path.Combine(cache, id, version, name);
            Assert.True(File.Exists(package), $"{package} is missing: restore the tests first");
            string destination = Path.Combine(AvaloniaApp.Feed, name);
            if (!File.Exists(destination))
            {
                File.Copy(package, destination, overwrite: true);
            }
        }
    }
}
