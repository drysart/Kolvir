using Kolvir.VirtualDom;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The virtual DOM page: a view model that renders a description of its controls, and the reconciler
/// that turns the description into the control tree under the card.
/// </summary>
/// <remarks>
/// <para>
/// Everything else on this page is ordinary Kolvir - a control built once, with handlers that mutate
/// it. This card is the other arrangement: the page mounts a component, and the component's
/// <see cref="Component.Render"/> describes what should be on screen. The page itself never touches the
/// controls inside the card, and the component never holds one.
/// </para>
/// <para>
/// The numbers on the card are there to make the loop visible. Every click re-renders and the render
/// count goes up, while the buttons the click came from keep their identity: the reconciler patched
/// them rather than rebuilding them, which is why the counter's own focus and the button's pressed
/// state are not lost. Reversing the list is the sharper demonstration - the rows are keyed, so their
/// controls are <em>moved</em> rather than recreated.
/// </para>
/// </remarks>
internal sealed class VirtualDomPage : DemoPage
{
    private readonly CounterCard _card = new();

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public VirtualDomPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Virtual DOM"));
        card.Controls.Add(Note(
            "The card below is a view model: its Render() returns a description of the controls it "
            + "wants, and the reconciler diffs that into the tree underneath. Not one control inside "
            + "the card was built by the page. Click to change its state, and watch the render count "
            + "rise while the same buttons stay on screen."));

        var stage = new Panel
        {
            Dock = XDock.Fill,
            BackColor = null,
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
    /// A card whose controls are described, not built: a counter, two buttons and a keyed row list.
    /// </summary>
    private sealed class CounterCard : Component
    {
        private static readonly (int Id, string Name)[] Rows =
        [
            (1, "First row"),
            (2, "Second row"),
            (3, "Third row"),
            (4, "Fourth row"),
        ];

        private int _clicks;
        private int _renders;
        private bool _reversed;

        public override VNode Render()
        {
            _renders++;

            return new VPanel
            {
                Dock = XDock.Fill,
                BackColor = null,
                Border = Border.None,
                Padding = new Thickness(0f),
                Children =
                [
                    new VButton
                    {
                        Dock = XDock.Top,
                        AutoSize = true,
                        Text = "Add one",
                        Style = ButtonStyle.Primary,
                        Margin = new Thickness(0f, 0f, 0f, 8f),
                        Click = _ => Count(),
                    },
                    new VButton
                    {
                        Dock = XDock.Top,
                        AutoSize = true,
                        Text = "Reverse the list",
                        Style = ButtonStyle.Secondary,
                        Margin = new Thickness(0f, 0f, 0f, 8f),
                        Click = _ => Reverse(),
                    },
                    new VLabel
                    {
                        Dock = XDock.Top,
                        AutoSize = false,
                        Height = 20f,
                        Font = Theme.CaptionFont,
                        ForeColorRole = ThemeColor.MutedForeground,
                        Text = $"{_clicks} click(s), {_renders} render(s)",
                        Margin = new Thickness(0f, 0f, 0f, 8f),
                    },
                    new VPanel
                    {
                        Key = "rows",
                        Dock = XDock.Fill,
                        BackColor = null,
                        Border = Border.None,
                        Padding = new Thickness(0f),
                        Children = [.. RowsInOrder()],
                    },
                ],
            };
        }

        private void Count()
        {
            _clicks++;
            SetState();
        }

        private void Reverse()
        {
            _reversed = !_reversed;
            SetState();
        }

        /// <summary>
        /// The rows, in the order the current state asks for. Each is keyed by its id, so reversing the
        /// list moves the controls that are already there instead of building new ones.
        /// </summary>
        private IEnumerable<VNode> RowsInOrder()
        {
            var order = _reversed ? Rows.Reverse() : Rows;

            foreach (var row in order)
            {
                yield return new VLabel
                {
                    Key = row.Id,
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 22f,
                    Text = row.Name,
                    Margin = new Thickness(0f, 0f, 0f, 2f),
                };
            }
        }
    }
}
