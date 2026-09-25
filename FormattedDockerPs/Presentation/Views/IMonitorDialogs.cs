using Terminal.Gui;

namespace FormattedDockerPs.Presentation.Views;

internal interface IMonitorDialogs
{
    bool Confirm(string title, string message, string accept);
    void ShowError(string title, string message);
}

internal sealed class MonitorDialogs : IMonitorDialogs
{
    public bool Confirm(string title, string message, string accept) =>
        MessageBox.Query(title, message, accept, "Cancel") == 0;
    public void ShowError(string title, string message) => MessageBox.ErrorQuery(title, message, "OK");
}
