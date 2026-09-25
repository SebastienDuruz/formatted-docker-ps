using FormattedDockerPs.Models;

namespace FormattedDockerPs.Presentation.Rendering;

internal sealed record TableLayout(int StartY, int DisplayedRowCount)
{
    public int FirstDataRowY => StartY + 4;
    public int NextSectionY => StartY + 5 + DisplayedRowCount;
}
internal sealed record TablesLayout(TableLayout Containers, TableLayout Volumes, TableLayout Network)
{
    public int GlobalActionsY => Network.NextSectionY + 1;
    public int ContentHeight => GlobalActionsY + 2;
}
internal sealed record Column(string Header, int MinWidth, Func<ContainerRow, string> Value);
