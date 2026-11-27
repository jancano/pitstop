using System.Reflection;
using System.Text.Json;
using Pitstop.Core;
using Pitstop.Dap;

namespace Pitstop.NativeDebug;

/// <summary>Allowlisted envelope metadata. Request bodies, paths, output and free-form error strings are never recorded.</summary>
public sealed record NativeDebugPacket(string Direction, string Kind, string? Name, int? Sequence, int? RequestSequence, bool? Success)
{
    private static readonly HashSet<string> Names = new(
        new[] { typeof(DapCommands), typeof(DapEvents) }.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!), StringComparer.Ordinal);

    internal static NativeDebugPacket From(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line.AsMemory(4));
        JsonElement envelope = document.RootElement;
        string? name = JsonMembers.Text(envelope, "command") ?? JsonMembers.Text(envelope, "event");
        string kind = JsonMembers.Text(envelope, "type") switch
        {
            "request" => "request",
            "response" => "response",
            "event" => "event",
            _ => "unknown",
        };
        return new(line[..3], kind, name is null || Names.Contains(name) ? name : "(other)",
            JsonMembers.Number(envelope, "seq"), JsonMembers.Number(envelope, "request_seq"), JsonMembers.Bool(envelope, "success"));
    }
}
