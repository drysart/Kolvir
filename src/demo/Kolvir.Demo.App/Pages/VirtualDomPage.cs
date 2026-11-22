using Kolvir.VirtualDom;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The virtual DOM page: a view model that renders a description of its controls, a reconciler that
/// turns the description into the control tree under the card, and child components of its own.
/// </summary>
/// <remarks>
/// <para>
/// Everything else on this page is ordinary Kolvir - a control built once, with handlers that mutate
/// it. This card is the other arrangement: the page mounts a component, and the component's
/// <see cref="Component.Render"/> describes what should be on screen. The page itself never touches the
/// controls inside the card, and the component never holds one.
/// </para>
/// <para>
/// The rows are components too. The list decides that a row is there and where it sits; the row decides
/// what it looks like and marks itself when someone types in it. So the two render counts on the card
/// move independently: typing in a row raises that row's count and leaves the list's alone, and the
/// list's count rises for an add, a move or a remove without any row rendering. A moved row's controls
/// are <em>moved</em> rather than rebuilt, which is why the caret stays in the box that moved.
/// </para>
/// <para>
/// Nothing on the card renders when it is clicked. Every handler changes state and calls
/// <see cref="Component.SetState"/>, which only marks the component, and the frame that follows renders
/// everything marked - once each. Adding a row, for instance, changes the list <em>and</em> the
/// two rows whose arrows no longer apply, and the card shows all of it after one render of each rather
/// than after five.
/// </para>
/// </remarks>
internal sealed class VirtualDomPage : DemoPage
{
    private readonly RowListCard _card = new();

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public VirtualDomPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Virtual DOM"));

        card.Controls.Add(Note(
            "The card below is a view model: its Render() returns a description of the controls it "
            + "wants, and the reconciler diffs that into the tree underneath. The rows are components "
            + "as well, each with its own state and mount: type in a row and only that row renders - and "
            + "nothing renders at all until the next frame is drawn."));

        var stage = new Panel
        {
            Dock = XDock.Fill,
            Border = Border.None,
            Padding = new Thickness(0f),
        };

        card.Controls.Add(stage);

        // The mount is what a window owns: it renders the component's first tree and stays attached so
        // that a SetState has something to patch. The page never renders again after this line, and it
        // does not keep the handle, because the component holds the mount it renders through.
        VirtualDomRuntime.Mount(stage, _card);

