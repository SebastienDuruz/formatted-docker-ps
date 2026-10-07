using Terminal.Gui;

namespace FormattedDockerPs.Presentation.Views;

// Compact button: "[Label]", or "▸Label◂" when focused. Focus avoids a background color,
// because a terminal cell background also fills the line spacing above the text.
internal sealed class ActionButton : View
{
    string label;

    public event Action? Clicked;

    public ActionButton(int x, int y, string label) : base(new Rect(x, y, WidthOf(label), 1))
    {
        this.label = label;
        CanFocus = true;
    }

    public static int WidthOf(string label) => label.Length + 2;

    public string Label
    {
        get => label;
        set
        {
            if (label == value) return;
            label = value;
            SetNeedsDisplay();
        }
    }

    public override void Redraw(Rect bounds)
    {
        Driver.SetAttribute(HasFocus ? ColorScheme.Focus : ColorScheme.Normal);
        Move(0, 0);
        Driver.AddStr(HasFocus ? $"▸{label}◂" : $"[{label}]");
    }

    // Same cursor position as Terminal.Gui buttons: the first letter of the label.
    public override void PositionCursor() => Move(1, 0);

    public override bool OnEnter(View view)
    {
        SetNeedsDisplay();
        return base.OnEnter(view);
    }

    public override bool OnLeave(View view)
    {
        SetNeedsDisplay();
        return base.OnLeave(view);
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        if (keyEvent.Key is not (Key.Enter or Key.Space)) return base.ProcessKey(keyEvent);
        Clicked?.Invoke();
        return true;
    }

    public override bool MouseEvent(MouseEvent mouseEvent)
    {
        // Same click flags as Terminal.Gui buttons.
        if (mouseEvent.Flags is not (MouseFlags.Button1Clicked or MouseFlags.Button1DoubleClicked or MouseFlags.Button1TripleClicked))
            return false;
        Clicked?.Invoke();
        return true;
    }
}
