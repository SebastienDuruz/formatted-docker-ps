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

// Identifies what a pending operation locks. Shared by the controller and the renderer.
internal static class ResourceKey
{
    public const string Global = "global";
    public static string Container(string id) => $"container:{id}";
    public static string Volume(string name) => $"volume:{name}";
    public static string Network(string id) => $"network:{id}";
}
