using System.Diagnostics;
using System.Text;
using Terminal.Gui;

const int RefreshIntervalMs = 1000;

// App setup
Application.Init();
var matrix = CreateMatrixScheme();
var buttonScheme = CreateButtonScheme();
var buttonHover = CreateButtonHoverScheme();

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

var scrollView = new ScrollView
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    CanFocus = true,
    AutoHideScrollBars = true,
    ShowHorizontalScrollIndicator = false,
    ShowVerticalScrollIndicator = true,
};

var tableView = new TextView
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ReadOnly = true,
    CanFocus = false,
    WordWrap = false,
    ColorScheme = matrix,
};

scrollView.Add(tableView);
window.Add(scrollView);
top.Add(window);

var rows = new List<ContainerRow>();
var volumes = new List<VolumeRow>();
var networks = new List<NetworkRow>();
var actionButtons = new List<Button>();
string? lastError = null;
string? volumeError = null;
string? networkError = null;
var lastRefresh = DateTime.Now;
var refreshInProgress = false;
var dockerCommandInProgress = false;

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
    rows = state.Containers;
    volumes = state.Volumes;
    networks = state.Networks;
    lastError = state.ContainerError;
    volumeError = state.VolumeError;
    networkError = state.NetworkError;
    lastRefresh = DateTime.Now;

    // Keep the vertical scroll position while the periodic refresh replaces the content.
    var contentOffset = scrollView.ContentOffset;
    contentOffset.X = 0;
    var layout = GetTableLayout(rows, volumes, networks, lastError, volumeError, networkError);
    var contentHeight = layout.ContentHeight;
    // The vertical indicator consumes one column. Reserve it before sizing the
    // content; otherwise the content is one column too wide and ScrollView adds
    // a horizontal scrollbar.
    var visibleContentWidth = Math.Max(
        1,
        scrollView.Bounds.Width - (contentHeight > scrollView.Bounds.Height ? 1 : 0));

    tableView.Text = RenderTable(
        rows,
        volumes,
        networks,
        lastError,
        volumeError,
        networkError,
        visibleContentWidth,
        scrollView.Bounds.Height,
        lastRefresh);

    scrollView.ContentSize = new Size(visibleContentWidth, contentHeight);
    tableView.Width = scrollView.ContentSize.Width;
    tableView.Height = contentHeight;
    RebuildActionButtons(rows, volumes, networks, lastError, volumeError, networkError, scrollView.ContentSize.Width, layout);
    scrollView.ContentOffset = contentOffset;
}

void RebuildActionButtons(
    IReadOnlyList<ContainerRow> containers,
    IReadOnlyList<VolumeRow> volumeRows,
    IReadOnlyList<NetworkRow> networkRows,
    string? error,
    string? volumesError,
    string? networksError,
    int width,
    TablesLayout layout)
{
    // The content is refreshed every second. Remember the focused button so a
    // keyboard user does not lose their place whenever the controls are rebuilt.
    var focusedButtonIndex = actionButtons.FindIndex(button => button.HasFocus);

    foreach (var button in actionButtons)
    {
        scrollView.Remove(button);
    }

    actionButtons.Clear();

    if (string.IsNullOrWhiteSpace(error))
    {
        var columns = PickColumns(width);
        var actionColumnIndex = Array.FindIndex(columns, column => column.Header == "ACTIONS");
        var columnWidths = ComputeWidths(columns.Select(column => column.MinWidth).ToArray(), Math.Max(20, width));
        var actionColumnX = 1 + columnWidths.Take(actionColumnIndex).Sum(columnWidth => columnWidth + 3) + 1;

        foreach (var (container, index) in containers.Select((container, index) => (container, index)))
        {
            var action = container.IsRunning ? "Stop" : "Start";
            var actionButton = new Button(actionColumnX, layout.Containers.FirstDataRowY + index, action) { ColorScheme = buttonScheme };
            EnableHoverHighlight(actionButton);
            actionButton.Clicked += () => ExecuteContainerAction(container, action.ToLowerInvariant());

            var deleteButton = new Button(actionColumnX + actionButton.Bounds.Width + 1, layout.Containers.FirstDataRowY + index, "Delete") { ColorScheme = buttonScheme };
            EnableHoverHighlight(deleteButton);
            deleteButton.Clicked += () => DeleteContainer(container);
            AddActionButton(actionButton);
            AddActionButton(deleteButton);
        }
    }

    var resourceActionX = GetResourceActionX(width);
    if (string.IsNullOrWhiteSpace(volumesError))
    {
        foreach (var (volume, index) in volumeRows.Select((volume, index) => (volume, index)))
        {
            var deleteButton = new Button(resourceActionX, layout.Volumes.FirstDataRowY + index, "Delete") { ColorScheme = buttonScheme };
            EnableHoverHighlight(deleteButton);
            deleteButton.Clicked += () => DeleteVolume(volume);
            AddActionButton(deleteButton);
        }
    }

    if (string.IsNullOrWhiteSpace(networksError))
    {
        foreach (var (network, index) in networkRows.Select((network, index) => (network, index)))
        {
            var deleteButton = new Button(resourceActionX, layout.Network.FirstDataRowY + index, "Delete") { ColorScheme = buttonScheme };
            EnableHoverHighlight(deleteButton);
            deleteButton.Clicked += () => DeleteNetwork(network);
            AddActionButton(deleteButton);
        }
    }

    var allContainersAction = containers.Any(container => container.IsRunning) ? "Stop all" : "Start all";
    var allContainersButton = new Button(16, layout.GlobalActionsY, allContainersAction) { ColorScheme = buttonScheme };
    EnableHoverHighlight(allContainersButton);
    allContainersButton.Clicked += ToggleAllContainers;
    AddActionButton(allContainersButton);

    var purgeButton = new Button(29, layout.GlobalActionsY, "Purge all") { ColorScheme = buttonScheme };
    EnableHoverHighlight(purgeButton);
    purgeButton.Clicked += PurgeAllDockerResources;
    AddActionButton(purgeButton);

    if (focusedButtonIndex >= 0 && focusedButtonIndex < actionButtons.Count)
    {
        actionButtons[focusedButtonIndex].SetFocus();
    }
}

