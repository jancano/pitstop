using System.ComponentModel;
using ModelContextProtocol.Server;
using Passenger.Core;
using Pitstop.Modules;

namespace Passenger.App.Mcp;

public sealed partial class PassengerTools
{
    [McpServerTool(Name = McpTools.ModuleWorkspace, Title = "C++ module dependencies", ReadOnly = true), Description(
        "Read the current trusted Xmake module scan. Supply a module name and optional target to inspect providers, direct dependencies, consumers and potential transitive impact.")]
    public Task<object> DescribeModules(string? name = null, string? target = null, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        cancel.ThrowIfCancellationRequested();
        object answer;
        if (!Session.Trusted || Session.Folder is not { } folder || !WorkspaceTrust.Trusts(folder))
        {
            answer = new { status = "Open a trusted Xmake workspace." };
        }
        else if (Session.Modules?.Snapshot is not { } graph)
        {
            answer = new { status = Session.Modules?.Status ?? "No module workspace is open." };
        }
        else
        {
            IEnumerable<ModuleProvider> providers = name is null ? graph.Providers : graph.Named(name);
            ModuleProvider[] selected = [.. providers.Where(provider => target is null || provider.Unit.Context.Target == target).Take(500)];
            answer = new
            {
                status = Session.Modules.Status,
                complete = graph.IsComplete,
                configuration = graph.Workspace.Configuration.Mode,
                platform = graph.Workspace.Configuration.Platform,
                architecture = graph.Workspace.Configuration.Architecture,
                modules = selected.Select(provider => new
                {
                    provider.Name, provider.Kind, provider.Origin, target = provider.Unit.Context.Target, provider.Unit.Context.TargetEvidence, source = PathOf(provider.Unit.Source),
                    dependencies = name is null ? null : graph.Imports(provider.Unit).Select(edge => new { edge.Name, edge.Resolution, edge.Evidence,
                        candidates = edge.Candidates.Select(candidate => new { target = candidate.Unit.Context.Target, source = PathOf(candidate.Unit.Source) }).ToArray() }).ToArray(),
                    consumers = name is null ? null : graph.Consumers(provider.Unit).Select(edge => PathOf(edge.Consumer.Source)).Distinct().ToArray(),
                    potentialImpact = name is null ? null : graph.Reachable(provider.Unit, consumers: true).Select(unit => PathOf(unit.Source)).Distinct().Take(500).ToArray(),
                }).ToArray(),
                unresolved = graph.Dependencies.Where(edge => edge.Provider is null).Take(500)
                    .Select(edge => new { edge.Name, edge.Resolution, source = PathOf(edge.Consumer.Source), target = edge.Consumer.Context.Target }).ToArray(),
                missing = graph.Missing.Take(500).ToArray(),
            };

            string PathOf(string path) => PathNames.Within(path, folder) ? Path.GetRelativePath(folder, path) : path;
        }
        return Task.FromResult(answer);
    });
}
