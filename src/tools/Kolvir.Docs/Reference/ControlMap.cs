namespace Kolvir.Docs.Reference;

/// <summary>
/// One entry of the control map: what a generated page says about where a control is explained and
/// demonstrated.
/// </summary>
/// <param name="Type">The type name, as it is written in code.</param>
/// <param name="Demo">The sample's page id that exercises the control, or null when no page does.</param>
/// <param name="Guide">The how-to page, relative to <c>docs/</c>, or null while that page is unwritten.</param>
/// <param name="Related">Types documented as sections of this page rather than with pages of their own.</param>
/// <param name="IncludeRemarks">
/// True to carry the type's <c>remarks</c> through. Off by default: most of them argue for a design,
/// which is contributor material, and the ones that describe behaviour are turned on one at a time.
/// </param>
internal sealed record ControlEntry(
    string Type,
    string? Demo,
    string? Guide = null,
    string[]? Related = null,
    bool IncludeRemarks = false);

/// <summary>
/// Which types get a reference page, where each is demonstrated, and which public types are
/// deliberately left out.
/// </summary>
/// <remarks>
/// <para>
/// The map is the authority, and the generator enforces it in both directions: a public control with
/// no entry fails the build, and an entry naming a type that no longer exists fails it too. That is
/// what keeps a new control from shipping without documentation and a removed one from leaving a
/// page behind.
/// </para>
/// <para>
/// <see cref="Excluded"/> records the public controls that exist for the library's own use - the menu
/// bar and menu drop-down presentations - so their absence from the reference is a decision on record
/// rather than an oversight.
/// </para>
/// </remarks>
internal static class ControlMap
{
    /// <summary>Every page the reference generates.</summary>
    internal static IReadOnlyList<ControlEntry> Entries { get; } =
    [
        new("Control", Demo: "relative-sizing"),
        new("Panel", Demo: null),
        new("ToggleControl", "check-boxes-and-radios"),
        new("BusyIndicator", "busy-indicator-and-overlay"),
        new("Button", "buttons-and-input"),
        new("CheckBox", "check-boxes-and-radios"),
        new("RadioButton", "check-boxes-and-radios"),
        new("ColorPicker", "colour-picker"),
        new("ComboBox", "combo-boxes"),
        new("DateTimePicker", "date-and-time", Related: ["DateTimePickerFormat"]),
        new("Expander", "expander"),
        new("GroupBox", "group-boxes"),
        new("Image", "images-and-layout"),
        new("Label", "labels-and-text"),
        new("LinkLabel", "link-labels", Related: ["LinkClickedEventArgs"]),
        new("ListBox", "lists", Related: ["ListBoxDrawItemEventArgs"]),
        new("ListView", "list-views", Related: ["ListViewItem", "ListViewColumn", "ListViewColumnClickEventArgs"]),
        new("NumericUpDown", "numeric-up-down"),
        new("Overlay", "busy-indicator-and-overlay"),
        new("ProgressBar", "progress-bars", Related: ["ProgressBarStyle"]),
        new("RichLabel", "rich-text", Related: ["TextSpan", "InlineAlignment", "SpanEventArgs"]),
        new("ScrollBar", "scrolling"),
        new("SplitButton", "split-buttons"),
        new("DropDownButton", "split-buttons"),
        new("SplitContainer", "split-container", Related: ["FixedPanel"]),
        new("StackPanel", "stack-panel"),
        new("StatusStrip", "status-and-tool-strips"),
        new("TabControl", "tab-controls"),
        new("TabPage", "tab-controls"),
        new("TableLayoutPanel", "table-layout", Related: ["TableLayoutPanelExtensions"]),
        new("TextBox", "text-input", Related: ["AutoCompleteMode", "AutoCompleteSource", "TextBoxAutoCompleteExtensions", "TextBoxSpan", "TextBoxSpanExtensions", "TextBoxMultilineExtensions", "TextBoxUndoExtensions"]),
        new("Toast", "toast", Related: ["ToastSeverity"]),
        new("ToolStrip", "status-and-tool-strips", Related: ["ToolStripItem", "ToolStripButton", "ToolStripLabel", "ToolStripSeparator", "ToolStripProgressBar", "ToolStripSpring", "ToolStripItemCollection", "ToolStripItemAlignment"]),
        new("ToolTip", "tool-tips"),
        new("TrackBar", "track-bars"),
        new("TreeView", "tree-view", Related: ["TreeNode"]),
        new("VirtualList", "performance-list-box"),
        new("WrapPanel", "wrap-panel"),
    ];

    /// <summary>Public controls that are the library's own presentation rather than an application's.</summary>
    internal static IReadOnlyDictionary<string, string> Excluded { get; } = new Dictionary<string, string>
    {
        ["MenuBarView"] = "the menu bar's own presentation; an application builds a MenuBar, not this",
        ["MenuView"] = "a menu drop-down's own presentation; an application builds a Menu, not this",
    };
}