void AddActionButton(Button button)
{
    // ScrollView is focusable for scrolling, so opt buttons into the tab order
    // explicitly. Button then provides its normal Enter/Space activation.
    button.CanFocus = true;
    button.TabStop = true;
    scrollView.Add(button);
    actionButtons.Add(button);
}

void EnableHoverHighlight(Button button)
{
    button.MouseEnter += _ =>
    {
        button.ColorScheme = buttonHover;
        button.SetNeedsDisplay();
    };

    button.MouseLeave += _ =>
    {
        button.ColorScheme = buttonScheme;
        button.SetNeedsDisplay();
    };
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

// Make the document scrollable with the mouse wheel even when a button has focus.
Application.RootMouseEvent += mouseEvent =>
{
    if (mouseEvent.Handled)
    {
        return;
    }

    if ((mouseEvent.Flags & MouseFlags.WheeledUp) != 0)
    {
        mouseEvent.Handled = scrollView.ScrollUp(3);
    }
    else if ((mouseEvent.Flags & MouseFlags.WheeledDown) != 0)
    {
        mouseEvent.Handled = scrollView.ScrollDown(3);
    }
};

// Handle quitting before the focused view can consume the key.
Application.RootKeyEvent += keyEvent =>
{
    if (keyEvent.Key == Key.Q)
    {
        Application.RequestStop();
        return true;
    }

    if (keyEvent.Key == Key.Tab || keyEvent.Key == (Key.ShiftMask | Key.Tab))
    {
        FocusNextActionButton(keyEvent.Key == (Key.ShiftMask | Key.Tab));
        return true;
    }

    // Ctrl+Q is deliberately ignored; q is the sole quit shortcut.
    return keyEvent.Key == (Key.CtrlMask | Key.Q);
};

void FocusNextActionButton(bool reverse)
{
    if (actionButtons.Count == 0)
    {
        return;
    }

    var focusedIndex = actionButtons.FindIndex(button => button.HasFocus);
    var nextIndex = focusedIndex < 0
        ? (reverse ? actionButtons.Count - 1 : 0)
        : (focusedIndex + (reverse ? -1 : 1) + actionButtons.Count) % actionButtons.Count;
    var nextButton = actionButtons[nextIndex];
    nextButton.SetFocus();
    ScrollIntoView(nextButton);
}

void ScrollIntoView(View view)
{
    var offset = scrollView.ContentOffset;
    var viewportHeight = scrollView.Bounds.Height;
    var viewTop = view.Bounds.Y;
    var viewBottom = viewTop + view.Bounds.Height;
    var viewportBottom = offset.Y + viewportHeight;

    if (viewTop < offset.Y)
    {
        offset.Y = viewTop;
    }
    else if (viewBottom > viewportBottom)
    {
        offset.Y = viewBottom - viewportHeight;
    }
    else
    {
        return;
    }

    offset.Y = Math.Clamp(offset.Y, 0, Math.Max(0, scrollView.ContentSize.Height - viewportHeight));
    scrollView.ContentOffset = offset;
}

window.Resized += _ => RefreshUi();

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
    int width,
    int height,
    DateTime refreshedAt)
{
    width = Math.Max(20, width);
    var sb = new StringBuilder();
    AppendContainerTable(sb, rows, error, width);
    AppendResourceTable(sb, "VOLUMES", volumes.Select(volume => new[] { volume.Name, volume.Driver, volume.Scope }), volumeError, width);
    AppendResourceTable(sb, "NETWORKS", networks.Select(network => new[] { network.Name, network.Driver, network.Scope }), networkError, width);
    sb.AppendLine($"Containers: {rows.Count} | Volumes: {volumes.Count} | Networks: {networks.Count} | Refresh: {refreshedAt:HH:mm:ss} | shift+q: quit");
    sb.Append("GLOBAL ACTIONS");
    return sb.ToString();
}

