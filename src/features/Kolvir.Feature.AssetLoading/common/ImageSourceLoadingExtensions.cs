namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// The members an <see cref="Image"/> gains by opting into asset loading: a URI to load, a
/// placeholder to show until there is something to draw, and the state of the request plus the
/// handlers that hear when it changes.
/// </summary>
/// <remarks>
/// <para>
/// These are extension members rather than members of <see cref="Image"/>, because a feature package
/// in another assembly cannot add members to a core type
/// (<c>FEATURE-COST-POLICY.md</c> section 4). The surface still reads as part of the control:
/// </para>
/// <code>
/// using Kolvir.Feature.AssetLoading;
///
/// var image = new Image { SourceUri = "embedded://logo.png", PlaceholderSource = shine };
///
/// using var subscription = image.AddSourceLoadStateChangedHandler(sender =>
///     spinner.Visible = ((Image)sender).SourceLoadState == ImageSourceLoadState.Loading);
/// </code>
/// <para>
/// <b>What it does.</b> It asks <see cref="IAssetLoaderProvider"/> for a loader that produces an
/// <see cref="ImageData"/> for the URI, submits it to <see cref="IAssetLoadManager"/>, and assigns
/// what comes back to <see cref="Image.Source"/>. The priority is
/// <see cref="AssetLoadPriority.Visible"/> while the control is on screen and
/// <see cref="AssetLoadPriority.Background"/> while it is not, taken from the effective-in-view
/// feature rather than from a second notion of visibility (section 3.15).
/// </para>
/// <para>
/// <b>What an application has to do.</b> Call <c>services.AddAssetLoading()</c> and register at least
/// one factory with <c>services.AddAssetLoaderFactory()</c>. Nothing starts until the control can
/// reach those services through <see cref="Control.Services"/>, which is why a URI assigned before the
/// control is attached waits at <see cref="ImageSourceLoadState.Pending"/> (sections 2.2 and 6.1).
/// </para>
/// <para>
/// <b>Ownership.</b> The loaded image belongs to the feature: it is disposed when it is replaced, when
/// the URI changes, and when the control is disposed. <c>PlaceholderSource</c> is the other way
/// round - the application created it and the application disposes it, and the feature only ever
/// writes it into <see cref="Image.Source"/> (section 6.3).
/// </para>
/// <para>
/// <b>It is UI-thread state</b>, like every other read and write of the control tree; the manager moves
/// a loader's notifications onto that thread before they arrive here (sections 3.12 and 6).
/// </para>
/// </remarks>
public static class ImageSourceLoadingExtensions
{
    extension(Image image)
    {
        /// <summary>
        /// The URI this control follows, or null when it is not following one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Assigning a URI puts the control into URI mode: the feature owns <see cref="Image.Source"/>
        /// for as long as the relationship lasts, writing the placeholder until the load produces an
        /// image and the loaded image afterwards. Assigning null ends the relationship: the load is
        /// cancelled, the loaded image is disposed, and the state is
        /// <see cref="ImageSourceLoadState.None"/>.
        /// </para>
        /// <para>
        /// A URI assigned before the control is attached is accepted and waits at
        /// <see cref="ImageSourceLoadState.Pending"/> - the setter records it and starts nothing,
        /// because <see cref="Control.Services"/> is null until the control is attached. The load
        /// starts when the control can reach its services.
        /// </para>
        /// <para>
        /// Assigning the value the control already holds does nothing: no restart, no state change and
        /// no write to <see cref="Image.Source"/>. That is what makes a re-rendering tree safe, and it
        /// is what makes a retry a deliberate act - <c>SourceUri = null</c> and then the URI, in that
        /// order.
        /// </para>
        /// <para>
        /// The getter returns exactly what was assigned. It has to exist: the VirtualDom generator
        /// reads a property's pristine value back through a public getter when a render drops it, and a
        /// set-only property would be silently skipped (<c>EXTENSION-MEMBERS-ON-VNODES.md</c>
        /// section 3).
        /// </para>
        /// </remarks>
        public string? SourceUri
        {
            get => ImageSourceLoadingState.GetSourceUri(image);
            set => ImageSourceLoadingState.SetSourceUri(image, value);
        }

