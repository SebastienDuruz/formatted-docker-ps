using System.Diagnostics;
using System.Text;
using Terminal.Gui;

const int RefreshIntervalMs = 1000;

// App setup
Application.Init();
var matrix = CreateMatrixScheme();
var buttonScheme = CreateButtonScheme();

var top = Application.Top;
top.ColorScheme = matrix;

var window = new Window("Docker PS Monitor")
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ColorScheme = matrix,
};

var actionView = new ActionViewport
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ColorScheme = matrix,
    ButtonScheme = buttonScheme,
};
window.Add(actionView);
top.Add(window);

var rows = new List<ContainerRow>();
var volumes = new List<VolumeRow>();
var networks = new List<NetworkRow>();
string? lastError = null;
string? volumeError = null;
string? networkError = null;
var lastRefresh = DateTime.Now;
var refreshInProgress = false;
var dockerCommandInProgress = false;
var hasDockerState = false;

void RefreshUi()
{
    if (refreshInProgress)
    {
        return;
    }

    refreshInProgress = true;
    _ = Task.Run(ReadDockerState).ContinueWith(task =>
    {
        Application.MainLoop?.Invoke(() =>
        {
            try
            {
                var state = task.IsCompletedSuccessfully
                    ? task.Result
                    : DockerState.WithError(task.Exception?.GetBaseException().Message ?? "Docker refresh failed");
                ApplyDockerState(state);
            }
            finally
            {
                refreshInProgress = false;
            }
        });
    }, TaskScheduler.Default);
}

void ApplyDockerState(DockerState state)
{
    hasDockerState = true;
    rows = state.Containers;
    volumes = state.Volumes;
    networks = state.Networks;
    lastError = state.ContainerError;
    volumeError = state.VolumeError;
    networkError = state.NetworkError;
    lastRefresh = DateTime.Now;

    RenderUi();
}

void RenderUi()
{
    if (Application.Current != top || !hasDockerState) return;

    var width = actionView.Bounds.Width;
    if (width < 44 || actionView.Bounds.Height < 1)
    {
        actionView.ShowSizeHint();
        return;
    }

    var layout = GetTableLayout(rows, volumes, networks, lastError, volumeError, networkError);
    var containerColumns = PickColumns(width);
    var containerWidths = ComputeWidths(containerColumns.Select(column => column.MinWidth).ToArray(), width);
    var resourceWidths = ComputeWidths([12, 10, 8, 10], width);
    var actions = new List<ViewportAction>();
    var actionX = ActionColumnX(containerWidths);

    if (string.IsNullOrWhiteSpace(lastError))
    {
        foreach (var (container, index) in rows.Select((row, index) => (row, index)))
        {
            var y = layout.Containers.FirstDataRowY + index;
            var label = container.IsRunning ? "Stop" : "Start";
            actions.Add(new($"container:{container.Id}:toggle", actionX, y, label,
                () => ExecuteContainerAction(container, label.ToLowerInvariant())));
            // Reserve the width of Start so changing state never shifts Delete.
            actions.Add(new($"container:{container.Id}:delete", actionX + 10, y, "Delete",
                () => DeleteContainer(container)));
        }
    }

    var resourceX = ActionColumnX(resourceWidths);
    if (string.IsNullOrWhiteSpace(volumeError))
        foreach (var (volume, index) in volumes.Select((row, index) => (row, index)))
            actions.Add(new($"volume:{volume.Name}:delete", resourceX, layout.Volumes.FirstDataRowY + index,
                "Delete", () => DeleteVolume(volume)));

    if (string.IsNullOrWhiteSpace(networkError))
        foreach (var (network, index) in networks.Select((row, index) => (row, index)))
            actions.Add(new($"network:{network.Id}:delete", resourceX, layout.Network.FirstDataRowY + index,
                "Delete", () => DeleteNetwork(network)));

    actions.Add(new("global:toggle", 16, layout.GlobalActionsY,
        rows.Any(container => container.IsRunning) ? "Stop all" : "Start all", ToggleAllContainers));
    actions.Add(new("global:purge", 30, layout.GlobalActionsY, "Purge all", PurgeAllDockerResources));

    actionView.SetContent(RenderTable(rows, volumes, networks, lastError, volumeError, networkError,
        containerColumns, containerWidths, resourceWidths, lastRefresh), layout.ContentHeight, actions);
}

void ExecuteContainerAction(ContainerRow container, string action) =>
    RunDockerWorkInBackground(() => RunDockerCommand(action, container.Id), "Docker action failed");

