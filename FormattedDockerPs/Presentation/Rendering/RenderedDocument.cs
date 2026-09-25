using FormattedDockerPs.Application;

namespace FormattedDockerPs.Presentation.Rendering;

internal sealed record RenderedAction(string Id, int X, int Y, string Label, MonitorAction Request);
internal sealed record RenderedDocument(string Text, int Height, IReadOnlyList<RenderedAction> Actions);
