using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The graphics acceleration page: every choice <see cref="IGpuAccelerationService"/> reports for this
/// machine, and the machine-readable value of the one selected.
/// </summary>
/// <remarks>
/// <para>
/// The list is what an application's settings UI would show, and the value under it is what that UI
/// would save. The value is the interesting part to look at on real hardware: its device half is a
/// hardware identity - an adapter's vendor, device and subsystem ids for Direct3D 12, a device UUID for
/// Vulkan - so reading it back on a machine with more than one GPU is how the claim that it names a
/// card rather than a position gets checked.
/// </para>
/// <para>
/// Selecting a row changes nothing on screen: there is no live switch yet. The selection is what an
/// application would hand to <see cref="ApplicationOptions.GfxAcceleration"/> at the next start, which
/// is why the page also reports what the window is drawing with now - the two agreeing is the whole
/// point of a saved setting.
/// </para>
/// </remarks>
internal sealed class GraphicsAccelerationPage : DemoPage
{
    /// <summary>The options on the list, in the order the rows were added.</summary>
    private readonly List<GpuAccelerationOption> _options = [];

    private ListBox _list = null!;
    private Label _valueLabel = null!;
    private Label _currentLabel = null!;
    private Label _windowLabel = null!;
    private IGpuAccelerationService? _service;

    /// <summary>What the backend last said it was drawing with, which arrives after the window shows.</summary>
    private string _graphicsPath = "not reported yet";
    private string? _graphicsRenderer;

    /// <summary>Set while the rows are being filled, so replacing them does not change the readouts.</summary>
    private bool _filling;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public GraphicsAccelerationPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// Tells the page which presentation path produced what is on screen, the same report the
    /// measured-rendering page gets.
    /// </summary>
    /// <param name="path">The path the window is rendering through.</param>
    /// <param name="renderer">The backend's own name for its renderer, or null when it does not report one.</param>
    internal void SetGraphicsInfo(string path, string? renderer)
    {
        _graphicsPath = path;
        _graphicsRenderer = renderer;
        ShowCurrent();
    }

    /// <inheritdoc />
    internal override void OnWindowAttached(Window window)
    {
        _service = window.Services.GetService(typeof(IGpuAccelerationService)) as IGpuAccelerationService;

        if (_service is null)
        {
            _list.Items.Add("This application registered no IGpuAccelerationService.");
            _list.Enabled = false;
            _valueLabel.Text = "none";
            _currentLabel.Text = "no service, so nothing is reported";
            ShowCurrent();
            return;
        }

        Populate(_service);

        // The service moves Current when a window builds its tier, which happens on the first paint -
        // after this page was built. Following the event is what keeps the readout honest without a
        // refresh button being the only way to see it.
        _service.CurrentChanged += (_, _) => ShowCurrent();
        ShowCurrent();
    }

    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Graphics acceleration"));

        card.Controls.Add(new Label(
            "Every choice this machine offers, from IGpuAccelerationService. The value under the list is "
            + "what an application saves: the family, then the card's hardware identity - vendor, device "
            + "and subsystem ids on Direct3D 12, a device UUID on Vulkan - rather than its position in "
            + "the enumeration, which shifts when a GPU is added or disabled. Selecting a row applies "
            + "nothing; there is no live switch, so a saved value takes effect at the next start.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 56f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });

        card.Controls.Add(ScrollCaption("Options"));

        _list = new ListBox
        {
            Dock = XDock.Top,
            Height = 150f,
            SelectionMode = SelectionMode.One,
            VerticalScroll = ScrollStyle.Automatic,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };

        _list.AddSelectedIndexChangedHandler(_ => ShowSelection());
        card.Controls.Add(_list);

        card.Controls.Add(Caption("Selected value"));
        _valueLabel = new Label("(nothing selected)")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 26f,
            WrapText = true,
            Font = Theme.EmphasizedFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };
        card.Controls.Add(_valueLabel);

        card.Controls.Add(Caption("In use, according to the service"));
        _currentLabel = new Label("not read yet")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 0f, 4f),
        };
        card.Controls.Add(_currentLabel);

        card.Controls.Add(Caption("In use, according to the window"));
        _windowLabel = new Label("not reported yet")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        card.Controls.Add(_windowLabel);

        var buttons = ButtonRow();
        buttons.Controls.Add(RowButton("Copy value", CopyValue));
        buttons.Controls.Add(RowButton("Select the one in use", SelectCurrent));
        buttons.Controls.Add(RowButton("Refresh", ShowCurrent));
        card.Controls.Add(buttons);

        return card;

        static Button RowButton(string text, Action onClick)
        {
            var button = new Button(text)
            {
                Dock = XDock.Left,
                AutoSize = true,
                Style = ButtonStyle.Secondary,
                Margin = new Thickness(0f, 0f, 8f, 0f),
            };

            button.AddClickHandler(_ => onClick());
            return button;
        }
    }

    /// <summary>Fills the list from the service, one row per fully-resolved choice.</summary>
    /// <param name="service">The service to ask.</param>
    private void Populate(IGpuAccelerationService service)
    {
        _filling = true;

        try
        {
            _options.Clear();
            _list.Items.Clear();

            foreach (var type in service.Types)
            {
                foreach (var option in type.Options)
                {
                    _options.Add(option);
                    _list.Items.Add(option.Name);
                }
            }

            SelectCurrent();
        }
        finally
        {
            _filling = false;
        }

        ShowSelection();
    }

    /// <summary>Selects the row that stands for the choice the window is drawing with.</summary>
    private void SelectCurrent()
    {
        if (_service is null)
        {
            return;
        }

        var index = _options.FindIndex(option => option == _service.Current);

        if (index >= 0)
        {
            _list.SelectedIndex = index;
        }
    }

    /// <summary>Shows the value of the selected row: the string an application would save.</summary>
    private void ShowSelection()
    {
        var index = _list.SelectedIndex;

        if (_filling || index < 0 || index >= _options.Count)
        {
            return;
        }

        var option = _options[index];
        _valueLabel.Text = option.Value;
        SetStatus($"Acceleration value: {option.Value}");
    }

    /// <summary>Shows what the service and the window each say is in use.</summary>
    private void ShowCurrent()
    {
        if (_service is not { } service)
        {
            return;
        }

        var current = service.Current;
        _currentLabel.Text = $"{current.Value} - {current.Name}";

        _windowLabel.Text = _graphicsRenderer is { } renderer
            ? $"{_graphicsPath} - {renderer}"
            : _graphicsPath;

        // The window can report before the page has a selection of its own, and a report of a new tier
        // is the moment the selected row should follow it.
        if (_list.SelectedIndex < 0)
        {
            SelectCurrent();
        }
    }

    private void CopyValue()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _options.Count)
        {
            SetStatus("Nothing selected to copy.");
            return;
        }

        var value = _options[_list.SelectedIndex].Value;
        Clipboard.SetText(value);
        SetStatus($"Copied \"{value}\" to the clipboard.");
    }
}
