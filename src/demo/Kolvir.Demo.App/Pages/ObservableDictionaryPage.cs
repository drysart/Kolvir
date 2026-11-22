using Kolvir.Observables;
using Kolvir.VirtualDom;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The observable dictionary page: a dictionary of settings, a key list that reads only the structure, one
/// row per key that reads only its own key, and buttons that change values and keys differently.
/// </summary>
/// <remarks>
/// <para>
/// The point of the page is the difference between the three reads. <b>Edit theme</b> changes a value
/// beside a key that stayed: one row re-renders, the key list does not, and the size line does not.
/// <b>Add a key</b> changes the structure: the key list re-renders and a new row appears. Watch the two
/// render counts to see which of the two each button moves.
/// </para>
/// <para>
/// The row reads with <c>TryGetValue</c> rather than the indexer, which is also the difference worth
/// knowing: a row whose key a change removed answers "not there" instead of throwing, and a reader that
/// asked about a key before it arrived is woken when it does.
/// </para>
/// </remarks>
internal sealed class ObservableDictionaryPage : DemoPage
{
    private readonly IObservableDictionary<string, string> _settings = Observable.CreateDictionary(
        StringComparer.OrdinalIgnoreCase,
        new Dictionary<string, string>
        {
            ["theme"] = "dark",
            ["font"] = "Segoe UI",
            ["scale"] = "1.0",
        });

    private readonly IReadOnlyObservableDictionary<string, string> _readOnly;
    private readonly StructureLine _structure = new();
    private readonly KeyList _list = new();
    private readonly Dictionary<string, string> _mirror = new(StringComparer.OrdinalIgnoreCase);
    private readonly Label _records;
    private int _events;
    private int _serial;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ObservableDictionaryPage(DemoUi shell) : base(shell)
    {
        _readOnly = _settings;

        foreach (var pair in _settings)
        {
            _mirror[pair.Key] = pair.Value;
        }

        _records = Caption($"No events yet: the copy of {_mirror.Count} key(s) is what the page was built with.");

        Initialize(Build());
    }

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Observable dictionary"));
        card.Controls.Add(Note(
            "An IObservableDictionary<TKey, TValue> publishes three reads: the structure (Count and Keys), "
            + "one key, and the contents (Values and everything that reads every value). Edit a value and "
            + "only its row re-renders; add or remove a key and the key list moves too. The line under the "
            + "buttons is a copy built only from the records."));

        foreach (var row in Actions())
        {
            card.Controls.Add(row);
        }

        var structureHost = Host(22f);
        card.Controls.Add(structureHost);

        card.Controls.Add(_records);

        var listHost = Host(0f);
        card.Controls.Add(listHost);

        _structure.DataModel = _readOnly;
        _list.DataModel = _readOnly;

        VirtualDomRuntime.Mount(structureHost, _structure);
        VirtualDomRuntime.Mount(listHost, _list);

        _settings.AddObservableDictionaryChangedHandler(OnChanged);

