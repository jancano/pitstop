using System.Text;

namespace Pitstop.Templates;

internal static class TemplateRenderer
{
    internal static string Render(string template, IReadOnlyDictionary<string, string> values, bool filename)
    {
        StringBuilder result = new();
        Stack<(string Name, bool Visible)> scopes = new();
        bool visible = true;
        int at = 0;
        while (template.IndexOf("{{", at, StringComparison.Ordinal) is int start and >= 0)
        {
            if (visible)
            {
                result.Append(template.AsSpan(at, start - at));
            }
            int end = template.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new IOException("A template token is missing its closing braces.");
            }
            string token = template[(start + 2)..end];
            if (!filename && token.StartsWith('#') && TemplatePaths.Identifier(token[1..]))
            {
                scopes.Push((token[1..], visible));
                visible &= values.GetValueOrDefault(token[1..]) == "true";
            }
            else if (!filename && token.StartsWith('/') && scopes.TryPop(out (string Name, bool Visible) scope) && scope.Name == token[1..])
            {
                visible = scope.Visible;
            }
            else if (visible || token.StartsWith('/'))
            {
                result.Append(Value(token, values, filename));
            }
            at = end + 2;
        }
        if (scopes.Count != 0)
        {
            throw new IOException($"Template condition '{scopes.Peek().Name}' is missing its closing token.");
        }
        return result.Append(template.AsSpan(at)).ToString();
    }

    private static string Value(string token, IReadOnlyDictionary<string, string> values, bool filename)
    {
        int separator = token.IndexOf(':', StringComparison.Ordinal);
        string name = separator < 0 ? token : token[(separator + 1)..];
        if (!values.TryGetValue(name, out string? value))
        {
            throw new IOException($"Template token '{token}' has no active parameter.");
        }
        string format = separator < 0 ? "" : token[..separator];
        return (filename, format) switch
        {
            (true, "") when TemplatePaths.FileName(value) => value,
            (false, "") when value.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '_' or '-' or '.') => value,
            (false, "lua" or "c") => Quote(value),
            (_, "id") => "project_" + new string([.. value.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]),
            _ => throw new IOException($"Template token '{token}' needs safe text or an escaping mode.")
        };
    }

    private static string Quote(string value)
    {
        if (value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
        {
            throw new IOException("A generated string literal contains an unsupported control character.");
        }
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal) + "\"";
    }
}
