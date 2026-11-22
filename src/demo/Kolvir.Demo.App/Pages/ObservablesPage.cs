using Kolvir.Observables;
using Kolvir.VirtualDom;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The observables page: a component whose render reads observable model state, and a row of ordinary
/// controls that change that model without telling the component anything.
/// </summary>
/// <remarks>
/// <para>
/// The Virtual DOM page shows a component that is <em>told</em> when to render - every handler calls
/// <see cref="Component.SetState"/>. This is the other arrangement, which is what the observables
/// package adds: the render reads a model, the read subscribes, and a change inside the model asks for
/// a render by itself. Nothing in the card knows the buttons above it exist, and nothing in the buttons
/// knows a component is listening.
/// </para>
/// <para>
/// What to watch is the card's render count. It moves once per frame however many values changed before
/// it, so <b>Change all three</b> - one handler writing three values - renders the card once. Typing in
/// the card's own box changes the same model and re-renders the card for the same reason, with the caret
/// staying where it is: the node writing back the text the box already holds is not a change.
/// </para>
/// </remarks>
internal sealed class ObservablesPage : DemoPage
{
    private readonly ReleaseModel _model = new();
    private readonly ReleaseCard _card = new();

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ObservablesPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Observables"));

        card.Controls.Add(Note(
            "The card below is an ObservableComponent: its render reads the model, the reads subscribe "
            + "it, and a change inside the model asks for a render on the next frame. The buttons here "
            + "are ordinary controls that know nothing about it - they change the model and the card "
            + "follows."));

        card.Controls.Add(Actions());

        var stage = new Panel
        {
            Dock = XDock.Fill,
            BackColor = null,
            Border = Border.None,
            Padding = new Thickness(0f),
        };

        card.Controls.Add(stage);

        // The model is set before the mount, so the first render already reads it. A component with no
        // model has nothing to describe, and this card has no null-model render to fall back on.
        _card.DataModel = _model;
        VirtualDomRuntime.Mount(stage, _card);

        return card;
    }

    /// <summary>The page's own controls: plain buttons that write to the model the card renders.</summary>
    /// <returns>A row of them, docked to the top of the card.</returns>
    private Panel Actions()
    {
        var row = ButtonRow();

        var rename = Action("Rename");
        var counter = 0;
        rename.AddClickHandler(_ =>
        {
            counter++;
            _model.Name = $"Kolvir {1 + (counter % 3)}.0";
            SetStatus($"The model's name is now {_model.Name}.");
        });

        var note = Action("Add a note");
        note.AddClickHandler(_ =>
        {
            _model.Notes++;
            SetStatus($"{_model.Notes} note(s). The card re-rendered on its own.");
        });

        var urgent = Action("Toggle urgent");
        urgent.AddClickHandler(_ =>
        {
            _model.Urgent = !_model.Urgent;
            SetStatus(_model.Urgent ? "Marked urgent." : "No longer urgent.");
        });

        var all = Action("Change all three");
        all.AddClickHandler(_ =>
        {
            // Three writes, three raises, three invalidations of the same component in one handler -
            // and one render, because the frame is the deadline and the component is marked once.
            _model.Name = $"Kolvir {counter % 3}.0-beta";
            _model.Notes++;
            _model.Urgent = !_model.Urgent;
            SetStatus("Three values changed: the card still renders once.");
        });

        row.Controls.Add(rename);
        row.Controls.Add(note);
        row.Controls.Add(urgent);
        row.Controls.Add(all);
        return row;
    }

    private static Button Action(string text)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };

        return button;
    }

    /// <summary>The state the card renders: three observable values and nothing else.</summary>
    /// <remarks>
    /// Ordinary properties over observable backing fields, the shape the observables package is for: the
    /// card reads <c>Name</c> and is tracked, and the page writes <c>Name</c> and is not.
    /// </remarks>
    private sealed class ReleaseModel
    {
        private readonly IObservableMutableValue<string> _name = Observable.CreateValue("Kolvir 1.0");
        private readonly IObservableMutableValue<int> _notes = Observable.CreateValue(3);
        private readonly IObservableMutableValue<bool> _urgent = Observable.CreateValue(false);

        internal string Name
        {
            get => _name.Value;
            set => _name.Value = value;
        }

        internal int Notes
        {
            get => _notes.Value;
            set => _notes.Value = value;
        }

        internal bool Urgent
        {
            get => _urgent.Value;
            set => _urgent.Value = value;
        }
    }

    /// <summary>The card: a render that is a function of the model it reads, and nothing that marks it.</summary>
    private sealed class ReleaseCard : ObservableComponent<ReleaseModel>
    {
        private int _renders;

        protected override VNode RenderWithDataModel(RenderArgs<ReleaseModel> args)
        {
            _renders++;

            var model = args.DataModel;

            return new VPanel
            {
                Dock = XDock.Fill,
                BackColor = null,
                Border = Border.None,
                Padding = new Thickness(0f),
                Children =
                [
                    new VLabel
                    {
                        Dock = XDock.Top,
                        AutoSize = true,
                        Font = Theme.EmphasizedFont,
                        Text = model.Name,
                        Margin = new Thickness(0f, 0f, 0f, 6f),
                    },
                    new VLabel
                    {
                        Dock = XDock.Top,
                        AutoSize = false,
                        Height = 20f,
                        Font = Theme.CaptionFont,
                        ForeColor = ThemeColor.MutedForeground,
                        Text = $"{model.Notes} note(s)"
                            + (model.Urgent ? " - urgent" : string.Empty)
                            + $" - {_renders} render(s) of the card",
                        Margin = new Thickness(0f, 0f, 0f, 8f),
                    },

                    // The box gets a row of its own: a Fill child of a Fill panel would be the whole card.
                    new VPanel
                    {
                        Dock = XDock.Top,
                        Height = 30f,
                        BackColor = null,
                        Border = Border.None,
                        Padding = new Thickness(0f),
                        Margin = new Thickness(0f, 0f, 0f, 10f),
                        Children =
                        [

                            // The one control on the card that the user writes to. Its text is the
                            // model's, and typing writes the model - which raises, which re-renders this
                            // card, which writes the same text back into the box. Assigning what the box
                            // already holds is not a change, so the round trip costs nothing and the
                            // caret stays put.
                            new VTextBox
                            {
                                Dock = XDock.Fill,
                                Text = model.Name,
                                TextChanged = sender => Rename(sender, model),
                            },
                        ],
                    },

                    // One label per note, so the render reads the count and then describes as many
                    // controls as it says - which is the ordinary thing a component is for.
                    .. Enumerable.Range(1, model.Notes).Select(number => (VNode)new VLabel
                    {
                        Dock = XDock.Top,
                        AutoSize = false,
                        Height = 22f,
                        Font = Theme.DefaultFont,
                        Text = $"Note {number}",
                        Margin = new Thickness(0f, 0f, 0f, 2f),
                    }),
                ],
            };
        }

        /// <summary>Takes what the user typed into the model, which is the whole of the binding.</summary>
        /// <param name="sender">The box that changed.</param>
        /// <param name="model">The model being written.</param>
        private static void Rename(object sender, ReleaseModel model)
        {
            if (sender is TextBox box && !string.Equals(box.Text, model.Name, StringComparison.Ordinal))
            {
                model.Name = box.Text ?? string.Empty;
            }
        }
    }
}
