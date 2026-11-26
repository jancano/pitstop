using System.ComponentModel;
using ModelContextProtocol.Server;
using Passenger.Core;
using Pitstop.Modules;

namespace Passenger.App.Mcp;

public sealed partial class PassengerTools
{
    [McpServerTool(Name = McpTools.ModuleWorkspace, Title = "C++ module dependencies", ReadOnly = true), Description(
        "Read the current trusted Xmake module scan. Supply a module name and optional target to inspect providers, direct dependencies, consumers and potential transitive impact. Limit each result list to 1–500 entries (default 100).")]
    public Task<object> DescribeModules(string? name = null, string? target = null, int limit = 100, CancellationToken cancel = default) => OnUi<object>(cancel, async () =>
    {
        cancel.ThrowIfCancellationRequested();
        if (!Session.Trusted || Session.Folder is not { } folder || !WorkspaceTrust.Trusts(folder))
        {
            return new { status = "Open a trusted Xmake workspace." };
        }
        if (Session.Modules?.Snapshot is not { } graph)
        {
            return new { status = Session.Modules?.Status ?? "No module workspace is open." };
        }
        string status = Session.Modules.Status;
        int count = Math.Clamp(limit, 1, 500);
        object answer = await BackgroundWork.Run(() => Describe(graph, status, folder, name, target, count, cancel), cancel);
        cancel.ThrowIfCancellationRequested();
        if (!ReferenceEquals(Session.Modules?.Snapshot, graph) || !Session.Trusted || !WorkspaceTrust.Trusts(folder))
        {
            return new { status = "The module workspace changed. Query the current workspace again." };
        }
        return answer;
    }, projects: false);

    private static object Describe(ModuleWorkspace graph, string status, string folder, string? name, string? target, int limit, CancellationToken cancel)
    {
        bool truncated = false;
        IEnumerable<ModuleProvider> providers = name is null ? graph.Providers : graph.Named(name);
        var modules = Bounded(providers.Where(provider => target is null || provider.Unit.Context.Target == target)).Select(provider => new
        {
            provider.Name, provider.Kind, provider.Origin, target = provider.Unit.Context.Target, provider.Unit.Context.TargetEvidence, source = PathOf(provider.Unit.Source),
            dependencies = name is null ? null : Bounded(graph.Imports(provider.Unit)).Select(edge => new
            {
                edge.Name, edge.Resolution, edge.Evidence,
                candidates = Bounded(edge.Candidates).Select(candidate => new { target = candidate.Unit.Context.Target, source = PathOf(candidate.Unit.Source) }).ToArray(),
            }).ToArray(),
            consumers = name is null ? null : Bounded(graph.Consumers(provider.Unit).Select(edge => PathOf(edge.Consumer.Source)).Distinct()),
            potentialImpact = name is null ? null : Bounded(graph.Reachable(provider.Unit, consumers: true).Select(unit => PathOf(unit.Source)).Distinct()),
        }).ToArray();
        var unresolved = Bounded(graph.Dependencies.Where(edge => edge.Provider is null && (target is null || edge.Consumer.Context.Target == target)))
            .Select(edge => new { edge.Name, edge.Resolution, source = PathOf(edge.Consumer.Source), target = edge.Consumer.Context.Target }).ToArray();
        string[] missing = Bounded(graph.Missing);
        return new
        {
            status, complete = graph.IsComplete, limit, truncated, configuration = graph.Workspace.Configuration.Mode,
            platform = graph.Workspace.Configuration.Platform, architecture = graph.Workspace.Configuration.Architecture,
            modules, unresolved, missing,
        };

        T[] Bounded<T>(IEnumerable<T> input)
        {
            List<T> items = [];
            foreach (T value in input)
            {
                cancel.ThrowIfCancellationRequested();
                if (items.Count == limit)
                {
                    truncated = true;
                    break;
                }
                items.Add(value);
            }
            return [.. items];
        }

        string PathOf(string path) => PathNames.Within(path, folder) ? Path.GetRelativePath(folder, path) : path;
    }
}
