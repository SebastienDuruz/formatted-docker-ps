namespace FormattedDockerPs.Models;

internal sealed record DockerState(
    List<ContainerRow> Containers,
    List<VolumeRow> Volumes,
    List<NetworkRow> Networks,
    string? ContainerError,
    string? VolumeError,
    string? NetworkError)
{
    public static DockerState WithError(string error) => new([], [], [], error, error, error);
}

internal sealed record ContainerRow(string Id, string Image, string Status, string State, string Ports, string Name)
{
    public bool IsRunning => State == "running";
}
internal sealed record VolumeRow(string Name, string Driver, string Scope);
internal sealed record NetworkRow(string Id, string Name, string Driver, string Scope)
{
    public bool IsBuiltIn => Name is "bridge" or "host" or "none";
}
