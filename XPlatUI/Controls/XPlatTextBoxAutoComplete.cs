namespace XPlatUI.AutoComplete;

/// <summary>What a text box does with its suggestions as the user types.</summary>
public enum XPlatAutoCompleteMode
{
    /// <summary>No suggestions and no inline completion.</summary>
    None = 0,

    /// <summary>
    /// A list of matching values opens under the caret as the text changes. The text itself is not
    /// changed until the user takes a suggestion.
    /// </summary>
    Suggest = 1,

    /// <summary>
    /// The rest of the first match is appended to what the user typed and selected, so the next
    /// keystroke replaces it. No list is shown.
    /// </summary>
    Append = 2,

    /// <summary>
    /// The rest of the first match is appended and selected, and the matching list opens under the
    /// caret at the same time.
    /// </summary>
    SuggestAppend = 3,
}

/// <summary>Where a text box's suggestions come from.</summary>
public enum XPlatAutoCompleteSource
{
    /// <summary>The values in <c>AutoCompleteCustomSource</c>.</summary>
    CustomSource = 0,

    /// <summary>
    /// The values the user has committed in this text box - with Enter, by taking a suggestion, or
    /// by leaving the field - most recent first. The list is bounded.
    /// </summary>
    HistoryList = 1,
}

/// <summary>
/// The autocomplete members an <see cref="XPlatTextBox"/> gains by opting into the feature: the
/// mode, the source of the suggestions and the custom list itself.
/// </summary>
/// <remarks>
/// <para>
/// These are extension members rather than members of <see cref="XPlatTextBox"/>, the way
/// FEATURE-COST-POLICY.md §5 asks: the feature keeps its state and its code in a table keyed by
/// the control, so an
/// application that never turns autocomplete on carries none of it. The surface still reads as part
/// of the control, object initializers included:
/// <code>
/// var city = new XPlatTextBox
/// {
///     AutoCompleteMode = XPlatAutoCompleteMode.SuggestAppend,
///     AutoCompleteSource = XPlatAutoCompleteSource.CustomSource,
/// };
/// city.AutoCompleteCustomSource.Add("Amsterdam");
/// </code>
/// </para>
/// <para>
/// The suggestion list is an <see cref="XPlatListBox"/> in an <see cref="XPlatPopup"/> under the
/// caret, so it can leave the window when the platform layer provides popup windows. It never takes
/// the keyboard: the text box keeps the caret and forwards Up, Down, Enter and Escape to the list,
/// the way <see cref="XPlatComboBox"/> does for its drop-down.
/// </para>
/// </remarks>
public static class XPlatTextBoxAutoCompleteExtensions
{
    extension(XPlatTextBox textBox)
    {
        /// <summary>
        /// What the box does with its suggestions as the user types. The default,
        /// <see cref="XPlatAutoCompleteMode.None"/>, turns the feature off.
        /// </summary>
        public XPlatAutoCompleteMode AutoCompleteMode
        {
            get => XPlatTextBoxAutoCompleteState.Find(textBox)?.Mode ?? XPlatAutoCompleteMode.None;
            set
            {
                // Setting None on a box that never opted in must not allocate the state, or every
                // text box in an application that assigns the default would pay for the feature.
                if (value == XPlatAutoCompleteMode.None && XPlatTextBoxAutoCompleteState.Find(textBox) is null)
                {
                    return;
                }

                XPlatTextBoxAutoCompleteState.Get(textBox).Mode = value;
            }
        }

        /// <summary>Where the suggestions come from. Defaults to <see cref="XPlatAutoCompleteSource.CustomSource"/>.</summary>
        public XPlatAutoCompleteSource AutoCompleteSource
        {
            get => XPlatTextBoxAutoCompleteState.Find(textBox)?.Source ?? XPlatAutoCompleteSource.CustomSource;
            set => XPlatTextBoxAutoCompleteState.Get(textBox).Source = value;
        }

        /// <summary>
        /// The values the box suggests when <see cref="AutoCompleteSource"/> is
        /// <see cref="XPlatAutoCompleteSource.CustomSource"/>. The list is read on every change, so
        /// adding a value while the user is typing takes effect on the next keystroke.
        /// </summary>
        public IList<string> AutoCompleteCustomSource =>
            XPlatTextBoxAutoCompleteState.Get(textBox).CustomSource;
    }
}
