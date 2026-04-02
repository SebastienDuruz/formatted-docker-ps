using System.Diagnostics;
using System.Text;
using Terminal.Gui;

const int RefreshIntervalMs = 1000;

var state = new UiState();

Application.Init();
var matrixScheme = ApplyGreenOnBlackTheme();

var top = Application.Top;
top.ColorScheme = matrixScheme;
var window = new Window("Docker PS Monitor")
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
};
window.ColorScheme = matrixScheme;

var tableView = new TextView
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ReadOnly = true,
    WordWrap = false,
};
tableView.ColorScheme = matrixScheme;

window.Add(tableView);
top.Add(window);

void RefreshUi()
{
    if (state.IsRefreshing)
    {
        return;
    }

    state.IsRefreshing = true;
    try
    {
        var result = DockerReader.ReadDockerPs();
        state.Rows = result.Rows;
        state.LastError = result.Error;
        state.LastRefresh = DateTime.Now;

        var table = TableRenderer.Render(
            state.Rows,
            tableView.Bounds.Width,
            tableView.Bounds.Height,
            state.LastRefresh,
            state.LastError);

        tableView.Text = table;
    }
    finally
    {
        state.IsRefreshing = false;
    }
}

var timer = new System.Threading.Timer(_ =>
{
    Application.MainLoop?.Invoke(RefreshUi);
}, null, dueTime: 0, period: RefreshIntervalMs);

window.KeyPress += args =>
{
    if (args.KeyEvent.Key == Key.Q || args.KeyEvent.Key == (Key.CtrlMask | Key.Q))
    {
        timer.Dispose();
        Application.RequestStop();
        args.Handled = true;
        return;
    }
};

window.Resized += _ => RefreshUi();

Application.Run();
timer.Dispose();
Application.Shutdown();

static ColorScheme ApplyGreenOnBlackTheme()
{
    var normal = Terminal.Gui.Attribute.Make(Color.Green, Color.Black);
    var focus = Terminal.Gui.Attribute.Make(Color.Green, Color.Black);
    var hotNormal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black);
    var hotFocus = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black);
    var matrix = new ColorScheme
    {
        Normal = normal,
        Focus = focus,
        HotNormal = hotNormal,
        HotFocus = hotFocus,
        Disabled = Terminal.Gui.Attribute.Make(Color.Green, Color.Black),
    };

    Colors.Base = matrix;
    Colors.TopLevel = matrix;
    Colors.Dialog = matrix;
    Colors.Menu = matrix;
    Colors.Error = new ColorScheme
    {
        Normal = Terminal.Gui.Attribute.Make(Color.BrightRed, Color.Black),
        Focus = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightRed),
        HotNormal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black),
        HotFocus = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightYellow),
        Disabled = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Black),
    };

    return matrix;
}

static class DockerReader
{
    public static DockerResult ReadDockerPs()
    {
        var dockerPath = ResolveDockerBinary();
        if (dockerPath is null)
        {
            return new DockerResult([], "docker introuvable dans le PATH");
        }

        var psi = new ProcessStartInfo
        {
            FileName = dockerPath,
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
            return new DockerResult([], $"Impossible de lancer docker: {ex.Message}");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(stderr) ? "docker ps a échoué" : stderr.Trim();
            return new DockerResult([], err);
        }

        var rows = new List<ContainerRow>();
        foreach (var rawLine in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = rawLine.Split('\t');
            if (cols.Length < 5)
            {
                continue;
            }

            rows.Add(new ContainerRow(
                Id: cols[0],
                Image: cols[1],
                Status: cols[2],
                Ports: cols[3],
                Name: cols[4]));
        }

        return new DockerResult(rows, null);
    }

    private static string? ResolveDockerBinary()
    {
        var envPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(envPath))
        {
            return null;
        }

        foreach (var dir in envPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "docker");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

static class TableRenderer
{
    public static string Render(
        IReadOnlyList<ContainerRow> rows,
        int width,
        int height,
        DateTime lastRefresh,
        string? error)
    {
        width = Math.Max(20, width);
        height = Math.Max(6, height);

        var specs = PickColumns(width);
        var colWidths = ComputeColumnWidths(specs, width);

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(error))
        {
            sb.AppendLine($"ERREUR: {error}");
            sb.AppendLine();
        }

        sb.AppendLine(HorizontalRule(colWidths));
        sb.AppendLine(RenderRow(specs, colWidths, isHeader: true));
        sb.AppendLine(HorizontalRule(colWidths));

        var rowsAvailable = Math.Max(1, height - 6 - (string.IsNullOrWhiteSpace(error) ? 0 : 2));
        var visible = rows.Take(rowsAvailable).ToList();

