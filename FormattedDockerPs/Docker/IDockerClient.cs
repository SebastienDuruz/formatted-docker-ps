using FormattedDockerPs.Models;

namespace FormattedDockerPs.Docker;

internal interface IDockerClient
{
    DockerState ReadState();
    string? StartContainer(string id);
    string? StopContainer(string id);
    string? DeleteContainer(string id, bool force = false);
    string? DeleteVolume(string name);
    string? DeleteNetwork(string id);
}
