using Kolvir.Observables;
using Kolvir.VirtualDom;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The observable set page: a set of tags, one chip per tag that reads only whether its own tag is in it,
/// and buttons that change the set in the shapes a set offers.
/// </summary>
/// <remarks>
/// <para>
/// The chip board never re-renders on its own: the tags it shows are a constant, and the only read it
/// makes is the one it makes once, when it hands the model to each chip. Every change after that reaches
/// the chips, and only the chips whose own tag moved. Watch the render counts - toggling one tag leaves
/// the others where they were, while <b>Toggle everything</b> moves them all in one event.
/// </para>
/// <para>
/// The other half is the bulk operations. <b>Add two at once</b> is one event with one record per tag
/// that actually arrived, and the copy built from the records says whether it still matches the set.
/// </para>
/// </remarks>
internal sealed class ObservableSetPage : DemoPage
{
    private static readonly string[] Tags = ["rust", "skia", "dotnet", "win32", "gtk"];

    private readonly IObservableSet<string> _tags = Observable.CreateHashSet(
        StringComparer.OrdinalIgnoreCase,
        "rust",
        "skia",
        "dotnet");

    private readonly IReadOnlyObservableSet<string> _readOnly;
    private readonly SummaryLine _summary = new();
    private readonly ChipBoard _board = new();
    private readonly HashSet<string> _mirror = new(StringComparer.OrdinalIgnoreCase);
    private readonly Label _records;
    private int _events;
    private int _serial;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ObservableSetPage(DemoUi shell) : base(shell)
    {
        _readOnly = _tags;
        _mirror.UnionWith(_tags);
        _records = Caption($"No events yet: the copy of {_mirror.Count} tag(s) is what the page was built with.");

        Initialize(Build());
    }

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Observable set"));
        card.Controls.Add(Note(
            "An IObservableSet<T> publishes a dependency per kind of read. A chip asks Contains(its own "
            + "tag) and nothing else, so toggling one tag re-renders that chip - the count line above and "
            + "the other chips keep their render counts. The line under the buttons is a copy built only "
            + "from the records."));

        card.Controls.Add(Actions());

        var summaryHost = Host(22f);
        card.Controls.Add(summaryHost);

        card.Controls.Add(_records);

        var boardHost = Host(0f);
        card.Controls.Add(boardHost);

        _summary.DataModel = _readOnly;
        _board.DataModel = _readOnly;

        VirtualDomRuntime.Mount(summaryHost, _summary);
        VirtualDomRuntime.Mount(boardHost, _board);

        _tags.AddObservableSetChangedHandler(OnChanged);