void DeleteContainer(ContainerRow container)
{
    var confirmation = MessageBox.Query(
        "Delete container",
        $"Delete '{container.Name}'? A running container must be stopped first.",
        "Delete",
        "Cancel");

    if (confirmation == 0)
    {
        RunDockerWorkInBackground(() => RunDockerCommand("rm", container.Id), "Docker delete failed");
    }
}

void ToggleAllContainers()
{
    var hasRunningContainer = rows.Any(container => container.IsRunning);
    var targets = (hasRunningContainer ? rows.Where(container => container.IsRunning) : rows).ToList();
    var action = hasRunningContainer ? "stop" : "start";
    RunDockerWorkInBackground(
        () => RunDockerCommands(targets.Select(container => new[] { action, container.Id })),
        "Docker bulk action failed");
}

void PurgeAllDockerResources()
{
    var confirmation = MessageBox.Query(
        "Purge Docker resources",
        "Delete all containers, volumes, and user-defined networks? Containers are removed forcibly. Docker built-in networks are preserved.",
        "Purge",
        "Cancel");

    if (confirmation == 0)
    {
        var containersToPurge = rows.ToList();
        var volumesToPurge = volumes.ToList();
        var networksToPurge = networks.Where(network => !network.IsBuiltIn).ToList();
        RunDockerWorkInBackground(
            () => PurgeDockerResources(containersToPurge, volumesToPurge, networksToPurge),
            "Docker purge completed with errors");
    }
}

void DeleteVolume(VolumeRow volume)
{
    if (MessageBox.Query("Delete volume", $"Delete '{volume.Name}'?", "Delete", "Cancel") == 0)
    {
        RunDockerWorkInBackground(() => RunDockerCommand("volume", "rm", volume.Name), "Docker delete failed");
    }
}

void DeleteNetwork(NetworkRow network)
{
    if (MessageBox.Query("Delete network", $"Delete '{network.Name}'?", "Delete", "Cancel") == 0)
    {
        RunDockerWorkInBackground(() => RunDockerCommand("network", "rm", network.Id), "Docker delete failed");
    }
}

void RunDockerWorkInBackground(Func<string?> work, string errorTitle)
{
    if (dockerCommandInProgress)
    {
        return;
    }

    dockerCommandInProgress = true;
    _ = Task.Run(work).ContinueWith(task =>
    {
        Application.MainLoop?.Invoke(() =>
        {
            dockerCommandInProgress = false;
            var error = task.IsCompletedSuccessfully
                ? task.Result
                : task.Exception?.GetBaseException().Message ?? "Docker command failed";
            if (error is not null)
            {
                MessageBox.ErrorQuery(errorTitle, error, "OK");
            }

            RefreshUi();
        });
    }, TaskScheduler.Default);
}

static string? RunDockerCommands(IEnumerable<string[]> commands)
{
    var errors = commands
        .Select(command => RunDockerCommand(command))
        .Where(error => error is not null)
        .Cast<string>()
        .ToList();
    return errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
}

static string? PurgeDockerResources(
    IReadOnlyList<ContainerRow> containers,
    IReadOnlyList<VolumeRow> volumes,
    IReadOnlyList<NetworkRow> networks)
{
    var commands = containers.Select(container => new[] { "rm", "--force", container.Id })
        .Concat(volumes.Select(volume => new[] { "volume", "rm", volume.Name }))
        .Concat(networks.Select(network => new[] { "network", "rm", network.Id }));
    return RunDockerCommands(commands);
}

// Refresh every second on the UI loop.
using var timer = new System.Threading.Timer(_ =>
{
    Application.MainLoop?.Invoke(RefreshUi);
}, null, dueTime: 0, period: RefreshIntervalMs);

// Only the main screen owns these inputs; modal dialogs retain their native controls.
Application.RootMouseEvent += mouseEvent =>
{
    if (Application.Current == top && actionView.HandleWheel(mouseEvent.Flags))
        mouseEvent.Handled = true;
};
Application.RootKeyEvent += keyEvent =>
{
    if (Application.Current != top) return false;
    if (keyEvent.Key == (Key)'q')
    {
        Application.RequestStop();
        return true;
    }
    return actionView.HandleKey(keyEvent.Key);
};

var renderedSize = Size.Empty;
window.LayoutComplete += _ =>
{
    if (renderedSize == actionView.Bounds.Size) return;
    renderedSize = actionView.Bounds.Size;
    RenderUi();
};