        foreach (var row in visible)
        {
            sb.AppendLine(RenderRow(specs, colWidths, row));
        }

        if (visible.Count == 0)
        {
            sb.AppendLine(RenderEmptyRow(colWidths, "Aucun conteneur en cours"));
        }

        if (rows.Count > visible.Count)
        {
            sb.AppendLine(RenderEmptyRow(colWidths, $"... {rows.Count - visible.Count} ligne(s) masquée(s) ..."));
        }

        sb.AppendLine(HorizontalRule(colWidths));
        sb.Append($"Conteneurs: {rows.Count} | Refresh: {lastRefresh:HH:mm:ss} | q: quitter");

        return sb.ToString();
    }

    private static List<ColumnSpec> PickColumns(int width)
    {
        if (width < 40)
        {
            return [new("NAME", 8, 44, row => row.Name)];
        }

        var cols = new List<ColumnSpec>
        {
            new("NAME", 12, 44, row => row.Name),
            new("STATUS", 8, 34, row => row.Status),
        };

        if (width >= 55)
        {
            cols.Add(new("ID", 8, 12, row => row.Id));
        }

        if (width >= 75)
        {
            cols.Add(new("IMAGE", 16, 46, row => row.Image));
        }

        if (width >= 100)
        {
            cols.Add(new("PORTS", 12, 40, row => row.Ports));
        }

        return cols;
    }

    private static List<int> ComputeColumnWidths(IReadOnlyList<ColumnSpec> specs, int totalWidth)
    {
        var separators = (specs.Count * 3) + 1;
        var contentBudget = Math.Max(specs.Count, totalWidth - separators);

        var widths = specs.Select(s => s.MinWidth).ToList();
        var used = widths.Sum();

        if (used > contentBudget)
        {
            var deficit = used - contentBudget;
            while (deficit > 0)
            {
                var shrunk = false;
                for (var i = 0; i < widths.Count && deficit > 0; i++)
                {
                    if (widths[i] > 4)
                    {
                        widths[i]--;
                        deficit--;
                        shrunk = true;
                    }
                }

                if (!shrunk)
                {
                    break;
                }
            }
        }

        used = widths.Sum();
        var extra = Math.Max(0, contentBudget - used);

        var open = true;
        while (extra > 0 && open)
        {
            open = false;
            for (var i = 0; i < specs.Count && extra > 0; i++)
            {
                if (widths[i] < specs[i].MaxWidth)
                {
                    widths[i]++;
                    extra--;
                    open = true;
                }
            }
        }

        return widths;
    }

    private static string HorizontalRule(IReadOnlyList<int> widths)
    {
        var sb = new StringBuilder();
        sb.Append('+');
        foreach (var w in widths)
        {
            sb.Append(' ', 1);
            sb.Append('-', w);
            sb.Append(' ', 1);
            sb.Append('+');
        }

        return sb.ToString();
    }

    private static string RenderRow(IReadOnlyList<ColumnSpec> specs, IReadOnlyList<int> widths, ContainerRow? row = null, bool isHeader = false)
    {
        var sb = new StringBuilder();
        sb.Append('|');

        for (var i = 0; i < specs.Count; i++)
        {
            var text = isHeader ? specs[i].Header : specs[i].Getter(row!);
            var fitted = Fit(text, widths[i]);
            sb.Append(' ');
            sb.Append(fitted.PadRight(widths[i]));
            sb.Append(' ');
            sb.Append('|');
        }

        return sb.ToString();
    }

    private static string RenderEmptyRow(IReadOnlyList<int> widths, string text)
    {
        var joinedWidth = widths.Sum() + (widths.Count * 3) - 1;
        var fitted = Fit(text, joinedWidth);
        return $"| {fitted.PadRight(joinedWidth)} |";
    }

    private static string Fit(string? value, int width)
    {
        value ??= string.Empty;
        if (width <= 1)
        {
            return value.Length == 0 ? "" : value[..1];
        }

        if (value.Length <= width)
        {
            return value;
        }

        return value[..(width - 1)] + "…";
    }
}

sealed class UiState
{
    public bool IsRefreshing { get; set; }
    public List<ContainerRow> Rows { get; set; } = [];
    public DateTime LastRefresh { get; set; } = DateTime.Now;
    public string? LastError { get; set; }
}

sealed record ContainerRow(string Id, string Image, string Status, string Ports, string Name);
sealed record DockerResult(List<ContainerRow> Rows, string? Error);
sealed record ColumnSpec(string Header, int MinWidth, int MaxWidth, Func<ContainerRow, string> Getter);