        /// <summary>
        /// The image drawn whenever the control has no loaded image for its URI: while the load is
        /// pending or in flight, and after it has failed. Null by default, which leaves
        /// <see cref="Image.PlaceholderColor"/> doing what it does today.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It is written into <see cref="Image.Source"/>, so there is no second drawing path: what the
        /// control shows is always its <c>Source</c>. The assignment happens under the same guard as a
        /// loaded image, so it is never mistaken for the application taking the control over.
        /// </para>
        /// <para>
        /// It is borrowed, not owned: the application created it, and the application disposes it. The
        /// feature never does - not even when a load succeeds over it. A loaded image always wins, so
        /// the feature never has to clear the placeholder on the way out.
        /// </para>
        /// <para>
        /// An assignment while an image is loaded leaves that image on screen, and the new placeholder
        /// is used from the next state that has none. A null assignment means nothing at all is shown
        /// and <see cref="Image.PlaceholderColor"/> does the work.
        /// </para>
        /// </remarks>
        public ImageData? PlaceholderSource
        {
            get => ImageSourceLoadingState.GetPlaceholderSource(image);
            set => ImageSourceLoadingState.SetPlaceholderSource(image, value);
        }

        /// <summary>Where this control's load has got to.</summary>
        /// <remarks>
        /// <para>
        /// A property rather than an event pair, because a consumer wants the current answer - what to
        /// draw now - not the transitions, and because a load that fails synchronously is already
        /// terminal before a handler subscribed afterwards could hear it (section 6). Pair it with
        /// <see cref="AddSourceLoadStateChangedHandler"/> to learn that the answer changed.
        /// </para>
        /// <para>
        /// It is readable while the control is being rendered, so a list can show a placeholder,
        /// a spinner or an error message per image.
        /// </para>
        /// </remarks>
        public ImageSourceLoadState SourceLoadState => ImageSourceLoadingState.GetSourceLoadState(image);

        /// <summary>
        /// What the last load threw, or null when it did not fail.
        /// </summary>
        /// <remarks>
        /// A state cannot carry an exception and an exception type is not a state, so the reason lives
        /// here rather than in <c>SourceLoadState</c>. It is cleared when a new URI is assigned
        /// and when a load succeeds, and a failure leaves <c>SourceUri</c> exactly as it was
        /// (section 6.1).
        /// </remarks>
        public Exception? SourceLoadError => ImageSourceLoadingState.GetSourceLoadError(image);

        /// <summary>
        /// Adds a handler that runs when <c>SourceLoadState</c> changes.
        /// </summary>
        /// <param name="handler">The handler, which receives the image control.</param>
        /// <returns>A subscription that removes the handler when it is disposed.</returns>
        /// <remarks>
        /// <para>
        /// Adding one does not raise it: the state is read through <c>SourceLoadState</c>, and
        /// only a later change is an event. On <see cref="ImageSourceLoadState.Loaded"/> and on
        /// <see cref="ImageSourceLoadState.Failed"/> the handler runs after
        /// <see cref="Image.Source"/> has already been assigned, so one that assigns
        /// <c>PlaceholderSource</c> in response is writing the image that stays on screen
        /// (section 6.1).
        /// </para>
        /// <para>
        /// The handler runs inside whatever the feature was doing - the setter, a tree notification, a
        /// completion - and may assign <c>SourceUri</c> or <see cref="Image.Source"/> itself.
        /// An exception it throws propagates to that caller, as every other handler pair in the library
        /// does.
        /// </para>
        /// </remarks>
        public IDisposable AddSourceLoadStateChangedHandler(Action<object> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            return ImageSourceLoadingState.AddHandler(image, handler);
        }

        /// <summary>Removes a handler that <see cref="AddSourceLoadStateChangedHandler"/> added.</summary>
        /// <param name="handler">The handler to remove.</param>
        public void RemoveSourceLoadStateChangedHandler(Action<object> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            ImageSourceLoadingState.RemoveHandler(image, handler);
        }
    }
}
