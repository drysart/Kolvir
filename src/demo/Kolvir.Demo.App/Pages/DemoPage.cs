using Kolvir.Svg;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// One page of the sample: its own control tree, built once and kept for the life of the window.
/// </summary>
/// <remarks>
/// <para>
/// The shell owns the window - the header, the navigation list, the status line and the stage - and a
/// page owns everything below the stage's inset: its cards, its readouts, its timers and the state
/// they drive. A page reaches the shell only for what belongs to the whole window: the status line,
/// and the handful of window services some pages drive (the owned window, the badge, the palette).
/// </para>
/// <para>
/// A page is constructed, never rebuilt. A control tree in this library keeps its state and its
/// handlers, so the shell builds every page once and a page swap is a re-parent.
/// </para>
/// </remarks>
internal abstract class DemoPage
{
    private Control? _root;

    /// <summary>Creates a page for <paramref name="shell"/>.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    protected DemoPage(DemoUi shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        Shell = shell;
    }

    /// <summary>The window the page is shown in: the header, the status line and the services.</summary>
    protected DemoUi Shell { get; }

    /// <summary>
    /// What the shell puts on the stage: the page's content inside the inset every page shares.
    /// </summary>
    /// <remarks>
    /// A page publishes this from its own constructor, after it has built its tree, so that the tree
    /// can assign and use the page's fields. Reaching a page whose constructor did not do that is a
    /// mistake in the page rather than in the shell, which is why it fails here rather than showing
    /// an empty stage.
    /// </remarks>
    public Control Root => _root ?? throw new InvalidOperationException(
        $"{GetType().Name} did not call Initialize, so it has no control tree to show.");

    /// <summary>Publishes the page's content, inside the inset every page shares.</summary>
    /// <param name="body">The page's own tree.</param>
    protected void Initialize(Control body) => _root = Page(body);

    /// <summary>Reports in the window's status line.</summary>
    /// <param name="message">What to say.</param>
    /// <remarks>
    /// The status line belongs to the window rather than to a page - the header, the menu bar and
    /// every page write to the same one - so a page asks the shell rather than keeping its own.
    /// </remarks>
    protected void SetStatus(string message) => Shell.SetStatus(message);

    /// <summary>The window the sample is shown in, or null before one is attached.</summary>
    protected Window? OwnerWindow => Shell.OwnerWindow;

    /// <summary>
    /// Called once for every page when the window the sample is showing in is attached.
    /// </summary>
    /// <param name="window">The window the sample is displayed in.</param>
    /// <remarks>
    /// A page is built before the window exists, so it cannot subscribe to it or read from it while
    /// it is being built: the chrome readout, the badge's changes and the web font's loader all only
    /// exist once there is a window. Headless renders never call this, which is why a page that needs
    /// the window reports "no window" rather than failing.
    /// </remarks>
    internal virtual void OnWindowAttached(Window window)
    {
    }

    /// <summary>Makes <paramref name="strip"/> drag the window it is in, the way a title bar does.</summary>
    /// <param name="strip">The control the press starts on.</param>
    protected void AttachWindowDrag(Control strip) => Shell.AttachWindowDrag(strip);

    /// <summary>
    /// Wraps a page body in the inset every page shares, so a page is only ever its content and the
    /// space around it is decided in one place.
    /// </summary>
    /// <param name="body">The page's own tree.</param>
    /// <returns>The control the shell shows.</returns>
    internal static Panel Page(Control body)
    {
        var page = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(20f, 16f, 20f, 16f),
        };

        page.Controls.Add(body);
        return page;
    }

    /// <summary>
    /// A frame of one pixel with rounded corners, which is the look the sample's cards and framed
    /// panels share. It takes no room: the sample's gutters were chosen with the frame sharing them,
    /// so reserving a pixel for the stroke would move the contents of every page by one.
    /// </summary>
    /// <param name="cornerRadius">The radius of the corners, in logical pixels.</param>
    /// <returns>The border.</returns>
    internal static SimpleBorder Framed(float cornerRadius) =>
        SimpleBorder.Thin.WithCornerRadius(cornerRadius).WithInsets(Thickness.Empty);

    /// <summary>The card most pages are a column of: the surface, a ten-pixel frame and a gutter.</summary>
    /// <returns>An empty card, docked to fill.</returns>
    internal static Panel Card() => new SurfacePanel()
    {
        Dock = XDock.Fill,
        Border = Framed(10f),
        Padding = new Thickness(16f),
    };

    /// <summary>A page's title, at the top of its card.</summary>
    /// <param name="text">The title.</param>
    /// <returns>The label, docked to the top.</returns>
    internal static Label Heading(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 26f,
        Font = Theme.EmphasizedFont,
        TextAlign = ContentAlignment.BottomLeft,
        Margin = new Thickness(0f, 0f, 0f, 8f),
    };

    /// <summary>A small muted caption, for the line above a control or a readout.</summary>
    /// <param name="text">The caption.</param>
    /// <returns>The label, docked to the top.</returns>
    internal static Label ScrollCaption(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 16f,
        Font = Theme.CaptionFont,
        ForeColor = ThemeColor.MutedForeground,
        TextAlign = ContentAlignment.BottomLeft,
        Margin = new Thickness(0f, 0f, 0f, 2f),
    };

    /// <summary>A small muted caption sitting above a field.</summary>
    /// <param name="text">The caption.</param>
    /// <returns>The label, docked to the top.</returns>
    internal static Label Caption(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 20f,
        Font = Theme.CaptionFont,
        ForeColor = ThemeColor.MutedForeground,
        TextAlign = ContentAlignment.BottomLeft,
    };

    /// <summary>A paragraph of explanation: a wrapped caption block.</summary>
    /// <param name="text">The paragraph.</param>
    /// <returns>The label, docked to the top.</returns>
    internal static Label Note(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 66f,
        WrapText = true,
        Font = Theme.CaptionFont,
        ForeColor = ThemeColor.MutedForeground,
        TextAlign = ContentAlignment.TopLeft,
        Margin = new Thickness(0f, 0f, 0f, 4f),
    };

    /// <summary>An invisible full-width strip a page uses to lay a row of controls out in.</summary>
    /// <param name="height">How tall the strip is.</param>
    /// <returns>An empty strip, docked to the top.</returns>
    internal static Panel Row(float height) => new()
    {
        Dock = XDock.Top,
        Height = height,
        Border = Border.None,
        Padding = new Thickness(0f),
        Margin = new Thickness(0f, 0f, 0f, 8f),
    };

    /// <summary>An invisible strip that a row of buttons docks into, left to right.</summary>
    /// <returns>An empty strip, docked to the top.</returns>
    internal static Panel ButtonRow() => new()
    {
        Dock = XDock.Top,
        Height = 30f,
        Border = Border.None,
        Padding = new Thickness(0f),
        Margin = new Thickness(0f, 0f, 0f, 6f),
    };

    /// <summary>An invisible strip for a row of thumbnails, one beside the next.</summary>
    /// <param name="height">How tall the strip is.</param>
    /// <returns>An empty strip, docked to the top.</returns>
    internal static Panel ThumbnailRow(float height = 72f) => new()
    {
        Dock = XDock.Top,
        Height = height,
        Padding = new Thickness(0f),
        Margin = new Thickness(0f, 0f, 0f, 4f),
    };

    /// <summary>A row with a caption on the left and a field of a fixed width on the right.</summary>
    /// <param name="caption">The caption.</param>
    /// <param name="field">The control the row is for.</param>
    /// <returns>The row, docked to the top.</returns>
    internal static Panel CaptionRow(string caption, Control field)
    {
        var row = new Panel
        {
            Dock = XDock.Top,
            Height = 34f,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };

        row.Controls.Add(new Label(caption)
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = Theme.DefaultFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 12f, 0f),
        });

        field.Dock = XDock.Right;
        field.Width = 240f;
        row.Controls.Add(field);
        return row;
    }

    /// <summary>A secondary button a page uses for one of its own actions.</summary>
    /// <param name="text">The button's text.</param>
    /// <param name="onClick">What the click does.</param>
    /// <returns>The button, docked to the top.</returns>
    internal static Button DemoButton(string text, Action onClick)
    {
        var button = new Button(text)
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };

        button.AddClickHandler(_ => onClick());
        return button;
    }

    /// <summary>A small pill-shaped button, for an inline action inside a sentence.</summary>
    /// <param name="text">The chip's text.</param>
    /// <returns>The chip.</returns>
    internal Button Chip(string text)
    {
        var chip = new Button(text)
        {
            Style = ButtonStyle.Secondary,
            AutoSize = true,
            Font = Theme.CaptionFont,
            Padding = new Thickness(8f, 4f),
            MinimumSize = new SKSize(0f, 26f),
            Border = SimpleBorder.Rounded(13f),
        };

        chip.AddClickHandler(_ => SetStatus($"Inline chip '{text}' clicked."));
        return chip;
    }

    /// <summary>
    /// Runs <paramref name="action"/> the first time <paramref name="control"/> has a host, which is
    /// the first moment there is one to subscribe to. Pages are built before they are attached, and
    /// attaching lays the tree out straight away, so the first bounds change is the earliest point at
    /// which <see cref="Control.Host"/> resolves. The handler stays registered and does nothing after
    /// that rather than disposing itself from inside its own invocation.
    /// </summary>
    /// <param name="control">The control whose host is wanted.</param>
    /// <param name="action">What to do with the host, once.</param>
    internal static void OnHostAvailable(Control control, Action<UiHost> action)
    {
        bool done = false;

        control.AddBoundsChangedHandler(_ =>
        {
            if (done || control.Host is not { } host)
            {
                return;
            }

            done = true;
            action(host);
        });
    }

    /// <summary>The thickness as four numbers, for a readout that has to fit on one line.</summary>
    /// <param name="thickness">The thickness.</param>
    /// <returns>The four edges, left to right.</returns>
    internal static string Describe(Thickness thickness) =>
        $"{thickness.Left:0.#},{thickness.Top:0.#},{thickness.Right:0.#},{thickness.Bottom:0.#}";

    /// <summary>The rectangle as a position and a size, or "none" when it is empty.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The rectangle in text.</returns>
    internal static string Describe(SKRect rect) =>
        rect.Width <= 0f || rect.Height <= 0f
            ? "none"
            : $"{rect.Left:0.#},{rect.Top:0.#} {rect.Width:0.#}x{rect.Height:0.#}";

    /// <summary>The colour as hex, for the readouts the colour pages share.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The colour in text.</returns>
    internal static string DescribeColor(SKColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2} (R {color.Red}, G {color.Green}, B {color.Blue})";

    /// <summary>Where each frame of an animation starts, read back from the timeline itself.</summary>
    /// <param name="data">The image.</param>
    /// <returns>One start time per frame, in order.</returns>
    internal static List<TimeSpan> FrameStarts(ImageData data)
    {
        var starts = new List<TimeSpan> { TimeSpan.Zero };
        var position = TimeSpan.Zero;

        while (true)
        {
            if (data.TimeUntilNextFrame(position) is not { } wait)
            {
                return starts;
            }

            var boundary = position + wait;
            if (boundary <= position || boundary >= data.Duration)
            {
                return starts;
            }

            starts.Add(boundary);
            position = boundary;
        }
    }

    /// <summary>
    /// The formats the sample enables for its images.
    /// </summary>
    /// <remarks>
    /// The application's <c>ConfigureServices</c> does the same thing through the container - see
    /// <c>Program.cs</c> - and this factory exists because the sample is also rendered headlessly,
    /// where no container is set up. One list, composed once, and every image on every page comes
    /// from it: <c>Images.LoadFromStream(...)</c> reads a PNG, an SVG, an animated GIF or an animated
    /// WebP without the call site knowing which.
    /// </remarks>
    internal static readonly ImageDataFactory Images =
        new([
            SvgImageFormatProvider.Shared,
            GifImageFormatProvider.Shared,
            WebpImageFormatProvider.Shared,
        ]);

    /// <summary>
    /// Encodes the gradient the images page shows. A PNG rather than a picture: the page is meant to
    /// exercise the bitmap path an application takes, and Phase 1 has to leave that page looking
    /// exactly as it did.
    /// </summary>
    /// <param name="width">The bitmap's width.</param>
    /// <param name="height">The bitmap's height.</param>
    /// <returns>The encoded PNG.</returns>
    internal static MemoryStream CreateGradientPng(int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0f, 0f),
                   new SKPoint(width, height),
                   new[] { new SKColor(0xFF2F6FED), new SKColor(0xFF7C3AED), new SKColor(0xFFEC4899) },
                   SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
        {
            canvas.DrawRect(new SKRect(0f, 0f, width, height), paint);
        }

        using (var paint = new SKPaint { Color = new SKColor(0x33FFFFFF), IsAntialias = true })
        {
            canvas.DrawCircle(width * 0.22f, height * 0.3f, height * 0.28f, paint);
            canvas.DrawCircle(width * 0.78f, height * 0.72f, height * 0.36f, paint);
        }

        using (var font = new SKFont(SKTypeface.Default, 30f) { Edging = SKFontEdging.Antialias })
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText("SkiaSharp", 22f, height - 26f, SKTextAlign.Left, font, paint);
        }

        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }
}
