using FormattedDockerPs.Docker;
using FormattedDockerPs.Models;

namespace FormattedDockerPs.Application;

// Call from the UI thread. Background results are posted through dispatchToUi;
// the returned tasks finish when that callback has been queued, not rendered.
internal sealed class MonitorController(IDockerClient docker, Action<Action> dispatchToUi) : IDisposable
{
    sealed record PendingCommand(IReadOnlyDictionary<string, string> Operations, Func<string?> Work, string ErrorTitle);

    bool disposed;
    bool refreshQueued;
    long refreshesStarted;
    readonly Dictionary<string, string> pendingOperations = [];
    // Finished commands keep their rows locked until a refresh started afterwards shows their outcome.
    readonly List<(string[] Keys, long LastStaleRefresh)> settling = [];

    public DockerState State { get; private set; } = new([], [], [], null, null, null);
    public DateTime LastRefresh { get; private set; } = DateTime.Now;
    public bool HasState { get; private set; }
    public bool RefreshInProgress { get; private set; }
    // Progress message per locked resource key (see ResourceKey).
    public IReadOnlyDictionary<string, string> PendingOperations => pendingOperations;

    public event Action? StateChanged;
    public event Action<string, string>? CommandFailed;

    public Task RefreshAsync() => RefreshAsync(queueIfBusy: false);

    async Task RefreshAsync(bool queueIfBusy)
    {
        if (disposed) return;
        if (RefreshInProgress)
        {
            // The running refresh may predate a command result, so run another one after it.
            refreshQueued |= queueIfBusy;
            return;
        }
        RefreshInProgress = true;
        var refresh = ++refreshesStarted;
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
                ReleaseSettledOperations(refresh);
                StateChanged?.Invoke();
            }
            finally
            {
                RefreshInProgress = false;
                if (refreshQueued)
                {
                    refreshQueued = false;
                    _ = RefreshAsync();
                }
            }
        });
    }

    void ReleaseSettledOperations(long refresh) =>
        settling.RemoveAll(entry =>
        {
            if (refresh <= entry.LastStaleRefresh) return false;
            foreach (var key in entry.Keys) pendingOperations.Remove(key);
            return true;
        });

    public Task ExecuteAsync(MonitorAction action)
    {
        if (disposed) return Task.CompletedTask;
        // Capture bulk targets before starting work, so refreshes cannot change them.
        var state = State;
        var command = action.Kind switch
        {
            MonitorActionKind.StartContainer => Single(ResourceKey.Container(action.ResourceId), "Starting",
                () => docker.StartContainer(action.ResourceId), "Docker action failed"),
            MonitorActionKind.StopContainer => Single(ResourceKey.Container(action.ResourceId), "Stopping",
                () => docker.StopContainer(action.ResourceId), "Docker action failed"),
            MonitorActionKind.DeleteContainer => Single(ResourceKey.Container(action.ResourceId), "Deleting",
                () => docker.DeleteContainer(action.ResourceId), "Docker delete failed"),
            MonitorActionKind.DeleteVolume => Single(ResourceKey.Volume(action.ResourceId), "Deleting",
                () => docker.DeleteVolume(action.ResourceId), "Docker delete failed"),
            MonitorActionKind.DeleteNetwork => Single(ResourceKey.Network(action.ResourceId), "Deleting",
                () => docker.DeleteNetwork(action.ResourceId), "Docker delete failed"),
            MonitorActionKind.ToggleAll => ToggleAll(state),
            MonitorActionKind.PurgeAll => PurgeAll(state),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        // Bulk actions wait for every operation; row actions only for their own row.
        var blocked = command.Operations.ContainsKey(ResourceKey.Global)
            ? pendingOperations.Count > 0
            : command.Operations.Keys.Any(pendingOperations.ContainsKey);
        return blocked ? Task.CompletedTask : RunCommandAsync(command);
    }

    static PendingCommand Single(string key, string message, Func<string?> work, string errorTitle) =>
        new(new Dictionary<string, string> { [key] = message }, work, errorTitle);

    PendingCommand ToggleAll(DockerState state)
    {
        var stop = state.Containers.Any(container => container.IsRunning);
        var targets = (stop ? state.Containers.Where(container => container.IsRunning) : state.Containers).ToList();
        var operations = targets.ToDictionary(container => ResourceKey.Container(container.Id), _ => stop ? "Stopping" : "Starting");
        operations[ResourceKey.Global] = stop ? "Stopping all" : "Starting all";
        return new(operations, () => CollectErrors(targets.Select(container =>
            stop ? docker.StopContainer(container.Id) : docker.StartContainer(container.Id))), "Docker bulk action failed");
    }

    PendingCommand PurgeAll(DockerState state)
    {
        var containers = state.Containers.ToList();
        var volumes = state.Volumes.ToList();
        var networks = state.Networks.Where(network => !network.IsBuiltIn).ToList();
        var operations = containers.Select(container => ResourceKey.Container(container.Id))
            .Concat(volumes.Select(volume => ResourceKey.Volume(volume.Name)))
            .Concat(networks.Select(network => ResourceKey.Network(network.Id)))
            .ToDictionary(key => key, _ => "Deleting");
        operations[ResourceKey.Global] = "Purging all";
        return new(operations, () => CollectErrors(
            containers.Select(container => docker.DeleteContainer(container.Id, force: true))
                .Concat(volumes.Select(volume => docker.DeleteVolume(volume.Name)))
                .Concat(networks.Select(network => docker.DeleteNetwork(network.Id)))), "Docker purge completed with errors");
    }

    static string? CollectErrors(IEnumerable<string?> results)
    {
        var errors = results.Where(error => error is not null).ToList();
        return errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
    }

    async Task RunCommandAsync(PendingCommand command)
    {
        foreach (var (key, message) in command.Operations) pendingOperations[key] = message;
        // Deferred: the clicked button must not be removed while its handler runs.
        dispatchToUi(() => { if (!disposed) StateChanged?.Invoke(); });
        string? error;
        try { error = await Task.Run(command.Work).ConfigureAwait(false); }
        catch (Exception ex) { error = ex.GetBaseException().Message; }
        dispatchToUi(() =>
        {
            if (disposed) return;
            settling.Add((command.Operations.Keys.ToArray(), refreshesStarted));
            _ = RefreshAsync(queueIfBusy: true);
            if (error is not null) CommandFailed?.Invoke(command.ErrorTitle, error);
        });
    }

    public void Dispose() => disposed = true;
}
