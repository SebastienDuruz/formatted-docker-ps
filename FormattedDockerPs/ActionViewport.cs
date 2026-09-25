using Terminal.Gui;

sealed record ViewportAction(string Id, int X, int Y, string Label, Action Invoke);

// One clipped document, one vertical offset. Buttons keep their content coordinates.
sealed class ActionViewport : View
{
    readonly View document = new(new Rect(0, 0, 1, 1)) { CanFocus = true };
    readonly Label text = new(new Rect(0, 0, 1, 1), "") { CanFocus = false, AutoSize = false };
    readonly Dictionary<string, Button> buttons = new();
    IReadOnlyList<ViewportAction> actions = Array.Empty<ViewportAction>();
    string? selectedId;
    int scrollTop;
    int contentHeight;
    bool followSelection = true;

    public ColorScheme ButtonScheme { get; init; } = Colors.Base;

    public ActionViewport()
    {
        CanFocus = true;
        text.HotKeySpecifier = new System.Rune(0xffff);
        document.Add(text);
        Add(document);
    }

    public void SetContent(string value, int height, IReadOnlyList<ViewportAction> nextActions)
    {
        var previousIndex = Math.Max(0, actions.ToList().FindIndex(action => action.Id == selectedId));
        var nextIds = nextActions.Select(action => action.Id).ToHashSet();
        foreach (var id in buttons.Keys.Where(id => !nextIds.Contains(id)).ToArray())
        {
            document.Remove(buttons[id]);
            buttons[id].Dispose();
            buttons.Remove(id);
        }

        var wasHidden = !document.Visible;
        document.Visible = true;
        // Re-establish the native focus chain after hiding the document.
        if (wasHidden) document.FocusFirst();
        actions = nextActions.ToArray();
        contentHeight = height;
        text.Text = string.Join('\n', value.Split('\n').Select(line => line[..Math.Min(line.Length, Bounds.Width)]));
        text.Frame = new Rect(0, 0, Bounds.Width, height);
        foreach (var action in actions)
        {
            if (!buttons.TryGetValue(action.Id, out var button))
            {
                button = new Button(action.X, action.Y, action.Label) { ColorScheme = ButtonScheme };
                var id = action.Id;
                button.Clicked += () =>
                {
                    selectedId = id;
                    followSelection = true;
                    buttons[id].SetFocus();
                    actions.First(item => item.Id == id).Invoke();
                };
                buttons.Add(id, button);
                document.Add(button);
            }
            button.Text = action.Label;
            button.Frame = new Rect(action.X, action.Y, action.Label.Length + 4, 1);
        }

        if (selectedId is null || !nextIds.Contains(selectedId))
            selectedId = actions.Count == 0 ? null : actions[Math.Min(previousIndex, actions.Count - 1)].Id;
        if (selectedId is not null) buttons[selectedId].SetFocus();
        SetScrollTop(followSelection ? SelectedScrollTop() : scrollTop);
    }

    public void ShowSizeHint()
    {
        // Retain the document and selection until the terminal is large enough again.
        document.Visible = false;
        Text = "Enlarge terminal (46 columns minimum). q: quit";
        SetNeedsDisplay();
    }

    public bool HandleWheel(MouseFlags flags)
    {
        const MouseFlags wheel = MouseFlags.WheeledUp | MouseFlags.WheeledDown |
            MouseFlags.WheeledLeft | MouseFlags.WheeledRight;
        if ((flags & wheel) == 0) return false;
        if (!document.Visible) return true;
        var delta = (flags & MouseFlags.WheeledUp) != 0 ? -3 :
            (flags & MouseFlags.WheeledDown) != 0 ? 3 : 0;
        if (delta == 0) return true;
        followSelection = false;
        SetScrollTop(scrollTop + delta);
        return true;
    }

    public bool HandleKey(Key key)
    {
        // Disable native tab traversal and document scrolling on the main screen.
        if (key is Key.Tab or (Key.ShiftMask | Key.Tab) or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            return true;
        if (key is not (Key.CursorLeft or Key.CursorRight or Key.CursorUp or Key.CursorDown))
            return !document.Visible && key is (Key.Enter or Key.Space);
        if (!document.Visible || selectedId is null) return true;

        var current = actions.First(action => action.Id == selectedId);
        var candidates = key switch
        {
            Key.CursorLeft => actions.Where(action => action.Y == current.Y && action.X < current.X)
                .OrderByDescending(action => action.X),
            Key.CursorRight => actions.Where(action => action.Y == current.Y && action.X > current.X)
                .OrderBy(action => action.X),
            Key.CursorUp => actions.Where(action => action.Y < current.Y)
                .OrderByDescending(action => action.Y).ThenBy(action => Math.Abs(action.X - current.X)).ThenBy(action => action.X),
            _ => actions.Where(action => action.Y > current.Y)
                .OrderBy(action => action.Y).ThenBy(action => Math.Abs(action.X - current.X)).ThenBy(action => action.X),
        };
        selectedId = (candidates.FirstOrDefault() ?? current).Id;
        followSelection = true;
        buttons[selectedId].SetFocus();
        SetScrollTop(SelectedScrollTop());
        return true;
    }

    int SelectedScrollTop()
    {
        if (selectedId is null) return scrollTop;
        var frame = buttons[selectedId].Frame;
        if (frame.Y < scrollTop) return frame.Y;
        if (frame.Bottom > scrollTop + Bounds.Height) return frame.Bottom - Bounds.Height;
        return scrollTop;
    }

    void SetScrollTop(int value)
    {
        Text = string.Empty;
        scrollTop = Math.Clamp(value, 0, Math.Max(0, contentHeight - Bounds.Height));
        document.Frame = new Rect(0, -scrollTop, Bounds.Width, contentHeight);
        SetNeedsDisplay();
    }
}