        return card;
    }

    private static Panel Host(float height) => new()
    {
        Dock = height > 0f ? XDock.Top : XDock.Fill,
        Height = height,
        Border = Border.None,
        Padding = new Thickness(0f),
    };

    /// <summary>The page's own controls: plain buttons that change the dictionary.</summary>
    /// <returns>Three rows of them - the value edits, the key edits, and the wholesale ones - each docked
    /// to the top of the card, so a narrow window stacks them instead of clipping them.</returns>
    private Panel[] Actions()
    {
        var values = ButtonRow();
        var keys = ButtonRow();
        var wholesale = ButtonRow();

        var edit = Action("Edit theme");
        edit.AddClickHandler(_ =>
        {
            _settings["theme"] = $"dark {++_serial}";
            SetStatus("One ItemChanged record: the theme row re-rendered, the key list and the size did not.");
        });

        var editAll = Action("Edit every value");
        editAll.AddClickHandler(_ =>
        {
            // One batch, so one event with a record per key - every row re-renders once, the key list not
            // at all.
            _settings.BeginChangeBatch();

            foreach (var key in _settings.Keys.ToArray())
            {
                _settings[key] = $"edited {++_serial}";
            }

            _settings.EndChangeBatch();
            SetStatus("One event with one record per key: every row moved, and the structure did not.");
        });

        var add = Action("Add a key");
        add.AddClickHandler(_ =>
        {
            _settings[$"extra {++_serial}"] = "new";
            SetStatus("ItemAdded: the key list re-rendered for the new row, and the size moved with it.");
        });

        var remove = Action("Remove last extra");
        remove.AddClickHandler(_ =>
        {
            var extra = _settings.Keys.FirstOrDefault(key => key.StartsWith("extra ", StringComparison.OrdinalIgnoreCase));

            if (extra is null)
            {
                SetStatus("There is no extra key to remove - add one first.");
                return;
            }

            _settings.Remove(extra);
            SetStatus($"ItemRemoved: {extra} left, and the record carries the value it had.");
        });

        var tryAdd = Action("TryAdd theme");
        tryAdd.AddClickHandler(_ =>
        {
            // The add-if-absent the interface has and IDictionary does not: false, and nothing raised,
            // because nothing changed.
            var added = _settings.TryAdd("theme", "ignored");
            SetStatus(added
                ? "The key was absent, so it was added."
                : "TryAdd returned false: the key is already there, nothing changed and no event was raised.");
        });

        var clear = Action("Clear");
        clear.AddClickHandler(_ =>
        {
            _settings.Clear();
            SetStatus("One DictionaryCleared record, however many keys there were.");
        });

        var sixty = Action("Add sixty keys");
        sixty.AddClickHandler(_ =>
        {
            _settings.BeginChangeBatch();

            for (var index = 0; index < 60; index++)
            {
                _settings[$"bulk {++_serial}"] = "value";
            }

            _settings.EndChangeBatch();
            SetStatus(
                $"More than {_settings.MaxCollectionBatchSize} records, so the event says only that things "
                + "changed and the copy rescans the dictionary.");
        });

        // One row per kind of change, so a narrow window stacks the buttons instead of clipping them.
        values.Controls.Add(edit);
        values.Controls.Add(editAll);

        keys.Controls.Add(add);
        keys.Controls.Add(remove);
        keys.Controls.Add(tryAdd);

        wholesale.Controls.Add(clear);
        wholesale.Controls.Add(sixty);

        return [values, keys, wholesale];
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
    private void OnChanged(ObservableDictionaryChangedEventArgs<string, string> args)
    {
        if (args.Changes.Count == 0)
        {
            _records.Text = "The dictionary was disposed.";
            return;
        }

        _events++;

        // The copy is built from the records and nothing else, which is the claim a record stream has to
        // support: no read of the dictionary is involved in applying them.
        foreach (var change in args.Changes)
        {
            switch (change.ChangeType)
            {
                case ObservableDictionaryChangeType.ItemAdded:
                case ObservableDictionaryChangeType.ItemChanged:
                    _mirror[change.Key!] = change.NewValue!;
                    break;

                case ObservableDictionaryChangeType.ItemRemoved:
                    _mirror.Remove(change.Key!);
                    break;

                case ObservableDictionaryChangeType.DictionaryCleared:
                    _mirror.Clear();
                    break;

                default:
                    // UntrackedChanges: the batch was too big to describe, so the copy is rebuilt from the
                    // dictionary - the one record that cannot be applied.
                    _mirror.Clear();

                    foreach (var pair in _settings)
                    {
                        _mirror[pair.Key] = pair.Value;
                    }

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

        var matches = _mirror.Count == _settings.Count
            && _settings.All(pair => _mirror.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));

        _records.Text = $"Event {_events}: {args.Changes.Count} record(s) - {described} - the copy "
            + (matches ? "matches the dictionary." : "DIFFERS FROM THE DICTIONARY.");
    }

    /// <summary>A line that reads only the size, so a value changing cannot re-render it.</summary>
    private sealed class StructureLine : ObservableComponent<IReadOnlyObservableDictionary<string, string>>
    {
        private int _renders;

        protected override VNode RenderWithDataModel(RenderArgs<IReadOnlyObservableDictionary<string, string>> args)
        {
            _renders++;

            var count = args.DataModel.Count;

            return new VLabel
            {
                Dock = XDock.Fill,
                AutoSize = false,
                Font = Theme.CaptionFont,
                ForeColor = ThemeColor.MutedForeground,
                Text = $"{count} key(s) - this line has rendered {_renders} time(s), on the structure alone",
            };
        }
    }

    /// <summary>
    /// The rows: it reads <see cref="IReadOnlyObservableDictionary{TKey, TValue}.Keys"/>, which is the
    /// structure, so a value changing does not re-render it - while an add or a removal does.
    /// </summary>
    private sealed class KeyList : ObservableComponent<IReadOnlyObservableDictionary<string, string>>
    {
        private readonly Dictionary<string, ValueRow> _rows = new(StringComparer.OrdinalIgnoreCase);
        private int _renders;

        protected override VNode RenderWithDataModel(RenderArgs<IReadOnlyObservableDictionary<string, string>> args)
        {
            _renders++;

            var settings = args.DataModel;
            var keys = settings.Keys.ToArray();

            // A row whose key a change removed is disposed by the reconciler, and re-mounting a disposed
            // component is refused - so the slots for keys that are gone let go of theirs here.
            foreach (var key in _rows.Keys.Except(keys, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                _rows.Remove(key);
            }

            var children = new List<VNode>(keys.Length + 1)
            {
                new VLabel
                {
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 18f,
                    Font = Theme.CaptionFont,
                    ForeColor = ThemeColor.MutedForeground,
                    Text = $"the key list has rendered {_renders} time(s), on the structure alone",
                    Margin = new Thickness(0f, 2f, 0f, 4f),
                },
            };

            foreach (var key in keys)
            {
                if (!_rows.TryGetValue(key, out var row))
                {
                    row = new ValueRow();
                    _rows.Add(key, row);
                }

                // The model is the parent's to hand down, and setting the same instance again is not a
                // change: the row listens to the key it reads, not to the parent.
                row.DataModel = settings;
                row.Key = key;

                children.Add(new VComponent(row) { Key = key });
            }

            return new VPanel
            {
                Dock = XDock.Fill,
                Border = Border.None,
                Padding = new Thickness(0f),
                Children = children,
            };
        }
    }

    /// <summary>One row: it reads its own key and nothing else.</summary>
    private sealed class ValueRow : ObservableComponent<IReadOnlyObservableDictionary<string, string>>
    {
        private int _renders;

        internal string Key { get; set; } = string.Empty;

        protected override VNode RenderWithDataModel(RenderArgs<IReadOnlyObservableDictionary<string, string>> args)
        {
            _renders++;

            // The one read: this key. TryGetValue publishes whether or not the key is there, so a row whose
            // key was removed answers rather than throwing.
            var present = args.DataModel.TryGetValue(Key, out var value);

            return new VLabel
            {
                Dock = XDock.Top,
                AutoSize = false,
                Height = 22f,
                Font = Theme.DefaultFont,
                ForeColor = present ? ThemeColor.Foreground : ThemeColor.MutedForeground,
                Text = $"{Key} = {(present ? value : "(removed)")}    (this row has rendered {_renders} time(s))",
                Margin = new Thickness(0f, 0f, 0f, 2f),
            };
        }
    }
}
