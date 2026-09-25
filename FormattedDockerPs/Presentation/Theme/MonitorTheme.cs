using Terminal.Gui;

namespace FormattedDockerPs.Presentation.Theme;

internal static class MonitorTheme
{
    public static ColorScheme CreateMatrixScheme()
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

    public static ColorScheme CreateButtonScheme()
    {
        var normal = Terminal.Gui.Attribute.Make(Color.Green, Color.Black);
        var focused = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightGreen);

        return new ColorScheme
        {
            Normal = normal,
            Focus = focused,
            HotNormal = normal,
            HotFocus = focused,
            Disabled = normal,
        };
    }

}
