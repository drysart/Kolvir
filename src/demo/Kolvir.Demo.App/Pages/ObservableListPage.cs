using Kolvir.Observables;
using Kolvir.VirtualDom;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The observable list page: a list whose changes are reported as records, three components that read
/// different things from it, and buttons that change it.
/// </summary>
/// <remarks>
/// <para>
/// The Observables page shows a component re-rendering when the values its render read change. This is
/// the collection version, and the point of it is the <em>granularity</em>: a read of <c>Count</c> and a
/// read of <c>list[3]</c> are different dependencies, so the line above the list keeps its render count
/// when a row is renamed, and only the row that was renamed re-renders. Watch the three render counts
/// while pressing the buttons.
/// </para>
/// <para>
/// The other half is fidelity. The page keeps a copy of the list that it builds <em>only</em> from the
/// change records - it never reads the list to update it - and the line under the buttons says whether
/// that copy still equals the list. <b>Add five in a batch</b> is the interesting one: five records, one
/// event, and the copy still matches.
/// </para>
/// </remarks>
internal sealed class ObservableListPage : DemoPage
{
    private readonly IObservableList<string> _tasks = Observable.CreateList(
        "Write the design",
        "Implement the list",
        "Render it once");

    private readonly IReadOnlyObservableList<string> _readOnly;
    private readonly SummaryLine _summary = new();
    private readonly RowsCard _rows = new();
    private readonly List<string> _mirror = [];
    private readonly Label _records;
    private int _events;
    private int _serial;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ObservableListPage(DemoUi shell) : base(shell)
    {
        _readOnly = _tasks;
        _mirror.AddRange(_tasks);
        _records = Caption(
            $"No events yet: the copy of {_mirror.Count} item(s) is the list the page was built with.");

        Initialize(Build());
    }

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Observable list"));
        card.Controls.Add(Note(
            "An IObservableList<T> reports every change as a record an observer can replay, and it "
            + "publishes a dependency per kind of read. Rename a row and only that row re-renders - the "
            + "count line above does not, because its render reads Count and the size did not change. "
            + "The line under the buttons is a copy built only from the records, which is how a change "
            + "stream proves it is complete."));

        card.Controls.Add(Actions());

        // Mounted before the model is read: the first render of each component reads the list and
        // subscribes, so a change afterwards is what asks for the re-render.
        var summaryHost = Host(22f);
        card.Controls.Add(summaryHost);

        card.Controls.Add(_records);

        var rowsHost = Host(0f);
        card.Controls.Add(rowsHost);

        _summary.DataModel = _readOnly;
        _rows.DataModel = _readOnly;

        VirtualDomRuntime.Mount(summaryHost, _summary);
        VirtualDomRuntime.Mount(rowsHost, _rows);

        _tasks.AddObservableListChangedHandler(OnChanged);