Application.Run();
Application.Shutdown();

static DockerState ReadDockerState()
{
    var (containers, containerError) = ReadDockerPs();
    var (volumes, volumeError) = ReadDockerVolumes();
    var (networks, networkError) = ReadDockerNetworks();
    return new DockerState(containers, volumes, networks, containerError, volumeError, networkError);
}

static (List<ContainerRow> Rows, string? Error) ReadDockerPs()
{
    var psi = new ProcessStartInfo
    {
        FileName = "docker",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    psi.ArgumentList.Add("ps");
    psi.ArgumentList.Add("--all");
    psi.ArgumentList.Add("--no-trunc");
    psi.ArgumentList.Add("--format");
    psi.ArgumentList.Add("{{.ID}}\t{{.Image}}\t{{.Status}}\t{{.State}}\t{{.Ports}}\t{{.Names}}");

    using var process = new Process { StartInfo = psi };

    try
    {
        process.Start();
    }
    catch (Exception ex)
    {
        return ([], $"Unable to start docker: {ex.Message}");
    }

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        return ([], string.IsNullOrWhiteSpace(stderr) ? "docker ps failed" : stderr.Trim());
    }

    var parsed = new List<ContainerRow>();
    foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var cols = line.Split('\t');
        if (cols.Length < 6)
        {
            continue;
        }

        parsed.Add(new ContainerRow(cols[0], cols[1], cols[2], cols[3], cols[4], cols[5]));
    }

    return (parsed, null);
}

static (List<VolumeRow> Rows, string? Error) ReadDockerVolumes()
{
    var (stdout, error) = RunDockerRead("volume", "ls", "--format", "{{.Name}}\t{{.Driver}}\t{{.Scope}}");
    if (error is not null) return ([], error);

    return (stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split('\t'))
        .Where(columns => columns.Length >= 3)
        .Select(columns => new VolumeRow(columns[0], columns[1], columns[2]))
        .ToList(), null);
}

static (List<NetworkRow> Rows, string? Error) ReadDockerNetworks()
{
    var (stdout, error) = RunDockerRead("network", "ls", "--no-trunc", "--format", "{{.ID}}\t{{.Name}}\t{{.Driver}}\t{{.Scope}}");
    if (error is not null) return ([], error);

    return (stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split('\t'))
        .Where(columns => columns.Length >= 4)
        .Select(columns => new NetworkRow(columns[0], columns[1], columns[2], columns[3]))
        .ToList(), null);
}

static (string Output, string? Error) RunDockerRead(params string[] arguments)
{
    var psi = CreateDockerProcess(arguments);
    using var process = new Process { StartInfo = psi };
    try { process.Start(); }
    catch (Exception ex) { return (string.Empty, $"Unable to start docker: {ex.Message}"); }

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode == 0
        ? (stdout, null)
        : (string.Empty, string.IsNullOrWhiteSpace(stderr) ? "docker list failed" : stderr.Trim());
}

static string? RunDockerCommand(params string[] arguments)
{
    var psi = CreateDockerProcess(arguments);

    using var process = new Process { StartInfo = psi };
    try
    {
        process.Start();
    }
    catch (Exception ex)
    {
        return $"Unable to start docker: {ex.Message}";
    }

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode == 0)
    {
        return null;
    }

    return string.IsNullOrWhiteSpace(stderr)
        ? string.IsNullOrWhiteSpace(stdout) ? $"docker {string.Join(' ', arguments.Take(2))} failed" : stdout.Trim()
        : stderr.Trim();
}

static ProcessStartInfo CreateDockerProcess(IEnumerable<string> arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = "docker",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (var argument in arguments)
    {
        psi.ArgumentList.Add(argument);
    }

    return psi;
}

static string RenderTable(
    IReadOnlyList<ContainerRow> rows,
    IReadOnlyList<VolumeRow> volumes,
    IReadOnlyList<NetworkRow> networks,
    string? error,
    string? volumeError,
    string? networkError,
    Column[] columns,
    int[] containerWidths,
    int[] resourceWidths,
    DateTime refreshedAt)
{
    var sb = new StringBuilder();
    AppendContainerTable(sb, rows, error, columns, containerWidths);
    AppendResourceTable(sb, "VOLUMES", volumes.Select(volume => new[] { volume.Name, volume.Driver, volume.Scope }), volumeError, resourceWidths);
    AppendResourceTable(sb, "NETWORKS", networks.Select(network => new[] { network.Name, network.Driver, network.Scope }), networkError, resourceWidths);
    sb.AppendLine($"Containers: {rows.Count} | Volumes: {volumes.Count} | Networks: {networks.Count} | Refresh: {refreshedAt:HH:mm:ss}");
    sb.AppendLine("GLOBAL ACTIONS");
    sb.Append("↑↓←→: select | Enter/Space: act | q: quit | Wheel: scroll");
    return sb.ToString();
}

