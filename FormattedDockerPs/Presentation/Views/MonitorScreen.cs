using FormattedDockerPs.Application;
using FormattedDockerPs.Presentation.Rendering;
using FormattedDockerPs.Presentation.Theme;
using Terminal.Gui;
using GuiApplication = Terminal.Gui.Application;

namespace FormattedDockerPs.Presentation.Views;

// Owns the main screen's subscriptions. Modal dialogs retain Terminal.Gui input handling.
internal sealed class MonitorScreen : IDisposable
{
    readonly Toplevel top;
    readonly MonitorController controller;
    readonly IMonitorDialogs dialogs;
    Size renderedSize = Size.Empty;

    public Window Window { get; }
    public ActionViewport Viewport { get; }

    public MonitorScreen(Toplevel top, MonitorController controller, IMonitorDialogs dialogs)
    {
        this.top = top;
        this.controller = controller;
        this.dialogs = dialogs;
        var matrix = MonitorTheme.CreateMatrixScheme();
        top.ColorScheme = matrix;
        Window = new Window("Docker PS Monitor")
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ColorScheme = matrix,
        };
        Viewport = new ActionViewport
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ColorScheme = matrix,
            ButtonScheme = MonitorTheme.CreateButtonScheme(),
        };
        Window.Add(Viewport);
        top.Add(Window);
        Window.LayoutComplete += _ =>
        {
            if (renderedSize == Viewport.Bounds.Size) return;
            renderedSize = Viewport.Bounds.Size;
            Render();
        };
        controller.StateChanged += Render;
        controller.CommandFailed += dialogs.ShowError;
        GuiApplication.RootKeyEvent += HandleKey;
        GuiApplication.RootMouseEvent += HandleMouse;
    }

    public void Render()
    {
        if (GuiApplication.Current != top || !controller.HasState) return;
        if (Viewport.Bounds.Width < TableRenderer.MinimumContentWidth || Viewport.Bounds.Height < 1)
        {
            Viewport.ShowSizeHint();
            return;
        }
        var document = TableRenderer.Render(controller.State, Viewport.Bounds.Width, controller.LastRefresh);
        var actions = document.Actions.Select(action => new ViewportAction(
            action.Id, action.X, action.Y, action.Label, () => Activate(action.Request))).ToArray();
        Viewport.SetContent(document.Text, document.Height, actions);
    }

    void Activate(MonitorAction action)
    {
        var confirmed = action.Kind switch
        {
            MonitorActionKind.DeleteContainer => dialogs.Confirm("Delete container",
                $"Delete '{action.ResourceName}'? A running container must be stopped first.", "Delete"),
            MonitorActionKind.DeleteVolume => dialogs.Confirm("Delete volume", $"Delete '{action.ResourceName}'?", "Delete"),
            MonitorActionKind.DeleteNetwork => dialogs.Confirm("Delete network", $"Delete '{action.ResourceName}'?", "Delete"),
            MonitorActionKind.PurgeAll => dialogs.Confirm("Purge Docker resources",
                "Delete all containers, volumes, and user-defined networks? Containers are removed forcibly. Docker built-in networks are preserved.", "Purge"),
            _ => true,
        };
        if (confirmed) _ = controller.ExecuteAsync(action);
    }

    bool HandleKey(KeyEvent keyEvent)
    {
        if (GuiApplication.Current != top) return false;
        if (keyEvent.Key == (Key)'q')
        {
            GuiApplication.RequestStop();
            return true;
        }
        return Viewport.HandleKey(keyEvent.Key);
    }

    void HandleMouse(MouseEvent mouseEvent)
    {
        if (GuiApplication.Current == top && Viewport.HandleWheel(mouseEvent.Flags)) mouseEvent.Handled = true;
    }

    public void Dispose()
    {
        GuiApplication.RootKeyEvent -= HandleKey;
        GuiApplication.RootMouseEvent -= HandleMouse;
        controller.StateChanged -= Render;
        controller.CommandFailed -= dialogs.ShowError;
        top.Remove(Window);
        Window.Dispose();
    }
}