        return card;
    }

    /// <summary>A docked panel with no chrome, used to give a mounted component a place in the card.</summary>
    /// <param name="height">Its height, or zero for the rest of the card.</param>
    /// <returns>The panel to mount into.</returns>
    private static Panel Host(float height) => new()
    {
        Dock = height > 0f ? XDock.Top : XDock.Fill,
        Height = height,
        BackColor = null,
        Border = Border.None,
        Padding = new Thickness(0f),
    };

    /// <summary>The page's own controls: plain buttons that change the list.</summary>
    /// <returns>A row of them, docked to the top of the card.</returns>
    private Panel Actions()
    {
        var row = ButtonRow();

        var add = Action("Add");
        add.AddClickHandler(_ =>
        {
            _serial++;
            _tasks.Add($"Task {_serial}");
            SetStatus($"Added - the list is now {_tasks.Count} item(s).");
        });

        var insert = Action("Insert first");
        insert.AddClickHandler(_ =>
        {
            _serial++;
            _tasks.Insert(0, $"Urgent {_serial}");
            SetStatus("Inserted at 0: every row at or past it re-rendered, and the count line did too.");
        });

        var rename = Action("Rename row 1");
        rename.AddClickHandler(_ =>
        {
            if (_tasks.Count == 0)
            {
                SetStatus("There is no row 1 to rename.");
                return;
            }

            _serial++;
            _tasks[0] = $"Renamed {_serial}";
            SetStatus("One row changed: only that row re-rendered, and the count line did not.");
        });

        var move = Action("Move first to last");
        move.AddClickHandler(_ =>
        {
            if (_tasks.Count < 2)
            {
                SetStatus("A move needs two items.");
                return;
            }

            _tasks.Move(0, _tasks.Count - 1);
            SetStatus("One ItemMoved record: the rows it crossed re-rendered, the size did not change.");
        });

        var remove = Action("Remove last");
        remove.AddClickHandler(_ =>
        {
            if (_tasks.Count == 0)
            {
                SetStatus("The list is already empty.");
                return;
            }

            _tasks.RemoveAt(_tasks.Count - 1);
            SetStatus($"Removed the last item - {_tasks.Count} left.");
        });

        var clear = Action("Clear");
        clear.AddClickHandler(_ =>
        {
            _tasks.Clear();
            SetStatus("One ListCleared record, however many items there were.");
        });

        var batch = Action("Add five in a batch");
        batch.AddClickHandler(_ =>
        {
            _tasks.BeginChangeBatch();
            for (var index = 0; index < 5; index++)
            {
                _serial++;
                _tasks.Add($"Batched {_serial}");
            }

            _tasks.EndChangeBatch();
            SetStatus("Five changes, one notification: read the record count on the line above.");
        });

        var tooMany = Action("Add sixty in a batch");
        tooMany.AddClickHandler(_ =>
        {
            _tasks.BeginChangeBatch();

            for (var index = 0; index < 60; index++)
            {
                _serial++;
                _tasks.Add($"Bulk {_serial}");
            }

            _tasks.EndChangeBatch();
            SetStatus(
                $"Sixty changes in one batch, which is more than {Observable.Default.MaxCollectionBatchSize} records - so "
                + "the event says only that things changed, and the copy rescans the list.");
        });

        row.Controls.Add(add);
        row.Controls.Add(insert);
        row.Controls.Add(rename);
        row.Controls.Add(move);
        row.Controls.Add(remove);
        row.Controls.Add(clear);
        row.Controls.Add(batch);
        row.Controls.Add(tooMany);
        return row;
    }

    private static Button Action(string text) => new(text)
    {
        Dock = XDock.Left,
        AutoSize = true,
        Style = ButtonStyle.Secondary,
        Margin = new Thickness(0f, 0f, 6f, 0f),
    };

    /// <summary>Updates the copy and the line that says whether it still matches.</summary>
    /// <param name="args">The event: one record outside a batch, and everything the batch collected
    /// inside one.</param>
    private void OnChanged(ObservableListChangedEventArgs<string> args)
    {
        if (args.Changes.Count == 0)
        {
            // The final event a disposal delivers: the list is dead, so the copy cannot be compared with
            // it any more - every read of it throws now.
            _records.Text = "The list was disposed.";
            return;
        }

        _events++;

        // The copy is built from the records and nothing else: no read of the list is involved in
        // applying them, which is the whole claim a record stream has to support.
        foreach (var change in args.Changes)
        {
            switch (change.ChangeType)
            {
                case ObservableListChangeType.ItemAdded:
                    _mirror.Insert(change.NewIndex!.Value, change.NewItem!);
                    break;

                case ObservableListChangeType.ItemRemoved:
                    _mirror.RemoveAt(change.OldIndex!.Value);
                    break;

                case ObservableListChangeType.ItemMoved:
                    _mirror.RemoveAt(change.OldIndex!.Value);
                    _mirror.Insert(change.NewIndex!.Value, change.NewItem!);
                    break;

                case ObservableListChangeType.ItemChanged:
                    _mirror[change.NewIndex!.Value] = change.NewItem!;
                    break;

                case ObservableListChangeType.ListCleared:
                    _mirror.Clear();
                    break;

                default:
                    // UntrackedChanges: the batch was too big to describe, so the copy cannot be patched
                    // and is rebuilt from the list - the one record an observer cannot replay.
                    _mirror.Clear();
                    _mirror.AddRange(_tasks);
                    break;
            }
        }

        var described = args.Changes.Count switch
        {
            0 => "nothing",
            1 or 2 or 3 => string.Join("; ", args.Changes.Select(change => change.ToString())),
            _ => string.Join("; ", args.Changes.Take(3).Select(change => change.ToString()))
                + $"; and {args.Changes.Count - 3} more",
        };

        var matches = _mirror.SequenceEqual(_tasks, StringComparer.Ordinal);

        _records.Text = $"Event {_events}: {args.Changes.Count} record(s) - {described} - the copy "
            + (matches ? "matches the list." : "DIFFERS FROM THE LIST.");
    }

    /// <summary>A line that reads only the size, so nothing about an item can re-render it.</summary>
    private sealed class SummaryLine : ObservableComponent<IReadOnlyObservableList<string>>
    {
        private int _renders;

        protected override VNode RenderWithDataModel(
            RenderArgs<IReadOnlyObservableList<string>> args)
        {
            _renders++;

            // The only read on this component, and the reason it keeps its render count while a row is
            // renamed: an item write does not change the size, so it does not wake this.
            var count = args.DataModel.Count;

            return new VLabel
            {
                Dock = XDock.Fill,
                AutoSize = false,
                Font = Theme.CaptionFont,
                ForeColor = ThemeColor.MutedForeground,
                Text = $"{count} item(s) - this line has rendered {_renders} time(s), on the size alone",
            };
        }
    }

    /// <summary>The rows: one child component per index, each reading only its own index.</summary>
    private sealed class RowsCard : ObservableComponent<IReadOnlyObservableList<string>>
    {
        private readonly Dictionary<int, RowCard> _rowCards = [];
        private int _renders;

        protected override VNode RenderWithDataModel(
            RenderArgs<IReadOnlyObservableList<string>> args)
        {
            _renders++;

            var tasks = args.DataModel;
            var count = tasks.Count;

            // A child that a shorter list removed is disposed by the reconciler, and re-mounting a
            // disposed component is refused - so the slots past the end let go of theirs here.
            foreach (var index in _rowCards.Keys.Where(index => index >= count).ToArray())
            {
                _rowCards.Remove(index);
            }

            var children = new List<VNode>(count + 1)
            {
                new VLabel
                {
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 18f,
                    Font = Theme.CaptionFont,
                    ForeColor = ThemeColor.MutedForeground,
                    Text = $"the list itself has rendered {_renders} time(s), on the size alone",
                    Margin = new Thickness(0f, 2f, 0f, 4f),
                },
            };

            for (var index = 0; index < count; index++)
            {
                if (!_rowCards.TryGetValue(index, out var rowCard))
                {
                    rowCard = new RowCard();
                    _rowCards.Add(index, rowCard);
                }

                // The model is the parent's to hand down: a child component has no idea where it is
                // mounted, and one with no model renders the empty fragment its base class supplies.
                rowCard.DataModel = tasks;
                rowCard.Index = index;

                // Keyed by the index, which is also what the child reads: the parent owns the place and
                // the child owns its content, so a rerender here leaves every existing row alone.
                children.Add(new VComponent(rowCard) { Key = index });
            }

            return new VPanel
            {
                Dock = XDock.Fill,
                BackColor = null,
                Border = Border.None,
                Padding = new Thickness(0f),
                Children = children,
            };
        }
    }

    /// <summary>One row: it reads that index and nothing else, so it is the only thing a write wakes.</summary>
    private sealed class RowCard : ObservableComponent<IReadOnlyObservableList<string>>
    {
        private int _renders;

        internal int Index { get; set; }

        protected override VNode RenderWithDataModel(
            RenderArgs<IReadOnlyObservableList<string>> args)
        {
            _renders++;

            // The one read: this index. Not the size, and not any other row's item.
            var text = args.DataModel[Index];

            return new VLabel
            {
                Dock = XDock.Top,
                AutoSize = false,
                Height = 22f,
                Font = Theme.DefaultFont,
                Text = $"{Index + 1}. {text}    (this row has rendered {_renders} time(s))",
                Margin = new Thickness(0f, 0f, 0f, 2f),
            };
        }
    }
}