static void AppendContainerTable(StringBuilder sb, IReadOnlyList<ContainerRow> rows, string? error, Column[] columns, int[] columnWidths)
{
    var merged = !string.IsNullOrWhiteSpace(error) || rows.Count == 0;
    sb.AppendLine("CONTAINERS");
    sb.AppendLine(HRule(columnWidths, "┌┬┐"));
    sb.AppendLine(RenderRow(columns.Select(column => column.Header).ToArray(), columnWidths));
    sb.AppendLine(HRule(columnWidths, merged ? "├┴┤" : "├┼┤"));

    if (!string.IsNullOrWhiteSpace(error))
    {
        sb.AppendLine(RenderMessageRow(columnWidths, $"ERROR: {error}"));
    }
    else if (rows.Count == 0)
    {
        sb.AppendLine(RenderMessageRow(columnWidths, "No containers"));
    }
    else
    {
        foreach (var row in rows)
        {
            sb.AppendLine(RenderRow(columns.Select(column => column.Value(row)).ToArray(), columnWidths));
        }
    }

    sb.AppendLine(HRule(columnWidths, merged ? "└─┘" : "└┴┘"));
}

static void AppendResourceTable(StringBuilder sb, string title, IEnumerable<string[]> rows, string? error, int[] columnWidths)
{
    var resourceRows = rows.ToList();
    var merged = !string.IsNullOrWhiteSpace(error) || resourceRows.Count == 0;
    sb.AppendLine(title);
    sb.AppendLine(HRule(columnWidths, "┌┬┐"));
    sb.AppendLine(RenderRow(["NAME", "DRIVER", "SCOPE", "ACTIONS"], columnWidths));
    sb.AppendLine(HRule(columnWidths, merged ? "├┴┤" : "├┼┤"));

    if (!string.IsNullOrWhiteSpace(error))
    {
        sb.AppendLine(RenderMessageRow(columnWidths, $"ERROR: {error}"));
    }
    else if (resourceRows.Count == 0)
    {
        sb.AppendLine(RenderMessageRow(columnWidths, $"No {title.ToLowerInvariant()}"));
    }
    else
    {
        foreach (var row in resourceRows)
        {
            sb.AppendLine(RenderRow([row[0], row[1], row[2], string.Empty], columnWidths));
        }
    }

    sb.AppendLine(HRule(columnWidths, merged ? "└─┘" : "└┴┘"));
}

static TablesLayout GetTableLayout(
    IReadOnlyList<ContainerRow> containers,
    IReadOnlyList<VolumeRow> volumes,
    IReadOnlyList<NetworkRow> networks,
    string? containerError,
    string? volumeError,
    string? networkError)
{
    var containerLayout = new TableLayout(0, GetDisplayedRowCount(containers.Count, containerError));
    var volumeLayout = new TableLayout(containerLayout.NextSectionY, GetDisplayedRowCount(volumes.Count, volumeError));
    var networkLayout = new TableLayout(volumeLayout.NextSectionY, GetDisplayedRowCount(networks.Count, networkError));
    return new TablesLayout(containerLayout, volumeLayout, networkLayout);
}

static int GetDisplayedRowCount(int count, string? error) => string.IsNullOrWhiteSpace(error) ? Math.Max(1, count) : 1;

static int ActionColumnX(IReadOnlyList<int> widths) => 2 + widths.Take(widths.Count - 1).Sum(width => width + 3);

// Small terminals show fewer columns instead of breaking layout.
static Column[] PickColumns(int width)
{
    if (width < 48)
    {
        return
        [
            new("NAME", 10, r => r.Name),
            new("STATUS", 8, r => r.Status),
            new("ACTIONS", 20, _ => string.Empty),
        ];
    }

    if (width < 78)
    {
        return
        [
            new("NAME", 12, r => r.Name),
            new("STATUS", 10, r => r.Status),
            new("ID", 8, r => r.Id),
            new("ACTIONS", 20, _ => string.Empty),
        ];
    }

    if (width < 108)
    {
        return
        [
            new("NAME", 12, r => r.Name),
            new("STATUS", 10, r => r.Status),
            new("ID", 8, r => r.Id),
            new("IMAGE", 12, r => r.Image),
            new("ACTIONS", 20, _ => string.Empty),
        ];
    }

    return
    [
        new("NAME", 12, r => r.Name),
        new("STATUS", 10, r => r.Status),
        new("ID", 8, r => r.Id),
        new("IMAGE", 12, r => r.Image),
        new("PORTS", 12, r => r.Ports),
        new("ACTIONS", 20, _ => string.Empty),
    ];
}

