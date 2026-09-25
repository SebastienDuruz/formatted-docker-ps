namespace FormattedDockerPs.Docker;

internal sealed record DockerProcessResult(string Output, string Error, int ExitCode, string? StartError = null);

internal interface IDockerProcessRunner
{
    DockerProcessResult Run(params string[] arguments);
}
