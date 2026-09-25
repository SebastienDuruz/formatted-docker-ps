using Terminal.Gui;

Application.Init(new FakeDriver());
try
{
    var viewport = new ActionViewport { Frame = new Rect(0, 0, 60, 8) };
    Application.Top.Add(viewport);
    using var run = Application.Begin(Application.Top);
    var calls = new List<string>();
    var actions = Enumerable.Range(0, 30).SelectMany(row => new[] {
        new ViewportAction($"{row}:toggle", 10, row + 4, "Start", () => calls.Add($"{row}:toggle")),
        new ViewportAction($"{row}:delete", 20, row + 4, "Delete", () => calls.Add($"{row}:delete")),
    }).ToList();
    void Render()
    {
        viewport.SetContent("CONTAINERS\n┌──────────────────────────────────────────────────────────┐", 40, actions);
        viewport.LayoutSubviews();
    }
    View Document() => viewport.Subviews.Single();
    Button Selected() => Document().Subviews.OfType<Button>().Single(button => button.HasFocus);
    int Scroll() => -Document().Frame.Y;
    void Check(bool condition, string scenario)
    {
        if (!condition) throw new Exception(scenario);
        Console.WriteLine($"PASS {scenario}");
    }
    void Visible(string scenario) => Check(Selected().Frame.Y >= Scroll() && Selected().Frame.Bottom <= Scroll() + viewport.Bounds.Height, scenario);
    void Press(Key key) => viewport.HandleKey(key);

    Render();
    Check(Selected().Frame == new Rect(10, 4, 9, 1), "first button selected");
    Press(Key.CursorLeft);
    Press(Key.CursorUp);
    Check(Selected().Frame.Y == 4 && Selected().Frame.X == 10, "no wrapping at start");
    Press(Key.CursorRight);
    Press(Key.CursorDown);
    Check(Selected().Frame.X == 20 && Selected().Frame.Y == 5, "spatial navigation retains action column");
    for (var i = 0; i < 20; i++) Press(Key.CursorDown);
    Visible("keyboard destination visible across multiple screens");
    Check(Scroll() == Selected().Frame.Bottom - 8, "minimal downward scroll");
    Render();
    Visible("refresh retains keyboard selection and visibility");
    var old = Selected();
    for (var i = 0; i < 20; i++) viewport.HandleWheel(MouseFlags.WheeledUp);
    Check(Scroll() == 0 && Selected() == old, "wheel scrolls freely without moving selection");
    Render();
    Check(Scroll() == 0 && Selected() == old, "refresh does not undo mouse scroll");
    Press(Key.CursorDown);
    Visible("arrow resumes selection after mouse scroll");
    viewport.HandleWheel(MouseFlags.WheeledDown);
    var offset = Scroll();
    viewport.HandleWheel(MouseFlags.WheeledLeft);
    viewport.HandleWheel(MouseFlags.WheeledRight);
    Check(Scroll() == offset && Document().Frame.X == 0, "horizontal wheel has no effect");
    for (var i = 0; i < 30; i++) Press(Key.CursorDown);
    Check(Selected().Frame.Y == 33, "no wrapping at bottom");
    old = Selected();
    actions.RemoveRange(0, 2);
    actions = actions.Select(action => action with { Y = action.Y - 1 }).ToList();
    Render();
    Check(Selected() == old && Selected().Frame.Y == 32, "identity survives removal above selection");
    actions.RemoveAt(actions.Count - 1);
    Render();
    Check(Selected().Frame.X == 10 && Selected().Frame.Y == 32, "removed final selection falls back to last action");
    viewport.Frame = new Rect(0, 0, 60, 3);
    Render();
    Visible("resize keeps keyboard selection visible");
    viewport.ShowSizeHint();
    Check(!Document().Visible && viewport.HandleKey(Key.Enter), "small terminal disables hidden actions");
    Render();
    Visible("selection restored after size hint");
    actions = [new("left", 10, 1, "Left", () => calls.Add("left")),
        new("right", 30, 1, "Right", () => calls.Add("right")),
        new("middle", 20, 5, "Middle", () => calls.Add("middle"))];
    Render();
    Press(Key.CursorDown);
    Press(Key.CursorUp);
    Check(Selected().Text == "Left", "vertical distance tie picks left button");
    Selected().ProcessKey(new KeyEvent(Key.Enter, new KeyModifiers()));
    Check(calls.SequenceEqual(new[] { "left" }), "Enter activates once");
    Selected().ProcessKey(new KeyEvent(Key.Space, new KeyModifiers()));
    Check(calls.Count == 2, "Space activates once");
    Selected().MouseEvent(new MouseEvent { X = 1, Y = 0, Flags = MouseFlags.Button1Clicked });
    Check(calls.Count == 3, "native click activates once");
    actions = [];
    Render();
    Check(Scroll() >= 0 && viewport.HandleKey(Key.CursorDown), "empty action list is safe");
    Application.End(run);
}
finally { Application.Shutdown(); }
