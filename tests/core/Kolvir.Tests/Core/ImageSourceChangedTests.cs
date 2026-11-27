using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The source-changed notification on <see cref="Image"/>: what an assignment raises, what it does
/// not, and what a handler may do from inside it. It is the signal an application that owns the
/// source uses to tell its own assignment from the control's internal use of the property.
/// </summary>
[TestClass]
[TestCategory("Core")]
public sealed class ImageSourceChangedTests
{
    [TestMethod]
    public void AssigningASourceRaisesOnceWithTheControlAsSender()
    {
        using var image = new Image();
        using var data = StillImage();
        var senders = new List<object>();

        image.AddSourceChangedHandler(sender => senders.Add(sender));
        image.Source = data;

        Assert.AreEqual(1, senders.Count, "one assignment is one notification");
        Assert.AreSame(image, senders[0], "the sender is the image control");
    }

    [TestMethod]
    public void AssigningTheSameInstanceAgainRaisesNothing()
    {
        using var image = new Image();
        using var data = StillImage();
        int raised = 0;

        image.Source = data;
        image.AddSourceChangedHandler(_ => raised++);
        image.Source = data;

        Assert.AreEqual(0, raised, "the reference did not change, so the setter returns early");
    }

    [TestMethod]
    public void AssigningNullAfterAValueRaises()
    {
        using var image = new Image();
        using var data = StillImage();
        int raised = 0;
        bool? sourceSeenByHandler = null;

        image.Source = data;
        image.AddSourceChangedHandler(_ =>
        {
            raised++;
            sourceSeenByHandler = image.Source is not null;
        });

        image.Source = null;

        Assert.AreEqual(1, raised, "clearing the source is an assignment like any other");
        Assert.IsFalse(sourceSeenByHandler, "the handler sees the source already cleared");
    }

    [TestMethod]
    public void TheHandlerSeesTheNewValueAlreadyInSource()
    {
        using var image = new Image();
        using var data = StillImage();
        ImageData? seen = null;

        image.AddSourceChangedHandler(_ => seen = image.Source);
        image.Source = data;

        Assert.AreSame(data, seen, "the notification is raised after the field is assigned");
    }

    [TestMethod]
    public void AHandlerThatAssignsSourceFromInsideIsSafe()
    {
        using var image = new Image();
        using var first = StillImage();
        using var second = StillImage();
        int raised = 0;

        image.AddSourceChangedHandler(_ =>
        {
            raised++;

            // Deliberately re-enter the setter from inside the notification.
            if (ReferenceEquals(image.Source, first))
            {
                image.Source = second;
            }
        });

        image.Source = first;

        Assert.AreEqual(2, raised, "the inner assignment raises a second notification");
        Assert.AreSame(second, image.Source, "and the inner value is what survives");
    }

    [TestMethod]
    public void DisposingTheTokenUnsubscribes()
    {
        using var image = new Image();
        using var data = StillImage();
        int raised = 0;

        var token = image.AddSourceChangedHandler(_ => raised++);
        token.Dispose();

        image.Source = data;

        Assert.AreEqual(0, raised, "the token removed the handler");
    }

    [TestMethod]
    public void AnExceptionFromAHandlerPropagates()
    {
        using var image = new Image();
        using var data = StillImage();
        image.AddSourceChangedHandler(_ => throw new InvalidOperationException("boom"));

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => image.Source = data);

        Assert.AreEqual("boom", thrown.Message);
        Assert.AreSame(data, image.Source, "the assignment had already completed when the handler ran");
    }

    /// <summary>A one-pixel still image whose data owns its picture, so the data is the only using.</summary>
    private static ImageData StillImage()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 1f, 1f));
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(0f, 0f, 1f, 1f, paint);
        return ImageData.FromPicture(recorder.EndRecording(), disposePictureWithData: true);
    }
}
