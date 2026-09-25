using FormattedDockerPs.Application;
using FormattedDockerPs.Docker;
using FormattedDockerPs.Presentation.Views;
using Terminal.Gui;

const int RefreshIntervalMs = 1000;

Application.Init();
try
{
    var docker = new DockerClient(new DockerProcessRunner());
    using var controller = new MonitorController(docker, action => Application.MainLoop?.Invoke(action));
    using var screen = new MonitorScreen(Application.Top, controller, new MonitorDialogs());
    using var timer = new System.Threading.Timer(timerState =>
        Application.MainLoop?.Invoke(() => { _ = controller.RefreshAsync(); }),
        null, dueTime: 0, period: RefreshIntervalMs);
    Application.Run();
}
finally { Application.Shutdown(); }
