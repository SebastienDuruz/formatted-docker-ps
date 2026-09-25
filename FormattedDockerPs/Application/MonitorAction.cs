namespace FormattedDockerPs.Application;

internal enum MonitorActionKind
{
    StartContainer,
    StopContainer,
    DeleteContainer,
    DeleteVolume,
    DeleteNetwork,
    ToggleAll,
    PurgeAll,
}

// Resource identity is captured at render time, just like the button's label.
internal sealed record MonitorAction(MonitorActionKind Kind, string ResourceId = "", string ResourceName = "");
