using Pitstop.Core;

namespace Pitstop.App.Web;

/// <summary>Whether and where the Dockerfile server asks for an image's tags as it completes <c>FROM image:</c>: Docker
/// Hub only while Settings' Complete image tags from Docker Hub is on. The server runs with Passenger's preload
/// (<c>web/pitstop/docker-hub.cjs</c>), told through <c>PITSTOP_DOCKER_HUB</c> to ask nothing, or to ask another
/// place: a window's stand-in, else the one the IDE's own <c>PITSTOP_DOCKER_HUB</c> names.</summary>
public static class DockerHubTags
{
    [IdentifierHome]
    public const string Variable = "PITSTOP_DOCKER_HUB";

    /// <summary>The preload, carried beside the web language servers.</summary>
    public static string Preload => Path.Combine(WebLanguages.Carried, "pitstop", "docker-hub.cjs");

    /// <summary>What the server's <c>PITSTOP_DOCKER_HUB</c> says: off while the setting is (<paramref name="asks"/>
    /// false), else the place standing in for Docker Hub, else nothing (Docker Hub itself).</summary>
    public static string? Told(bool asks, Uri? standIn) => !asks ? "off" : (standIn ?? Named)?.AbsoluteUri;

    /// <summary>The place the IDE's <c>PITSTOP_DOCKER_HUB</c> names; null when it names none
    /// (<see cref="EnvironmentValues.Address(string, string)"/>).</summary>
    public static Uri? Named => EnvironmentValues.Address(Variable, "Docker Hub's requests for image tags go to instead");
}