static int[] ComputeWidths(int[] minWidths, int totalWidth)
{
    var separators = (minWidths.Length * 3) + 1;
    var budget = Math.Max(minWidths.Length, totalWidth - separators);

    var widths = (int[])minWidths.Clone();
    var used = widths.Sum();

    // First shrink if minimums do not fit.
    while (used > budget)
    {
        var changed = false;
        for (var i = widths.Length - 2; i >= 0 && used > budget; i--)
        {
            if (widths[i] > 4)
            {
                widths[i]--;
                used--;
                changed = true;
            }
        }

        if (!changed)
        {
            break;
        }
    }

    // Then spread remaining width evenly.
    var extra = budget - used;
    for (var i = 0; extra > 0; i = (i + 1) % widths.Length)
    {
        widths[i]++;
        extra--;
    }

    return widths;
}

static string HRule(IReadOnlyList<int> widths, string joints) =>
    joints[0] + string.Join(joints[1], widths.Select(width => new string('─', width + 2))) + joints[2];

static string RenderRow(IReadOnlyList<string> values, IReadOnlyList<int> widths)
{
    var sb = new StringBuilder("│");
    for (var i = 0; i < values.Count; i++)
    {
        var cell = Fit(values[i], widths[i]);
        sb.Append(' ').Append(cell.PadRight(widths[i])).Append(' ').Append('│');
    }

    return sb.ToString();
}

static string RenderMessageRow(IReadOnlyList<int> widths, string message)
{
    var contentWidth = widths.Sum() + (widths.Count * 3) - 3;
    var cell = Fit(message, contentWidth);
    return $"│ {cell.PadRight(contentWidth)} │";
}

static string Fit(string value, int width)
{
    value = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    if (width <= 1)
    {
        return value.Length == 0 ? string.Empty : value[..1];
    }

    if (value.Length <= width)
    {
        return value;
    }

    return value[..(width - 1)] + "…";
}

static ColorScheme CreateMatrixScheme()
{
    var green = Terminal.Gui.Attribute.Make(Color.Green, Color.Black);
    var brightGreen = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black);

    var scheme = new ColorScheme
    {
        Normal = green,
        Focus = green,
        HotNormal = brightGreen,
        HotFocus = brightGreen,
        Disabled = green,
    };

    Colors.Base = scheme;
    Colors.TopLevel = scheme;
    Colors.Dialog = scheme;
    Colors.Menu = scheme;

    return scheme;
}

static ColorScheme CreateButtonScheme()
{
    var normal = Terminal.Gui.Attribute.Make(Color.Green, Color.Black);
    var focused = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightGreen);

    return new ColorScheme
    {
        Normal = normal,
        Focus = focused,
        HotNormal = normal,
        HotFocus = focused,
        Disabled = normal,
    };
}

sealed record DockerState(
    List<ContainerRow> Containers,
    List<VolumeRow> Volumes,
    List<NetworkRow> Networks,
    string? ContainerError,
    string? VolumeError,
    string? NetworkError)
{
    public static DockerState WithError(string error) => new([], [], [], error, error, error);
}

sealed record ContainerRow(string Id, string Image, string Status, string State, string Ports, string Name)
{
    public bool IsRunning => State == "running";
}
sealed record VolumeRow(string Name, string Driver, string Scope);
sealed record NetworkRow(string Id, string Name, string Driver, string Scope)
{
    public bool IsBuiltIn => Name is "bridge" or "host" or "none";
}
sealed record TableLayout(int StartY, int DisplayedRowCount)
{
    public int FirstDataRowY => StartY + 4;
    public int NextSectionY => StartY + 5 + DisplayedRowCount;
}
sealed record TablesLayout(TableLayout Containers, TableLayout Volumes, TableLayout Network)
{
    public int GlobalActionsY => Network.NextSectionY + 1;
    public int ContentHeight => GlobalActionsY + 2;
}
sealed record Column(string Header, int MinWidth, Func<ContainerRow, string> Value);
