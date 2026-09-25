using System.Diagnostics;

namespace FormattedDockerPs.Docker;

internal sealed class DockerProcessRunner : IDockerProcessRunner
{
    public DockerProcessResult Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try { process.Start(); }
        catch (Exception ex) { return new("", "", -1, $"Unable to start docker: {ex.Message}"); }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new(stdout, stderr, process.ExitCode);
    }
}