        return card;
    }

    private static Panel Host(float height) => new()
    {
        Dock = height > 0f ? XDock.Top : XDock.Fill,
        Height = height,
        BackColor = null,
        Border = Border.None,
        Padding = new Thickness(0f),
    };

    /// <summary>The page's own controls: plain buttons that change the set.</summary>
    /// <returns>A row of them, docked to the top of the card.</returns>
    private Panel Actions()
    {
        var row = ButtonRow();

        var toggleRust = Action("Toggle rust");
        toggleRust.AddClickHandler(_ =>
        {
            // Removing when it is there and adding when it is not is one record either way, and wakes one
            // chip either way.
            if (_tags.Remove("rust"))
            {
                SetStatus("rust left: its chip re-rendered, the others did not, and the size changed.");
                return;
            }

            _tags.Add("rust");
            SetStatus("rust arrived: its chip re-rendered and the count line moved.");
        });

        var toggleGtk = Action("Toggle gtk");
        toggleGtk.AddClickHandler(_ =>
        {
            if (_tags.Remove("gtk"))
            {
                SetStatus("gtk left - the same one-chip change, from the other direction.");
                return;
            }

            _tags.Add("gtk");
            SetStatus("gtk arrived.");
        });

        var two = Action("Add two at once");
        two.AddClickHandler(_ =>
        {
            _tags.UnionWith(["win32", "gtk"]);
            SetStatus("One event with one record per tag that actually arrived; the copy applied them in order.");
        });

        var removeTwo = Action("Remove two");
        removeTwo.AddClickHandler(_ =>
        {
            _tags.ExceptWith(["win32", "gtk"]);
            SetStatus("One event, one record per tag that left - and nothing at all if neither was there.");
        });

        var everything = Action("Toggle everything");
        everything.AddClickHandler(_ =>
        {
            _tags.SymmetricExceptWith(Tags);
            SetStatus("One event: every chip moved, and the removals are reported before the additions.");
        });

        var clear = Action("Clear");
        clear.AddClickHandler(_ =>
        {
            _tags.Clear();
            SetStatus("One SetCleared record, however many tags there were.");
        });

        var sixty = Action("Add sixty at once");
        sixty.AddClickHandler(_ =>
        {
            var batch = new List<string>(60);

            for (var index = 0; index < 60; index++)
            {
                batch.Add($"bulk {++_serial}");
            }

            _tags.UnionWith(batch);

            SetStatus(
                $"More than {_tags.MaxCollectionBatchSize} records, so the event says only that things "
                + "changed and the copy rescans the set.");
        });

        row.Controls.Add(toggleRust);
        row.Controls.Add(toggleGtk);
        row.Controls.Add(two);
        row.Controls.Add(removeTwo);
        row.Controls.Add(everything);
        row.Controls.Add(clear);
        row.Controls.Add(sixty);
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
    private void OnChanged(ObservableSetChangedEventArgs<string> args)
    {
        if (args.Changes.Count == 0)
        {
            _records.Text = "The set was disposed.";
            return;
        }

        _events++;

        // The copy is built from the records and nothing else, which is the claim a record stream has to
        // support: no read of the set is involved in applying them.
        foreach (var change in args.Changes)
        {
            switch (change.ChangeType)
            {
                case ObservableSetChangeType.ItemAdded:
                    _mirror.Add(change.Item!);
                    break;

                case ObservableSetChangeType.ItemRemoved:
                    _mirror.Remove(change.Item!);
                    break;

                case ObservableSetChangeType.SetCleared:
                    _mirror.Clear();
                    break;

                default:
                    // UntrackedChanges: the batch was too big to describe, so the copy is rebuilt from the
                    // set - the one record that cannot be applied.
                    _mirror.Clear();
                    _mirror.UnionWith(_tags);
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

        var matches = _mirror.SetEquals(_tags);

        _records.Text = $"Event {_events}: {args.Changes.Count} record(s) - {described} - the copy "
            + (matches ? "matches the set." : "DIFFERS FROM THE SET.");
    }

    /// <summary>A line that reads only the size, so nothing about an item can re-render it.</summary>
    private sealed class SummaryLine : ObservableComponent<IReadOnlyObservableSet<string>>
    {
        private int _renders;

        protected override VNode RenderWithDataModel(RenderArgs<IReadOnlyObservableSet<string>> args)
        {
            _renders++;

            var count = args.DataModel.Count;

            return new VLabel
            {
                Dock = XDock.Fill,
                AutoSize = false,
                Font = Theme.CaptionFont,
                ForeColorRole = ThemeColor.MutedForeground,
                Text = $"{count} of {Tags.Length} tag(s) on - this line has rendered {_renders} time(s), on the size alone",
            };
        }
    }

    /// <summary>
    /// The chips, one per tag. The board reads nothing, so it renders once for the life of the page: every
    /// change after that is the chips' business.
    /// </summary>
    private sealed class ChipBoard : ObservableComponent<IReadOnlyObservableSet<string>>
    {
        private readonly Dictionary<string, TagChip> _chips = new(StringComparer.OrdinalIgnoreCase);

        protected override VNode RenderWithDataModel(RenderArgs<IReadOnlyObservableSet<string>> args)
        {
            var children = new List<VNode>(Tags.Length + 1)
            {
                new VLabel
                {
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 18f,
                    Font = Theme.CaptionFont,
                    ForeColorRole = ThemeColor.MutedForeground,
                    Text = "five chips, each reading only its own tag - the board itself has rendered once",
                    Margin = new Thickness(0f, 2f, 0f, 4f),
                },
            };

            foreach (var tag in Tags)
            {
                if (!_chips.TryGetValue(tag, out var chip))
                {
                    chip = new TagChip();
                    _chips.Add(tag, chip);
                }

                // The model is the parent's to hand down: a child component has no idea where it is
                // mounted, and one with no model renders the empty fragment its base class supplies.
                chip.DataModel = args.DataModel;
                chip.Tag = tag;

                children.Add(new VComponent(chip) { Key = tag });
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

    /// <summary>One chip: it asks whether its own tag is in the set and nothing else.</summary>
    private sealed class TagChip : ObservableComponent<IReadOnlyObservableSet<string>>
    {
        private int _renders;

        internal string Tag { get; set; } = string.Empty;

        protected override VNode RenderWithDataModel(RenderArgs<IReadOnlyObservableSet<string>> args)
        {
            _renders++;

            // The one read: this tag. Not the size, and not any other chip's answer.
            var on = args.DataModel.Contains(Tag);

            return new VLabel
            {
                Dock = XDock.Top,
                AutoSize = false,
                Height = 22f,
                Font = Theme.DefaultFont,
                ForeColorRole = on ? ThemeColor.Foreground : ThemeColor.MutedForeground,
                Text = $"{(on ? "on " : "off")}  {Tag}    (this chip has rendered {_renders} time(s))",
                Margin = new Thickness(0f, 0f, 0f, 2f),
            };
        }
    }
}
