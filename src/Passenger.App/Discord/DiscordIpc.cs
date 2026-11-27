using System.Globalization;

namespace Passenger.App.Discord;

/// <summary>Where Passenger looks for the Discord app on this computer and how often: the places Discord's own libraries
/// look, each with <c>-0</c> to <c>-9</c> after it (a Unix socket's path, or a named pipe's name on Windows), or the one
/// <c>PITSTOP_DISCORD_IPC</c> names in their stead.</summary>
public sealed record DiscordIpc(IReadOnlyList<string> Places)
{
    [IdentifierHome]
    public const string Variable = "PITSTOP_DISCORD_IPC";

    [IdentifierHome]
    public const string ApplicationIdVariable = "PITSTOP_DISCORD_APPLICATION_ID";

    /// <summary>How long Passenger waits before looking for Discord again.</summary>
    public TimeSpan Retry { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The least time between two updates, so a run of tab switches is one and they stay within the five every
    /// 20 seconds Discord takes; a quarter of it is the least a change waits, for the run to end.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>How long Discord has to answer the handshake, or to take the last update as Passenger closes.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    internal static bool Configured => ReadApplicationId() is not null || Environment.GetEnvironmentVariable(Variable) is { Length: > 0 };

    public string? ApplicationId { get; init; }

    /// <summary>The system's places, or the one <c>PITSTOP_DISCORD_IPC</c> names; a value there that is no absolute
    /// path (a pipe's name with no backslash, on Windows) is fatal.</summary>
    public static DiscordIpc Default => new(Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } named
        ? [Parse(named)] : SystemPlaces())
    {
        ApplicationId = ReadApplicationId(),
    };

    private static string? ReadApplicationId()
    {
        string? value = Environment.GetEnvironmentVariable(ApplicationIdVariable)?.Trim();
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id > 0 ? value : null;
    }

    /// <summary>The endpoints to try, in order: each number from 0, in each place.</summary>
    public IEnumerable<string> Endpoints() =>
        Enumerable.Range(0, 10).SelectMany(number => Places.Select(place => $"{place}-{number}"));

    private static string Parse(string text) => OperatingSystem.IsWindows()
        ? text.Contains('\\') || text.Contains('/') ? throw Malformed(text) : text
        : Path.IsPathRooted(text) ? text : throw Malformed(text);

    private static FormatException Malformed(string text) => new(OperatingSystem.IsWindows()
        ? $"{Variable} takes the name of Discord's pipe without its number (discord-ipc), not '{text}'"
        : $"{Variable} takes the absolute path of Discord's socket without its number (/tmp/discord-ipc), not '{text}'");

    /// <summary>Discord's own: its pipe on Windows; elsewhere its socket in the runtime or temporary folder the variables
    /// name, or /tmp, and on Linux inside the folders its snap and Flatpak packages are given there.</summary>
    private static string[] SystemPlaces()
    {
        if (OperatingSystem.IsWindows())
        {
            return ["discord-ipc"];
        }
        string[] folders = [.. new[] { Core.SocketFolders.RuntimeVariable, "TMPDIR", "TMP", "TEMP" }
            .Select(Environment.GetEnvironmentVariable)
            .OfType<string>()
            .Where(Path.IsPathRooted)
            .Append("/tmp")
            .Select(folder => folder.TrimEnd('/'))
            .Distinct(StringComparer.Ordinal)];
        string[] packaged = OperatingSystem.IsLinux()
            ? ["app/com.discordapp.Discord", "app/com.discordapp.DiscordCanary", "app/com.discordapp.DiscordPTB", "snap.discord", "snap.discord-canary"]
            : [];
        return [.. folders.SelectMany(folder => packaged.Prepend(string.Empty).Select(inside => Path.Combine(folder, inside, "discord-ipc")))];
    }
}
