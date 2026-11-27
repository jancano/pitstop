using System.Text;
using Passenger.Core;

namespace Pitstop.Xmake;

public static class XmakeProjectEdits
{
    public static XmakeScriptEdit AddTarget(XmakeWorkspace workspace, string name, string kind)
    {
        if (name.Length == 0 || !char.IsAsciiLetterOrDigit(name[0]) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))
            || workspace.Targets.Any(target => string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Choose an unused target name made of ASCII letters, digits, underscores or hyphens, beginning with a letter or digit.");
        }
        if (kind is not ("binary" or "static" or "shared" or "headeronly"))
        {
            throw new ArgumentException("Choose a supported Xmake target kind.");
        }
        return Plan(workspace, Path.Combine(workspace.Root, XmakeService.ProjectFile), TargetBlock(name, "set_kind", kind));
    }

    public static XmakeScriptEdit AddFile(XmakeWorkspace workspace, XmakeTarget target, string source, bool header)
    {
        EnsureWithin(workspace.Root, source);
        string relative = Path.GetRelativePath(Path.GetDirectoryName(target.Script)!, source).Replace('\\', '/');
        if (relative.Any(c => c is '*' or '?' or '[' or ']' or '|' or '\r' or '\n'))
        {
            throw new ArgumentException("This file name uses characters interpreted by Xmake's file patterns. Edit the target's script to include it.");
        }
        return Plan(workspace, target.Script, TargetBlock(target.Name, header ? "add_headerfiles" : "add_files", relative));
    }

    public static XmakeScriptEdit AddDependency(XmakeWorkspace workspace, XmakeTarget target, XmakeTarget dependency)
    {
        HashSet<string> visited = new(StringComparer.Ordinal);
        Stack<string> pending = new([dependency.Name]);
        while (pending.TryPop(out string? candidate))
        {
            if (candidate == target.Name)
            {
                throw new ArgumentException("Choose a different dependency. This target would depend on itself.");
            }
            if (visited.Add(candidate))
            {
                foreach (string next in workspace.Graph.DependenciesOf(candidate))
                {
                    pending.Push(next);
                }
            }
        }
        if (workspace.Graph.DependenciesOf(target.Name).Contains(dependency.Name))
        {
            throw new ArgumentException($"{target.Name} already depends on {dependency.Name}.");
        }
        return Plan(workspace, target.Script, TargetBlock(target.Name, "add_deps", dependency.Name));
    }

    public static string NewSourcePath(XmakeWorkspace workspace, string relative, string extension)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
        {
            throw new ArgumentException("Enter a source path relative to the workspace.");
        }
        if (Path.GetExtension(relative).Length == 0)
        {
            relative += extension;
        }
        string chosenExtension = Path.GetExtension(relative).ToLowerInvariant();
        if (chosenExtension != extension && !(extension == ".hpp" && chosenExtension is ".h" or ".hh" or ".hxx")
            && !(extension == ".cpp" && chosenExtension is ".cc" or ".cxx"))
        {
            throw new ArgumentException($"Use the {extension} file extension for this action.");
        }
        string full = Path.GetFullPath(relative, workspace.Root);
        EnsureWithin(workspace.Root, full);
        if (File.Exists(full) || Directory.Exists(full) || !Directory.Exists(Path.GetDirectoryName(full)))
        {
            throw new ArgumentException("Choose a file that does not exist inside an existing project directory.");
        }
        return full;
    }

    private static XmakeScriptEdit Plan(XmakeWorkspace workspace, string script, string append)
    {
        WorkspaceTrust.Check(workspace.Root);
        EnsureWithin(workspace.Root, script);
        if (!workspace.ProjectFiles.Contains(script, PathNames.Comparer))
        {
            throw new ArgumentException("Open the evaluated target's script and edit it directly. Xmake did not list it as a project input.");
        }
        byte[] original = File.ReadAllBytes(script);
        // Preserve the exact bytes of the existing UTF-8 script, including its BOM and line endings.
        string source = new UTF8Encoding(false, true).GetString(original);
        string ending = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new(workspace.Root, script, original, ("\n\n" + append).ReplaceLineEndings(ending));
    }

    private static string TargetBlock(string target, string command, string value) =>
        $"target({LuaString(target)})\n    {command}({LuaString(value)})\ntarget_end()\n";

    private static string LuaString(string value)
    {
        string delimiter = "=";
        while (value.Contains($"]{delimiter}]", StringComparison.Ordinal))
        {
            delimiter += "=";
        }
        return $"[{delimiter}[{value}]{delimiter}]";
    }

    internal static void EnsureWithin(string root, string path)
    {
        if (!PathNames.IsUnder(path, root) || !PathNames.Same(PathNames.Canonical(path), path))
        {
            throw new ArgumentException("Choose a project path inside the workspace that does not resolve through a link.");
        }
    }
}

public sealed class XmakeScriptEdit
{
    private readonly string root;
    private readonly byte[] original;

    internal XmakeScriptEdit(string root, string path, byte[] original, string addition)
    {
        this.root = root;
        this.original = original;
        Path = path;
        Addition = addition;
    }

    public string Path { get; }
    public string Addition { get; }

    public void Apply(string addition)
    {
        WorkspaceTrust.Check(root);
        XmakeProjectEdits.EnsureWithin(root, Path);
        using FileStream file = new(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using MemoryStream current = new();
        file.CopyTo(current);
        if (!current.GetBuffer().AsSpan(0, (int)current.Length).SequenceEqual(original))
        {
            throw new IOException("The Xmake script changed since the preview. Reopen the operation to review the current script.");
        }
        byte[] bytes = Encoding.UTF8.GetBytes(addition);
        try
        {
            file.Write(bytes);
        }
        catch (IOException)
        {
            file.SetLength(original.Length);
            throw;
        }
    }
}
