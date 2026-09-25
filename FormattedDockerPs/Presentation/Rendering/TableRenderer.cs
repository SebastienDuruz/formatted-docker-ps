using System.Text;
using FormattedDockerPs.Application;
using FormattedDockerPs.Models;

namespace FormattedDockerPs.Presentation.Rendering;

internal static class TableRenderer
{
    public const int MinimumContentWidth = 44;

    public static RenderedDocument Render(DockerState state, int width, DateTime lastRefresh)
    {
        var rows = state.Containers;
        var volumes = state.Volumes;
        var networks = state.Networks;
        var lastError = state.ContainerError;
        var volumeError = state.VolumeError;
        var networkError = state.NetworkError;
        var layout = GetTableLayout(rows, volumes, networks, lastError, volumeError, networkError);
        var containerColumns = PickColumns(width);
        var containerWidths = ComputeWidths(containerColumns.Select(column => column.MinWidth).ToArray(), width);
        var resourceWidths = ComputeWidths([12, 10, 8, 10], width);
        var actions = new List<RenderedAction>();
        var actionX = ActionColumnX(containerWidths);

        if (string.IsNullOrWhiteSpace(lastError))
        {
            foreach (var (container, index) in rows.Select((row, index) => (row, index)))
            {
                var y = layout.Containers.FirstDataRowY + index;
                var label = container.IsRunning ? "Stop" : "Start";
                actions.Add(new($"container:{container.Id}:toggle", actionX, y, label,
                    new(container.IsRunning ? MonitorActionKind.StopContainer : MonitorActionKind.StartContainer, container.Id, container.Name)));
                // Reserve the width of Start so changing state never shifts Delete.
                actions.Add(new($"container:{container.Id}:delete", actionX + 10, y, "Delete",
                    new(MonitorActionKind.DeleteContainer, container.Id, container.Name)));
            }
        }

        var resourceX = ActionColumnX(resourceWidths);
        if (string.IsNullOrWhiteSpace(volumeError))
            foreach (var (volume, index) in volumes.Select((row, index) => (row, index)))
                actions.Add(new($"volume:{volume.Name}:delete", resourceX, layout.Volumes.FirstDataRowY + index,
                    "Delete", new(MonitorActionKind.DeleteVolume, volume.Name, volume.Name)));

        if (string.IsNullOrWhiteSpace(networkError))
            foreach (var (network, index) in networks.Select((row, index) => (row, index)))
                actions.Add(new($"network:{network.Id}:delete", resourceX, layout.Network.FirstDataRowY + index,
                    "Delete", new(MonitorActionKind.DeleteNetwork, network.Id, network.Name)));

        actions.Add(new("global:toggle", 17, layout.GlobalActionsY,
            rows.Any(container => container.IsRunning) ? "Stop all" : "Start all", new(MonitorActionKind.ToggleAll)));
        actions.Add(new("global:purge", 31, layout.GlobalActionsY, "Purge all", new(MonitorActionKind.PurgeAll)));

        return new(RenderTable(rows, volumes, networks, lastError, volumeError, networkError,
            containerColumns, containerWidths, resourceWidths, lastRefresh), layout.ContentHeight, actions);
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
        sb.AppendLine($" Containers: {rows.Count} | Volumes: {volumes.Count} | Networks: {networks.Count} | Refresh: {refreshedAt:HH:mm:ss}");
        sb.AppendLine(" GLOBAL ACTIONS");
        sb.Append(" ↑↓←→: select | Enter/Space: act | q: quit | Wheel: scroll");
        return sb.ToString();
    }

    static void AppendContainerTable(StringBuilder sb, IReadOnlyList<ContainerRow> rows, string? error, Column[] columns, int[] columnWidths)
    {
        var merged = !string.IsNullOrWhiteSpace(error) || rows.Count == 0;
        sb.AppendLine(" CONTAINERS");
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
        sb.Append(' ').AppendLine(title);
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

}