static void AppendContainerTable(StringBuilder sb, IReadOnlyList<ContainerRow> rows, string? error, int width)
{
    var columns = PickColumns(width);
    var columnWidths = ComputeWidths(columns.Select(column => column.MinWidth).ToArray(), width);
    sb.AppendLine("CONTAINERS");
    sb.AppendLine(HRule(columnWidths));
    sb.AppendLine(RenderRow(columns.Select(column => column.Header).ToArray(), columnWidths));
    sb.AppendLine(HRule(columnWidths));

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

    sb.AppendLine(HRule(columnWidths));
}

static void AppendResourceTable(StringBuilder sb, string title, IEnumerable<string[]> rows, string? error, int width)
{
    var columnWidths = ComputeWidths([12, 10, 8, 15], Math.Max(20, width));
    var resourceRows = rows.ToList();
    sb.AppendLine(title);
    sb.AppendLine(HRule(columnWidths));
    sb.AppendLine(RenderRow(["NAME", "DRIVER", "SCOPE", "ACTIONS"], columnWidths));
    sb.AppendLine(HRule(columnWidths));

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

    sb.AppendLine(HRule(columnWidths));
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

static int GetResourceActionX(int width)
{
    var columnWidths = ComputeWidths([12, 10, 8, 15], Math.Max(20, width));
    return columnWidths.Take(3).Sum() + 11;
}

// Small terminals show fewer columns instead of breaking layout.
static Column[] PickColumns(int width)
{
    if (width < 48)
    {
        return
        [
            new("NAME", 10, r => r.Name),
            new("STATUS", 8, r => r.Status),
            new("ACTIONS", 15, _ => string.Empty),
        ];
    }

    if (width < 78)
    {
        return
        [
            new("NAME", 12, r => r.Name),
            new("STATUS", 10, r => r.Status),
            new("ID", 8, r => r.Id),
            new("ACTIONS", 15, _ => string.Empty),
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
            new("ACTIONS", 15, _ => string.Empty),
        ];
    }

    return
    [
        new("NAME", 12, r => r.Name),
        new("STATUS", 10, r => r.Status),
        new("ID", 8, r => r.Id),
        new("IMAGE", 12, r => r.Image),
        new("PORTS", 12, r => r.Ports),
        new("ACTIONS", 15, _ => string.Empty),
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
        for (var i = widths.Length - 1; i >= 0 && used > budget; i--)
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

static string HRule(IReadOnlyList<int> widths)
{
    var sb = new StringBuilder("+");
    foreach (var w in widths)
    {
        sb.Append(' ', 1).Append('-', w).Append(' ', 1).Append('+');
    }

    return sb.ToString();
}

static string RenderRow(IReadOnlyList<string> values, IReadOnlyList<int> widths)
{
    var sb = new StringBuilder("|");
    for (var i = 0; i < values.Count; i++)
    {
        var cell = Fit(values[i], widths[i]);
        sb.Append(' ').Append(cell.PadRight(widths[i])).Append(' ').Append('|');
    }

    return sb.ToString();
}

static string RenderMessageRow(IReadOnlyList<int> widths, string message)
{
    var contentWidth = widths.Sum() + (widths.Count * 3) - 1;
    var cell = Fit(message, contentWidth);
    return $"| {cell.PadRight(contentWidth)} |";
}

static string Fit(string value, int width)
{
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

static ColorScheme CreateButtonHoverScheme()
{
    var highlighted = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightGreen);

    return new ColorScheme
    {
        Normal = highlighted,
        Focus = highlighted,
        HotNormal = highlighted,
        HotFocus = highlighted,
        Disabled = highlighted,
    };
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
    public int ContentHeight => GlobalActionsY + 1;
}
sealed record Column(string Header, int MinWidth, Func<ContainerRow, string> Value);
