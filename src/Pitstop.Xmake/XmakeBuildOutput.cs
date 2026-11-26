using System.Text.RegularExpressions;
using Passenger.Core;

namespace Pitstop.Xmake;

public enum XmakeDiagnosticSeverity
{
    Error,
    Warning,
    Note,
}

public sealed record XmakeDiagnostic(XmakeDiagnosticSeverity Severity, string Message, string? Path, int Line = 0, int Column = 0);

/// <summary>Reads diagnostic headers and adjacent Clang or LLD detail lines from Xmake's text output.</summary>
public sealed partial class XmakeBuildOutput(string root)
{
    private readonly List<XmakeDiagnostic> problems = [];
    private readonly HashSet<XmakeDiagnostic> seen = [];
    private XmakeDiagnostic? pending;

    public void Read(string line)
    {
        string text = line.Trim();
        if (pending is { } before && IsDetail(text) && before.Message.Length < 8192)
        {
            pending = before with { Message = $"{before.Message}\n{line.TrimEnd()}" };
            return;
        }
        Flush();
        // Xmake wraps compiler output in an outer error prefix on its first line.
        string unwrapped = text.StartsWith("error: ", StringComparison.Ordinal) ? text[7..] : text;
        Match header = Located().Match(unwrapped);
        if (header.Success && int.TryParse(header.Groups["line"].Value, out int row) && row > 0
            && int.TryParse(header.Groups["column"].Value, out int column) && column > 0
            && FilePath(header.Groups["path"].Value) is { } path)
        {
            pending = new(Severity(header.Groups["severity"].Value), header.Groups["message"].Value, path, row, column);
            return;
        }
        header = Unlocated().Match(unwrapped);
        if (!header.Success)
        {
            header = Unlocated().Match(text);
        }
        if (header.Success)
        {
            pending = new(Severity(header.Groups["severity"].Value), text, null);
        }
    }

    public IReadOnlyList<XmakeDiagnostic> Complete()
    {
        Flush();
        return problems;
    }

    private void Flush()
    {
        if (pending is { } problem && seen.Add(problem))
        {
            problems.Add(problem);
        }
        pending = null;
    }

    private string? FilePath(string path)
    {
        if (path.StartsWith('<') || path.Contains(':') && !Path.IsPathFullyQualified(path))
        {
            return null;
        }
        try
        {
            return PathNames.Canonical(Path.GetFullPath(path, root));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsDetail(string text) => text.StartsWith(">>>", StringComparison.Ordinal)
        || text.StartsWith('|') || Snippet().IsMatch(text)
        || text.StartsWith("> in ", StringComparison.Ordinal) || text.StartsWith("-> ", StringComparison.Ordinal);

    private static XmakeDiagnosticSeverity Severity(string severity) => severity switch
    {
        "warning" => XmakeDiagnosticSeverity.Warning,
        "note" => XmakeDiagnosticSeverity.Note,
        _ => XmakeDiagnosticSeverity.Error,
    };

    [GeneratedRegex(@"^(?<path>.+?):(?<line>[0-9]+):(?<column>[0-9]+): (?<severity>fatal error|error|warning|note): (?<message>.+)$")]
    private static partial Regex Located();

    [GeneratedRegex(@"^(?:[^:\s]+: )?(?<severity>fatal error|error|warning|note): ")]
    private static partial Regex Unlocated();

    [GeneratedRegex(@"^[0-9]+\s+\|")]
    private static partial Regex Snippet();
}
