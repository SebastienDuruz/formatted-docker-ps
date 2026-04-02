using System.Diagnostics;
using System.Text;
using Terminal.Gui;

const int RefreshIntervalMs = 1000;

// App setup
Application.Init();
var matrix = CreateMatrixScheme();

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

var tableView = new TextView
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ReadOnly = true,
    WordWrap = false,
    ColorScheme = matrix,
};

window.Add(tableView);
top.Add(window);

var rows = new List<ContainerRow>();
string? lastError = null;
var lastRefresh = DateTime.Now;
var refreshInProgress = false;

void RefreshUi()
{
    if (refreshInProgress)
    {
        return;
    }

    refreshInProgress = true;
    try
    {
        (rows, lastError) = ReadDockerPs();
        lastRefresh = DateTime.Now;

        tableView.Text = RenderTable(
            rows,
            lastError,
            tableView.Bounds.Width,
            tableView.Bounds.Height,
            lastRefresh);
    }
    finally
    {
        refreshInProgress = false;
    }
}

// Refresh every second on the UI loop.
using var timer = new System.Threading.Timer(_ =>
{
    Application.MainLoop?.Invoke(RefreshUi);
}, null, dueTime: 0, period: RefreshIntervalMs);

// Keep controls intentionally minimal.
window.KeyPress += args =>
{
    if (args.KeyEvent.Key == Key.Q || args.KeyEvent.Key == (Key.CtrlMask | Key.Q))
    {
        Application.RequestStop();
        args.Handled = true;
    }
};

window.Resized += _ => RefreshUi();

Application.Run();
Application.Shutdown();

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
    psi.ArgumentList.Add("--no-trunc");
    psi.ArgumentList.Add("--format");
    psi.ArgumentList.Add("{{.ID}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}\t{{.Names}}");

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
        if (cols.Length < 5)
        {
            continue;
        }

        parsed.Add(new ContainerRow(cols[0], cols[1], cols[2], cols[3], cols[4]));
    }

    return (parsed, null);
}

static string RenderTable(IReadOnlyList<ContainerRow> rows, string? error, int width, int height, DateTime refreshedAt)
{
    width = Math.Max(20, width);
    height = Math.Max(6, height);

    var columns = PickColumns(width);
    var columnWidths = ComputeWidths(columns.Select(c => c.MinWidth).ToArray(), width);

    var sb = new StringBuilder();

    if (!string.IsNullOrWhiteSpace(error))
    {
        sb.AppendLine($"ERROR: {error}");
        sb.AppendLine();
    }

    sb.AppendLine(HRule(columnWidths));
    sb.AppendLine(RenderRow(columns.Select(c => c.Header).ToArray(), columnWidths));
    sb.AppendLine(HRule(columnWidths));

    var maxRows = Math.Max(1, height - 6 - (string.IsNullOrWhiteSpace(error) ? 0 : 2));
    var visible = rows.Take(maxRows).ToList();

    foreach (var row in visible)
    {
        var values = columns.Select(c => c.Value(row)).ToArray();
        sb.AppendLine(RenderRow(values, columnWidths));
    }

    if (visible.Count == 0)
    {
        sb.AppendLine(RenderMessageRow(columnWidths, "No running containers"));
    }
    else if (rows.Count > visible.Count)
    {
        sb.AppendLine(RenderMessageRow(columnWidths, $"... {rows.Count - visible.Count} hidden row(s) ..."));
    }

    sb.AppendLine(HRule(columnWidths));
    sb.Append($"Containers: {rows.Count} | Refresh: {refreshedAt:HH:mm:ss} | q: quit");

    return sb.ToString();
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
        ];
    }

    if (width < 78)
    {
        return
        [
            new("NAME", 12, r => r.Name),
            new("STATUS", 10, r => r.Status),
            new("ID", 8, r => r.Id),
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
        ];
    }

    return
    [
        new("NAME", 12, r => r.Name),
        new("STATUS", 10, r => r.Status),
        new("ID", 8, r => r.Id),
        new("IMAGE", 12, r => r.Image),
        new("PORTS", 12, r => r.Ports),
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

sealed record ContainerRow(string Id, string Image, string Status, string Ports, string Name);
sealed record Column(string Header, int MinWidth, Func<ContainerRow, string> Value);