        return card;
    }

    /// <summary>
    /// A component whose state is a list of rows: it decides which rows exist and in what order, and
    /// none of it is a row's own state.
    /// </summary>
    private sealed class RowListCard : Component
    {
        private readonly List<RowCard> _rows = [];
        private int _nextId = 1;
        private int _renders;

        internal RowListCard()
        {
            // Three rows to start with, so the page says something before anyone clicks. Their
            // SetState calls find no mount yet, which is a no-op: the first render reads the state.
            for (int i = 0; i < 3; i++)
            {
                _rows.Add(new RowCard(this, _nextId++));
            }

            SyncPositions();
        }

        public override VNode Render()
        {
            _renders++;

            return new VPanel
            {
                Dock = XDock.Fill,
                Border = Border.None,
                Padding = new Thickness(0f),
                Children =
                [
                    new VButton
                    {
                        Dock = XDock.Top,
                        AutoSize = true,
                        Text = "Add a row",
                        Style = ButtonStyle.Primary,
                        Margin = new Thickness(0f, 0f, 0f, 8f),
                        Click = _ => Add(),
                    },
                    new VLabel
                    {
                        Dock = XDock.Top,
                        AutoSize = false,
                        Height = 20f,
                        Font = Theme.CaptionFont,
                        ForeColor = ThemeColor.MutedForeground,
                        Text = $"{_rows.Count} row(s) - {_renders} render(s) of the list",
                        Margin = new Thickness(0f, 0f, 0f, 8f),
                    },

                    // A component is a node like any other, so the list's children are its rows. The
                    // key is the row itself: that is what lets the reconciler move a row's controls
                    // instead of rebuilding them when the order changes.
                    new VPanel
                    {
                        Dock = XDock.Fill,
                        Border = Border.None,
                        Padding = new Thickness(0f),
                        Children = [.. _rows.Select(row => new VComponent(row) { Key = row })],
                    },
                ],
            };
        }

        private void Add()
        {
            _rows.Add(new RowCard(this, _nextId++));
            SyncPositions();
            SetState();
        }

        internal void Remove(RowCard row)
        {
            if (!_rows.Remove(row))
            {
                return;
            }

            SyncPositions();
            SetState();
        }

        internal void Move(RowCard row, int by)
        {
            var from = _rows.IndexOf(row);
            var to = from + by;

            if (from < 0 || to < 0 || to >= _rows.Count)
            {
                return;
            }

            _rows.RemoveAt(from);
            _rows.Insert(to, row);
            SyncPositions();
            SetState();
        }

        /// <summary>
        /// Tells each row where it now sits, which re-renders only the rows whose answer changed.
        /// </summary>
        /// <remarks>
        /// The parent owns the order, so it is the parent that tells a row it has become the first or
        /// the last. A row answers with its own <see cref="Component.SetState"/>, so the edge rows
        /// disable their own arrows without the list or its other rows rendering for it.
        /// </remarks>
        private void SyncPositions()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].SetPosition(canMoveUp: i > 0, canMoveDown: i < _rows.Count - 1);
            }
        }
    }

    /// <summary>
    /// One row: its own text, its own three buttons, its own render count. It asks the list to move or
    /// remove it, because the order of the list is the list's state and not the row's.
    /// </summary>
    private sealed class RowCard : Component
    {
        private readonly RowListCard _list;
        private bool _canMoveUp;
        private bool _canMoveDown;

        internal RowCard(RowListCard list, int id)
        {
            _list = list;
            Id = id;
            Text = $"Row {id}";
        }

        internal int Id { get; }

        /// <summary>The row's string, which is the only state a click or a keystroke changes.</summary>
        internal string Text { get; private set; }

        internal int Renders { get; private set; }

        public override VNode Render()
        {
            Renders++;

            return new VPanel
            {
                Dock = XDock.Top,
                Height = 30f,
                Border = Border.None,
                Padding = new Thickness(0f),
                Margin = new Thickness(0f, 0f, 0f, 6f),
                Children =
                [
                    // Docked right to left in this order, so the readout is outermost and the three
                    // buttons sit between it and the box. The box is the only Fill child.
                    new VLabel
                    {
                        Dock = XDock.Right,
                        AutoSize = false,
                        Width = 104f,
                        Font = Theme.CaptionFont,
                        ForeColor = ThemeColor.MutedForeground,
                        TextAlign = ContentAlignment.MiddleRight,
                        Text = $"#{Id} - {Renders} render(s)",
                    },
                    RowButton("Remove", () => _list.Remove(this), enabled: true),
                    RowButton("Down", () => _list.Move(this, +1), _canMoveDown),
                    RowButton("Up", () => _list.Move(this, -1), _canMoveUp),

                    // The row's state as the user sees and edits it. Echoing the box back into the node
                    // is safe: assigning the text a box already holds is not a change, so this does not
                    // fight the caret and does not re-enter the handler.
                    new VTextBox
                    {
                        Dock = XDock.Fill,
                        Text = Text,
                        Margin = new Thickness(0f, 0f, 8f, 0f),
                        TextChanged = sender => Edit(sender),
                    },
                ],
            };
        }

        /// <summary>Takes the position the list reports, re-rendering only if it changed.</summary>
        /// <param name="canMoveUp">Whether there is a row above this one.</param>
        /// <param name="canMoveDown">Whether there is a row below this one.</param>
        internal void SetPosition(bool canMoveUp, bool canMoveDown)
        {
            if (_canMoveUp == canMoveUp && _canMoveDown == canMoveDown)
            {
                return;
            }

            _canMoveUp = canMoveUp;
            _canMoveDown = canMoveDown;
            SetState();
        }

        private void Edit(object sender)
        {
            if (sender is not TextBox box || string.Equals(box.Text, Text, StringComparison.Ordinal))
            {
                return;
            }

            Text = box.Text;
            SetState();
        }

        private static VButton RowButton(string text, Action pressed, bool enabled) => new()
        {
            Dock = XDock.Right,
            AutoSize = true,
            Enabled = enabled,
            Text = text,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(4f, 0f, 0f, 0f),
            Click = _ => pressed(),
        };
    }
}
