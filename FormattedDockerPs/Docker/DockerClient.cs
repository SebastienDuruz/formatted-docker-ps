using FormattedDockerPs.Models;

namespace FormattedDockerPs.Docker;

internal sealed class DockerClient(IDockerProcessRunner runner) : IDockerClient
{
    public DockerState ReadState()
    {
        var (containers, containerError) = ReadRows(
            ["ps", "--all", "--no-trunc", "--format", "{{.ID}}\t{{.Image}}\t{{.Status}}\t{{.State}}\t{{.Ports}}\t{{.Names}}"],
            6, cols => new ContainerRow(cols[0], cols[1], cols[2], cols[3], cols[4], cols[5]), "docker ps failed");
        var (volumes, volumeError) = ReadRows(
            ["volume", "ls", "--format", "{{.Name}}\t{{.Driver}}\t{{.Scope}}"],
            3, cols => new VolumeRow(cols[0], cols[1], cols[2]), "docker list failed");
        var (networks, networkError) = ReadRows(
            ["network", "ls", "--no-trunc", "--format", "{{.ID}}\t{{.Name}}\t{{.Driver}}\t{{.Scope}}"],
            4, cols => new NetworkRow(cols[0], cols[1], cols[2], cols[3]), "docker list failed");
        return new(containers, volumes, networks, containerError, volumeError, networkError);
    }

    public string? StartContainer(string id) => Execute("start", id);
    public string? StopContainer(string id) => Execute("stop", id);
    public string? DeleteContainer(string id, bool force = false) =>
        force ? Execute("rm", "--force", id) : Execute("rm", id);
    public string? DeleteVolume(string name) => Execute("volume", "rm", name);
    public string? DeleteNetwork(string id) => Execute("network", "rm", id);

    (List<T> Rows, string? Error) ReadRows<T>(string[] arguments, int fieldCount, Func<string[], T> parse, string fallback)
    {
        var result = runner.Run(arguments);
        if (result.StartError is not null) return ([], result.StartError);
        if (result.ExitCode != 0)
            return ([], string.IsNullOrWhiteSpace(result.Error) ? fallback : result.Error.Trim());

        return (result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(columns => columns.Length >= fieldCount)
            .Select(parse).ToList(), null);
    }

    string? Execute(params string[] arguments)
    {
        var result = runner.Run(arguments);
        if (result.StartError is not null) return result.StartError;
        if (result.ExitCode == 0) return null;
        return string.IsNullOrWhiteSpace(result.Error)
            ? string.IsNullOrWhiteSpace(result.Output) ? $"docker {string.Join(' ', arguments.Take(2))} failed" : result.Output.Trim()
            : result.Error.Trim();
    }
}
