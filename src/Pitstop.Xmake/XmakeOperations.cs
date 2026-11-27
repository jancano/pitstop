using System.Diagnostics;
using Pitstop.Core;

namespace Pitstop.Xmake;

public enum XmakeBuildKind
{
    Build,
    Rebuild,
    Clean,
}

/// <summary>Commands for the evaluated configuration. Xmake owns the build graph and the target's runtime settings.</summary>
public static class XmakeOperations
{
    public static ProcessStartInfo Build(XmakeWorkspace workspace, XmakeBuildKind kind, IReadOnlyList<string> targets)
    {
        foreach (string target in targets)
        {
            FindTarget(workspace, target);
        }
        string[] options = kind switch
        {
            XmakeBuildKind.Build when targets.Count == 0 => ["--all"],
            XmakeBuildKind.Rebuild => targets.Count == 0 ? ["--rebuild", "--all"] : ["--rebuild"],
            _ => [],
        };
        return XmakeService.ProjectCommand(workspace.Installation.Executable, workspace.Root,
            [kind == XmakeBuildKind.Clean ? "clean" : "build", "--confirm=no", .. options, .. targets]);
    }

    public static ProcessStartInfo Run(XmakeWorkspace workspace, string target, IReadOnlyList<string> arguments, string directory)
    {
        if (FindTarget(workspace, target).Kind != "binary")
        {
            throw new XmakeException($"Xmake target {target} is not an executable. Choose a binary target to run.");
        }
        string[] workdir = [];
        if (directory.Length > 0)
        {
            string absolute = Path.GetFullPath(directory, workspace.Root);
            if (!Directory.Exists(absolute))
            {
                throw new XmakeException($"The working directory {absolute} does not exist. Edit the run configuration or create the directory.");
            }
            workdir = [$"--workdir={absolute}"];
        }
        return XmakeService.ProjectCommand(workspace.Installation.Executable, workspace.Root,
            ["run", "--confirm=no", .. workdir, target, .. arguments]);
    }

    private static XmakeTarget FindTarget(XmakeWorkspace workspace, string name) =>
        !name.StartsWith('-') && workspace.Targets.FirstOrDefault(target => target.Name == name) is { } target ? target
            : throw new XmakeException($"Xmake target {name} is unavailable in {workspace.Configuration.Mode}. Choose a target from the evaluated project.");
}
