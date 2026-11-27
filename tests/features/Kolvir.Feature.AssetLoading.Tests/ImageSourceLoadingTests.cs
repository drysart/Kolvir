using Kolvir.Feature.InView;
using SkiaSharp;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// The <see cref="Image"/> integration: the URI, the placeholder, the state, and the ordering rules
/// the control follows when a load starts, completes, fails, is superseded, or is cancelled by the
/// tree it lives in (dev-docs/ASSET-LOADING.md sections 6 and 6.1).
/// </summary>
/// <remarks>
/// Everything is driven by hand: a load starts when the manager's walk starts it, completes when the
/// test completes the fake loader, and is cancelled by the notification the test raises. There is no
/// timer, no sleep and no <c>Task.Delay</c>, so an assertion is about what the control did rather than
/// about how long the test waited (section 7.1).
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class ImageSourceLoadingTests
{
    private const string Uri = "memory://a.png";
    private const string OtherUri = "memory://b.png";

    [TestMethod]
    public void AUriAcceptedBeforeTheControlIsInATreeWaitsAtPending()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var image = new Image { SourceUri = Uri };

        Assert.AreEqual(ImageSourceLoadState.Pending, image.SourceLoadState);
        Assert.AreEqual(Uri, image.SourceUri, "the URI is recorded, so a retry and a render can read it back");
        Assert.AreEqual(0, factory.Loaders.Count, "there are no services to resolve a factory from yet");
        Assert.IsNull(image.SourceLoadError);
    }

    [TestMethod]
    public void AddedToTreeStartsTheLoad()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        Assert.AreEqual(ImageSourceLoadState.Pending, image.SourceLoadState);

        panel.Controls.Add(image);

        Assert.AreEqual(1, factory.Loaders.Count, "joining a hosted tree is where the load starts");
        Assert.AreEqual(Uri, factory.Asked[0]);
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState, "the manager said it started");
    }

    [TestMethod]
    public void ATreeAttachedToAHostStartsAPendingLoadWithoutEitherTreeNotification()
    {
        // The pattern with no notification of its own: the control is added to a tree that is not
        // hosted yet, so AddedToTree fires with no services, and the tree only reaches a host when a
        // window takes it - which raises neither AddedToTree nor RemovedFromTree.
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        var image = new Image { SourceUri = Uri };

        panel.Controls.Add(image);

        Assert.AreEqual(0, factory.Loaders.Count, "no services yet");
        Assert.AreEqual(ImageSourceLoadState.Pending, image.SourceLoadState);

        scene.Open(panel);

        Assert.AreEqual(1, factory.Loaders.Count, "the first completed layout pass is where it starts");
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
    }

    [TestMethod]
    public void AssigningTheUriItAlreadyHoldsDoesNothing()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        int changed = 0;
        using var subscription = image.AddSourceLoadStateChangedHandler(_ => changed++);

        image.SourceUri = Uri;

        Assert.AreEqual(1, factory.Loaders.Count, "no second request was made");
        Assert.AreEqual(0, changed, "and no state change was raised");
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
    }

    [TestMethod]
    public void PendingIsSetBeforeTheLoadIsSubmitted()
    {
        // A loader that completes on start calls back synchronously from inside the manager's
        // LoadAsync, so a control that set Pending after that call would never be seen at Pending.
        var data = new TestImage();
        var factory = new ImageLoaderFactory
        {
            Configure = loader =>
            {
                loader.CompletesOnStart = true;
                loader.OnStartResult = data.Data;
            },
        };

        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image();
        panel.Controls.Add(image);

        var states = new List<ImageSourceLoadState>();
        using var subscription = image.AddSourceLoadStateChangedHandler(
            sender => states.Add(((Image)sender).SourceLoadState));

        image.SourceUri = Uri;

        CollectionAssert.AreEqual(
            new[] { ImageSourceLoadState.Pending, ImageSourceLoadState.Loading, ImageSourceLoadState.Loaded },
            states.ToArray());
        Assert.AreSame(data.Data, image.Source);
    }

    [TestMethod]
    public void SuccessfulLoadAssignsTheImageAndRaisesLoadedAfterItIsSettled()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };

        ImageData? seenInHandler = null;
        var states = new List<ImageSourceLoadState>();
        using var subscription = image.AddSourceLoadStateChangedHandler(sender =>
        {
            var control = (Image)sender;

            if (control.SourceLoadState == ImageSourceLoadState.Loaded)
            {
                seenInHandler = control.Source;
            }

            states.Add(control.SourceLoadState);
        });

        panel.Controls.Add(image);

        var loaded = new TestImage();
        factory.Last!.Complete(loaded.Data);

        Assert.AreEqual(ImageSourceLoadState.Loaded, image.SourceLoadState);
        Assert.AreSame(loaded.Data, image.Source);
        Assert.AreSame(loaded.Data, seenInHandler, "Source is settled before the state change is raised");
        Assert.AreEqual(Uri, image.SourceUri, "the feature's own write does not end the relationship");
        Assert.IsNull(image.SourceLoadError);
        CollectionAssert.AreEqual(
            new[] { ImageSourceLoadState.Loading, ImageSourceLoadState.Loaded },
            states.ToArray());
        Assert.IsFalse(loaded.IsDisposed, "the loaded image is the feature's to keep until it is replaced");
    }

    [TestMethod]
    public void AFailureCarriesTheExceptionAndLeavesTheUriIntact()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        var failure = new InvalidOperationException("the bytes were not an image");

        ImageSourceLoadState? seenInHandler = null;
        using var subscription = image.AddSourceLoadStateChangedHandler(
            sender => seenInHandler = ((Image)sender).SourceLoadState);

        factory.Last!.Fail(failure);

        Assert.AreEqual(ImageSourceLoadState.Failed, image.SourceLoadState);
        Assert.AreSame(failure, image.SourceLoadError);
        Assert.AreEqual(Uri, image.SourceUri, "clearing the URI would lose the only record of what failed");
        Assert.AreEqual(ImageSourceLoadState.Failed, seenInHandler);
        Assert.IsNull(image.Source, "there is no placeholder, so nothing at all is shown");
    }

    [TestMethod]
    public void AFailureShowsThePlaceholderBeforeRaisingTheState()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var placeholder = new TestImage();
        var image = new Image { PlaceholderSource = placeholder.Data, SourceUri = Uri };
        panel.Controls.Add(image);

        ImageData? seenInHandler = null;
        using var subscription = image.AddSourceLoadStateChangedHandler(sender =>
            seenInHandler = ((Image)sender).Source);

        factory.Last!.Fail(new InvalidOperationException("nope"));

        Assert.AreEqual(ImageSourceLoadState.Failed, image.SourceLoadState);
        Assert.AreSame(placeholder.Data, image.Source, "a failure shows the placeholder, not a stale image");
        Assert.AreSame(placeholder.Data, seenInHandler, "Source is settled before the state change is raised");
        Assert.IsFalse(placeholder.IsDisposed, "the placeholder is borrowed and is never disposed");
    }

    [TestMethod]
    public void ThePlaceholderIsShownWhilePendingAndReplacedByTheLoadedImage()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var placeholder = new TestImage();
        var image = new Image { PlaceholderSource = placeholder.Data, SourceUri = Uri };

        Assert.AreSame(placeholder.Data, image.Source, "the placeholder is what a pending control draws");

        panel.Controls.Add(image);

        var loaded = new TestImage();
        factory.Last!.Complete(loaded.Data);

        Assert.AreSame(loaded.Data, image.Source, "the loaded image wins automatically");
        Assert.IsFalse(placeholder.IsDisposed);
    }

    [TestMethod]
    public void AssigningThePlaceholderItAlreadyHoldsDoesNothing()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var placeholder = new TestImage();
        var image = new Image { PlaceholderSource = placeholder.Data, SourceUri = Uri };
        panel.Controls.Add(image);

        int sourceChanges = 0;
        using var sourceSubscription = image.AddSourceChangedHandler(_ => sourceChanges++);

        image.PlaceholderSource = placeholder.Data;

        Assert.AreEqual(0, sourceChanges, "an equal assignment raises nothing");
        Assert.AreSame(placeholder.Data, image.Source);
    }

    [TestMethod]
    public void AssigningSourceFromTheApplicationClearsTheUriAndLeavesTheImageAlone()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        var loaded = new TestImage();
        factory.Last!.Complete(loaded.Data);

        var own = new TestImage();
        image.Source = own.Data;

        Assert.AreEqual(ImageSourceLoadState.None, image.SourceLoadState, "nothing failed: the application took over");
        Assert.IsNull(image.SourceUri);
        Assert.IsNull(image.SourceLoadError);
        Assert.AreSame(own.Data, image.Source, "the image the application assigned stays exactly where it is");
        Assert.IsTrue(loaded.IsDisposed, "the feature releases the image it created and no longer shows");
        Assert.IsFalse(own.IsDisposed, "and never disposes the application's");
    }

    [TestMethod]
    public void AssigningSourceWhileLoadingStopsTheCompletionFromOverwritingIt()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        FakeAssetLoader<ImageData> loader = factory.Last!;

        var own = new TestImage();
        image.Source = own.Data;

        Assert.AreEqual(ImageSourceLoadState.None, image.SourceLoadState);
        Assert.IsTrue(loader.Token.IsCancellationRequested, "there is no longer a load to deliver");

        var late = new TestImage();
        loader.Complete(late.Data);

        Assert.AreEqual(ImageSourceLoadState.None, image.SourceLoadState);
        Assert.AreSame(own.Data, image.Source, "the late result is dropped rather than delivered");
    }

    [TestMethod]
    public void ANullUriEndsTheRelationshipAndTheNextUriStartsAgain()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        var loaded = new TestImage();
        factory.Last!.Complete(loaded.Data);

        Assert.AreEqual(ImageSourceLoadState.Loaded, image.SourceLoadState);

        image.SourceUri = null;

        Assert.AreEqual(ImageSourceLoadState.None, image.SourceLoadState, "the first half of a retry");
        Assert.IsNull(image.Source);
        Assert.IsTrue(loaded.IsDisposed, "the loaded image is released when the URI changes");

        image.SourceUri = Uri;

        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState, "the fresh load started at once");
        Assert.AreEqual(2, factory.Loaders.Count, "a fresh load, not the old one resumed");
    }

    [TestMethod]
    public void ANullUriOnAControlThatHasNoUriDoesNothing()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var image = new Image { SourceUri = null };

        Assert.AreEqual(ImageSourceLoadState.None, image.SourceLoadState);
        Assert.IsNull(image.SourceUri);
        Assert.AreEqual(0, factory.Loaders.Count);
    }

    [TestMethod]
    public void ANewUriReleasesTheOldImageAndNeverPassesThroughNone()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        var first = new TestImage();
        factory.Last!.Complete(first.Data);

        var states = new List<ImageSourceLoadState>();
        using var subscription = image.AddSourceLoadStateChangedHandler(
            sender => states.Add(((Image)sender).SourceLoadState));

        image.SourceUri = OtherUri;

        Assert.IsTrue(first.IsDisposed, "the feature disposes the image it created when the URI changes");
        Assert.AreEqual(Uri, factory.Asked[0]);
        Assert.AreEqual(OtherUri, factory.Asked[1]);
        Assert.AreEqual(2, factory.Loaders.Count);
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState, "the new load started at once");
        CollectionAssert.DoesNotContain(
            states,
            ImageSourceLoadState.None,
            "the control still has a URI, it is only a different one");
    }

    [TestMethod]
    public void ATakeoverFromInsideTheFeatureSourceWriteIsTheFinalAnswer()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        var loaded = new TestImage();
        var own = new TestImage();

        // A SourceChanged handler that replaces the image the feature is writing, which re-enters the
        // setter from inside the feature's own assignment.
        using var takeOver = image.AddSourceChangedHandler(sender =>
        {
            var control = (Image)sender;

            if (ReferenceEquals(control.Source, loaded.Data))
            {
                control.Source = own.Data;
            }
        });

        factory.Last!.Complete(loaded.Data);

        Assert.AreEqual(ImageSourceLoadState.None, image.SourceLoadState, "the application took the control over");
        Assert.IsNull(image.SourceUri);
        Assert.AreSame(own.Data, image.Source);
        Assert.IsTrue(loaded.IsDisposed);
        Assert.IsFalse(own.IsDisposed);
    }

    [TestMethod]
    public void ADeliveredResultWhoseTurnHasNotComeYetDoesNotWriteOverANewUri()
    {
        // On the delivered path the caller's continuation runs inline inside the manager's turn (3.12),
        // so "the result is delivered" and "the control writes it" are one turn and there is no window
        // for a new URI to land in. This test constructs the window the stale comparison covers
        // instead: the manager's turn is taken with the context deliberately not current, so the
        // control's turn is posted behind it, and a URI assigned in between is what the comparison is
        // for. The scene's default has every await resume inline, which is the other shape.
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        using var context = new QueuedSynchronizationContext();

        var image = new Image();
        panel.Controls.Add(image);
        image.SourceUri = Uri;

        FakeAssetLoader<ImageData> delivered = factory.Last!;
        var stale = new TestImage();

        // The completion arrives without the context the load started under, and the turn taken below
        // restores none either - which is what queues the manager's continuation, and the control's
        // behind it, rather than letting either run inline.
        SynchronizationContext.SetSynchronizationContext(null);

        delivered.Complete(stale.Data);

        Assert.AreEqual(1, context.Pending, "the manager's own continuation is queued");

        context.RunNext();

        Assert.AreEqual(
            1,
            context.Pending,
            "the manager has settled the request and this control's turn is queued behind it");
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
        Assert.IsFalse(ReferenceEquals(stale.Data, image.Source), "nothing was written before its turn");

        image.SourceUri = OtherUri;

        context.RunNext();

        Assert.IsFalse(
            ReferenceEquals(stale.Data, image.Source),
            "the delivered result is not written over a control that follows another URI");
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
        Assert.AreEqual(OtherUri, image.SourceUri);
        Assert.IsTrue(stale.IsDisposed, "and the image it carried is released rather than leaked");
        Assert.AreEqual(0, context.Pending);
    }

    [TestMethod]
    public void AStaleCompletionNeverWritesLoaded()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        FakeAssetLoader<ImageData> superseded = factory.Loaders[0];

        image.SourceUri = OtherUri;

        FakeAssetLoader<ImageData> current = factory.Loaders[1];

        var stale = new TestImage();
        superseded.Complete(stale.Data);

        Assert.IsFalse(ReferenceEquals(stale.Data, image.Source), "the superseded request's image is never written");
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
        Assert.AreEqual(OtherUri, image.SourceUri);

        var fresh = new TestImage();
        current.Complete(fresh.Data);

        Assert.AreSame(fresh.Data, image.Source, "the current request is the one that settles the control");
        Assert.AreEqual(ImageSourceLoadState.Loaded, image.SourceLoadState);
    }

    [TestMethod]
    public void ARemovedControlIsCancelledAndResetToPending()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);
        FakeAssetLoader<ImageData> loader = factory.Last!;

        panel.Controls.Remove(image);

        Assert.AreEqual(
            ImageSourceLoadState.Pending,
            image.SourceLoadState,
            "a spinner over a load that is not coming back would be wrong");
        Assert.IsTrue(loader.Token.IsCancellationRequested);
        Assert.AreEqual(Uri, image.SourceUri, "the URI stays: only the load was withdrawn");
    }

    [TestMethod]
    public void AReattachedControlStartsTheSameUriAgain()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        panel.Controls.Remove(image);
        panel.Controls.Add(image);

        Assert.AreEqual(2, factory.Loaders.Count, "AddedToTree is where a detached image starts again");
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
        Assert.AreEqual(Uri, factory.Asked[1]);
    }

    [TestMethod]
    public void DisposingTheControlReleasesTheLoadedImage()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        var loaded = new TestImage();
        factory.Last!.Complete(loaded.Data);

        panel.Controls.Remove(image);
        image.Dispose();

        Assert.IsTrue(loaded.IsDisposed, "the loaded ImageData is disposed at the last moment the control is usable");
    }

    [TestMethod]
    public void DisposingTheControlCancelsALoadInFlight()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);
        FakeAssetLoader<ImageData> loader = factory.Last!;

        panel.Controls.Remove(image);
        image.Dispose();

        Assert.IsTrue(loader.Token.IsCancellationRequested);
    }

    [TestMethod]
    public void ThePriorityFollowsEffectiveInView()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        Window window = scene.Open(panel, 200f, 200f);

        var image = new Image
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 0f, 10f, 10f),
            SourceUri = Uri,
        };
        panel.Controls.Add(image);

        // The first completed pass after subscribing establishes the baseline; only a later pass
        // reports a change.
        ImageSourceScene.Layout(window);

        Assert.IsTrue(image.EffectiveInView);
        Assert.AreEqual(AssetLoadPriority.Visible, factory.Last!.Priority, "what is on screen loads first");

        image.Bounds = new SKRect(0f, 600f, 10f, 610f);
        ImageSourceScene.Layout(window);

        Assert.IsFalse(image.EffectiveInView);
        Assert.AreEqual(
            AssetLoadPriority.Background,
            factory.Last!.Priority,
            "scrolling it off screen moves its load rather than restarting it");
    }

    [TestMethod]
    public void ALoadStartedBeforeTheAnswerExistsTakesThePriorityTheNextPassGivesIt()
    {
        // The configuration an application is in rather than the demo's: the URI is assigned while the
        // tree is unhosted, so the load starts on the first completed layout pass - and that pass, like
        // the one after it, arrives before the window has a client area.
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        var image = new Image
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 0f, 10f, 10f),
            SourceUri = Uri,
        };

        panel.Controls.Add(image);

        // Passes that cannot answer come first: the load starts on the first of them, which is where
        // the priority reads worst.
        Window window = scene.Open(panel, 0f, 0f);

        Assert.AreEqual(1, factory.Loaders.Count, "the first completed pass is where the load starts");
        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
        Assert.IsFalse(image.EffectiveInView, "a host with no client area has nothing to say");
        Assert.AreEqual(
            AssetLoadPriority.Background,
            factory.Last!.Priority,
            "the priority it was submitted with, read at the one moment the answer is missing");

        // The pass that can answer. It establishes the baseline and deliberately raises no change, so
        // the confirmation is the only thing that can correct the load.
        window.ClientSize = new SKSize(200f, 200f);
        ImageSourceScene.Layout(window);

        Assert.IsTrue(image.EffectiveInView);
        Assert.AreEqual(
            AssetLoadPriority.Visible,
            factory.Last!.Priority,
            "the confirming pass is what says the control is on screen, and it raises nothing");
    }

    [TestMethod]
    public void AConfirmationSurvivesAnotherPassThatCannotAnswer()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        var image = new Image
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 0f, 10f, 10f),
            SourceUri = Uri,
        };

        panel.Controls.Add(image);

        Window window = scene.Open(panel, 0f, 0f);
        Assert.AreEqual(AssetLoadPriority.Background, factory.Last!.Priority);

        // A pass that still cannot answer must not consume the confirmation: the answer it would give
        // is the same wrong one, and the pass after it establishes the baseline without raising.
        ImageSourceScene.Layout(window);

        Assert.AreEqual(
            AssetLoadPriority.Background,
            factory.Last!.Priority,
            "nothing moves while the host still has no client area");

        window.ClientSize = new SKSize(200f, 200f);
        ImageSourceScene.Layout(window);

        Assert.IsTrue(image.EffectiveInView);
        Assert.AreEqual(
            AssetLoadPriority.Visible,
            factory.Last!.Priority,
            "the confirmation waited for a pass that could answer rather than being spent on one that could not");
    }

    [TestMethod]
    public void AConfirmationLeavesAControlOffScreenAtBackground()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        var image = new Image
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 600f, 10f, 610f),
            SourceUri = Uri,
        };

        panel.Controls.Add(image);

        Window window = scene.Open(panel, 0f, 0f);
        Assert.AreEqual(1, factory.Loaders.Count);
        Assert.AreEqual(AssetLoadPriority.Background, factory.Last!.Priority);

        window.ClientSize = new SKSize(200f, 200f);
        ImageSourceScene.Layout(window);

        Assert.IsFalse(image.EffectiveInView, "the control sits below the window");
        Assert.AreEqual(
            AssetLoadPriority.Background,
            factory.Last!.Priority,
            "the confirmation reads the answer rather than assuming one: off screen stays background");
    }

    [TestMethod]
    public void ALoadStartedWhereTheAnswerIsAlreadyAvailableIsNotDisturbedByTheConfirmation()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        Window window = scene.Open(panel, 200f, 200f);

        var image = new Image
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 0f, 10f, 10f),
        };

        panel.Controls.Add(image);
        image.SourceUri = Uri;

        Assert.AreEqual(
            AssetLoadPriority.Visible,
            factory.Last!.Priority,
            "the answer is available at submission, so it is the one the load is submitted with");

        // The pass that follows agrees with the submission, so the confirmation is a no-op rather than
        // a second change: the loader's setter guards on equality.
        ImageSourceScene.Layout(window);

        Assert.IsTrue(image.EffectiveInView);
        Assert.AreEqual(AssetLoadPriority.Visible, factory.Last!.Priority);
    }

    [TestMethod]
    public void AUriNothingClaimsFailsRatherThanStayingPending()
    {
        var factory = new RefusingImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        Assert.AreEqual(ImageSourceLoadState.Failed, image.SourceLoadState);
        Assert.IsInstanceOfType<NotSupportedException>(image.SourceLoadError);
        Assert.AreEqual(Uri, image.SourceUri);
    }

    [TestMethod]
    public void AMissingRegistrationFailsWithAnExplanation()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(
            services => services.AddAssetLoaderFactory(factory),
            registerInfrastructure: false);
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        Assert.AreEqual(ImageSourceLoadState.Failed, image.SourceLoadState);
        Assert.IsInstanceOfType<InvalidOperationException>(image.SourceLoadError);
        StringAssert.Contains(image.SourceLoadError!.Message, "AddAssetLoading");
        Assert.AreEqual(0, factory.Loaders.Count);
    }

    [TestMethod]
    public void ALoaderThatThrowsOnTheWayToATaskFailsTheControl()
    {
        var failure = new IOException("the stream is gone");
        var factory = new ImageLoaderFactory { Configure = loader => loader.StartError = failure };
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        Assert.AreEqual(ImageSourceLoadState.Failed, image.SourceLoadState);
        Assert.AreSame(failure, image.SourceLoadError);
        Assert.AreEqual(Uri, image.SourceUri);
    }

    [TestMethod]
    public void AQueuedLoadStaysPendingUntilTheLoaderCanStart()
    {
        var factory = new ImageLoaderFactory { Configure = loader => loader.CanLoadNow = false };
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image { SourceUri = Uri };
        panel.Controls.Add(image);

        Assert.AreEqual(ImageSourceLoadState.Pending, image.SourceLoadState, "queued is not loading");

        var states = new List<ImageSourceLoadState>();
        using var subscription = image.AddSourceLoadStateChangedHandler(
            sender => states.Add(((Image)sender).SourceLoadState));

        factory.Last!.SetCanLoadNow(true);

        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
        CollectionAssert.AreEqual(new[] { ImageSourceLoadState.Loading }, states.ToArray());
    }

    [TestMethod]
    public void TheStateHandlerPairCanBeRemoved()
    {
        var factory = new ImageLoaderFactory();
        using var scene = new ImageSourceScene(services => services.AddAssetLoaderFactory(factory));
        var panel = new Panel();
        scene.Open(panel);

        var image = new Image();
        int changed = 0;
        Action<object> handler = _ => changed++;

        IDisposable subscription = image.AddSourceLoadStateChangedHandler(handler);
        subscription.Dispose();

        panel.Controls.Add(image);
        image.SourceUri = Uri;

        Assert.AreEqual(ImageSourceLoadState.Loading, image.SourceLoadState);
        Assert.AreEqual(0, changed, "a disposed subscription has been removed");
    }
}
