namespace Kolvir.Backend.Headless;

/// <summary>
/// The headless backend's <see cref="IDialogService"/>: it records every request and answers
/// from a script instead of showing anything, so a code path that asks the user a question can be
/// tested with no display and no human.
/// </summary>
/// <remarks>
/// Nothing blocks. The handlers are plain delegates, so a test can make an "are you sure" answer yes
/// or no and assert on what the application did next. The default answers are the ones a dismissed
/// dialog would give: <see cref="DialogResult.Cancel"/> and a null path.
/// </remarks>
public sealed class HeadlessDialogService : IDialogService
{
    private readonly List<MessageBoxOptions> _messages = [];
    private readonly List<FileDialogOptions> _openFiles = [];
    private readonly List<FileDialogOptions> _saveFiles = [];
    private readonly List<FileDialogOptions> _folders = [];
    private readonly List<FontDialogOptions> _fonts = [];

    /// <summary>Answers <see cref="ShowMessage"/>. Defaults to <see cref="DialogResult.Cancel"/>.</summary>
    public Func<MessageBoxOptions, Window?, DialogResult> MessageHandler { get; set; } =
        static (_, _) => DialogResult.Cancel;

    /// <summary>Answers <see cref="OpenFile"/>. Defaults to null, meaning the user cancelled.</summary>
    public Func<FileDialogOptions, Window?, string?> OpenFileHandler { get; set; } = static (_, _) => null;

    /// <summary>Answers <see cref="SaveFile"/>. Defaults to null, meaning the user cancelled.</summary>
    public Func<FileDialogOptions, Window?, string?> SaveFileHandler { get; set; } = static (_, _) => null;

    /// <summary>Answers <see cref="PickFolder"/>. Defaults to null, meaning the user cancelled.</summary>
    public Func<FileDialogOptions, Window?, string?> PickFolderHandler { get; set; } = static (_, _) => null;

    /// <summary>Answers <see cref="PickFont"/>. Defaults to null, meaning the user cancelled.</summary>
    public Func<FontDialogOptions, Window?, FontSelection?> FontHandler { get; set; } =
        static (_, _) => null;

    /// <summary>Every message box request, in order.</summary>
    public IReadOnlyList<MessageBoxOptions> Messages => _messages;

    /// <summary>Every open-file request, in order.</summary>
    public IReadOnlyList<FileDialogOptions> OpenFileRequests => _openFiles;

    /// <summary>Every save-file request, in order.</summary>
    public IReadOnlyList<FileDialogOptions> SaveFileRequests => _saveFiles;

    /// <summary>Every folder request, in order.</summary>
    public IReadOnlyList<FileDialogOptions> PickFolderRequests => _folders;

    /// <summary>Every font request, in order.</summary>
    public IReadOnlyList<FontDialogOptions> FontRequests => _fonts;

    /// <inheritdoc />
    public DialogResult ShowMessage(MessageBoxOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        _messages.Add(options);
        return MessageHandler(options, owner);
    }

    /// <inheritdoc />
    public string? OpenFile(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        _openFiles.Add(options);
        return OpenFileHandler(options, owner);
    }

    /// <inheritdoc />
    public string? SaveFile(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        _saveFiles.Add(options);
        return SaveFileHandler(options, owner);
    }

    /// <inheritdoc />
    public string? PickFolder(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        _folders.Add(options);
        return PickFolderHandler(options, owner);
    }

    /// <inheritdoc />
    public FontSelection? PickFont(FontDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        _fonts.Add(options);
        return FontHandler(options, owner);
    }
}
