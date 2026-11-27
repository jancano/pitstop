namespace Pitstop.Templates;

internal static class TemplatePaths
{
    internal static bool Identifier(string? value) => value is { Length: > 0 } && (char.IsAsciiLetter(value[0]) || value[0] == '_')
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    internal static bool FileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.EndsWith('.') || value.EndsWith(' ')
            || value.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
        {
            return false;
        }
        string stem = value.Split('.', 2)[0].TrimEnd().ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789¹²³".Contains(stem[3]));
    }

    internal static string RelativeTo(string root, string? relative)
    {
        string[] parts = relative?.Split(['/', '\\']) ?? [];
        if (parts.Length == 0 || parts.Any(part => !FileName(part)))
        {
            throw new IOException($"'{relative}' is not a portable relative template path.");
        }
        return Path.Combine(root, Path.Combine(parts));
    }

    internal static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal static void CheckAncestors(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
        {
            if (Attributes(part) is { } found && (found & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Template paths must not pass through a symbolic link or junction: '{part}'.");
            }
        }
    }
}
