using FormattedDockerPs.Docker;
using FormattedDockerPs.Models;

namespace FormattedDockerPs.Application;

// Call from the UI thread. Background results are posted through dispatchToUi;
// the returned tasks finish when that callback has been queued, not rendered.
internal sealed class MonitorController(IDockerClient docker, Action<Action> dispatchToUi) : IDisposable
{
    bool disposed;
    public DockerState State { get; private set; } = new([], [], [], null, null, null);
    public DateTime LastRefresh { get; private set; } = DateTime.Now;
    public bool HasState { get; private set; }
    public bool RefreshInProgress { get; private set; }
    public bool CommandInProgress { get; private set; }

    public event Action? StateChanged;
    public event Action<string, string>? CommandFailed;

    public async Task RefreshAsync()
    {
        if (disposed || RefreshInProgress) return;
        RefreshInProgress = true;
        DockerState state;
        try { state = await Task.Run(docker.ReadState).ConfigureAwait(false); }
        catch (Exception ex) { state = DockerState.WithError(ex.GetBaseException().Message); }
        dispatchToUi(() =>
        {
            if (disposed) return;
            try
            {
                State = state;
                HasState = true;
                LastRefresh = DateTime.Now;
                StateChanged?.Invoke();
            }
            finally { RefreshInProgress = false; }
        });
    }

    public Task ExecuteAsync(MonitorAction action)
    {
        if (disposed || CommandInProgress) return Task.CompletedTask;
        // Capture bulk targets before starting work, so refreshes cannot change them.
        var state = State;
        return action.Kind switch
        {
            MonitorActionKind.StartContainer => RunCommandAsync(() => docker.StartContainer(action.ResourceId), "Docker action failed"),
            MonitorActionKind.StopContainer => RunCommandAsync(() => docker.StopContainer(action.ResourceId), "Docker action failed"),
            MonitorActionKind.DeleteContainer => RunCommandAsync(() => docker.DeleteContainer(action.ResourceId), "Docker delete failed"),
            MonitorActionKind.DeleteVolume => RunCommandAsync(() => docker.DeleteVolume(action.ResourceId), "Docker delete failed"),
            MonitorActionKind.DeleteNetwork => RunCommandAsync(() => docker.DeleteNetwork(action.ResourceId), "Docker delete failed"),
            MonitorActionKind.ToggleAll => ToggleAllAsync(state),
            MonitorActionKind.PurgeAll => PurgeAllAsync(state),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    Task ToggleAllAsync(DockerState state)
    {
        var stop = state.Containers.Any(container => container.IsRunning);
        var targets = (stop ? state.Containers.Where(container => container.IsRunning) : state.Containers).ToList();
        return RunCommandAsync(() => CollectErrors(targets.Select(container =>
            stop ? docker.StopContainer(container.Id) : docker.StartContainer(container.Id))), "Docker bulk action failed");
    }

    Task PurgeAllAsync(DockerState state)
    {
        var containers = state.Containers.ToList();
        var volumes = state.Volumes.ToList();
        var networks = state.Networks.Where(network => !network.IsBuiltIn).ToList();
        return RunCommandAsync(() => CollectErrors(
            containers.Select(container => docker.DeleteContainer(container.Id, force: true))
                .Concat(volumes.Select(volume => docker.DeleteVolume(volume.Name)))
                .Concat(networks.Select(network => docker.DeleteNetwork(network.Id)))), "Docker purge completed with errors");
    }

    static string? CollectErrors(IEnumerable<string?> results)
    {
        var errors = results.Where(error => error is not null).ToList();
        return errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
    }

    async Task RunCommandAsync(Func<string?> work, string errorTitle)
    {
        CommandInProgress = true;
        string? error;
        try { error = await Task.Run(work).ConfigureAwait(false); }
        catch (Exception ex) { error = ex.GetBaseException().Message; }
        dispatchToUi(() =>
        {
            if (disposed) return;
            CommandInProgress = false;
            if (error is not null) CommandFailed?.Invoke(errorTitle, error);
            _ = RefreshAsync();
        });
    }

    public void Dispose() => disposed = true;
}
