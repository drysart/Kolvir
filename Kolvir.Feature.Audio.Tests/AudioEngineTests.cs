using Kolvir.Audio;
using Kolvir.Undo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Audio.Tests;

/// <summary>
/// The devices an engine reports, what a voice does on the silent device, and what reaches the
/// platform half when a voice plays on a real one.
/// </summary>
/// <remarks>
/// The null device's promise is a timing promise - a voice on it finishes when it would have sounded
/// - so these tests drive the clock and the timer rather than waiting for either. The other half is
/// the seam: which device a voice opens on, and which of the voice's calls reach the platform.
/// </remarks>
[TestClass]
public sealed class AudioEngineTests
{
    private TestApplicationBackend _backend = null!;
    private Application _application = null!;
    private AudioTestClock _clock = null!;

    [TestInitialize]
    public void Setup()
    {
        _clock = new AudioTestClock();
        UndoClock.Current = _clock;
        _backend = new TestApplicationBackend();
        _application = new Application(_backend);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _application.Dispose();
        UndoClock.Current = TimeProvider.System;
    }

    [TestMethod]
    public void AnEngineWithNoPlatformStillHasADeviceAndADefault()
    {
        // The whole point of the null device: no call site needs to check for the absence of audio.
        using var engine = new AudioEngine(platform: null);

        Assert.AreEqual(1, engine.Devices.Count);
        Assert.IsTrue(engine.Devices[0].IsNull);
        Assert.IsTrue(engine.DefaultDevice.IsNull);
        Assert.AreEqual("audio (no platform)", engine.Path);
    }

    [TestMethod]
    public void RealDevicesComeFirstAndTheSilentOneIsLast()
    {
        var platform = new RecordingPlatform();
        platform.Devices.Add(new PlatformDevice("speakers", "Speakers", IsDefault: true));
        platform.Devices.Add(new PlatformDevice("headset", "Headset", IsDefault: false));

        using var engine = new AudioEngine(platform);

        Assert.AreEqual(3, engine.Devices.Count);
        Assert.AreEqual("Speakers", engine.Devices[0].Name);
        Assert.IsTrue(engine.Devices[0].IsDefault);
        Assert.AreEqual("Headset", engine.Devices[1].Name);
        Assert.IsTrue(engine.Devices[2].IsNull, "the silent device is last, so a picker reads real outputs first");
        Assert.AreEqual("Speakers", engine.DefaultDevice.Name, "with a real device there is no reason to be silent");
    }

    [TestMethod]
    public void TheReasonForSilenceReachesTheSilentDevice()
    {
        // A machine with no sound server is the common case on Linux, and a user who is told "no
        // output device" when the server is simply not running has been told the wrong thing.
        var platform = new RecordingPlatform { EmptyReason = "no sound server" };

        using var engine = new AudioEngine(platform);

        Assert.AreEqual("no sound server", engine.NullDevice.Reason);
    }

    [TestMethod]
    public void AVoiceOnTheSilentDeviceFinishesWhenItWouldHaveSounded()
    {
        using var engine = new AudioEngine(platform: null);
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        var voice = clip.Start();
        var finished = 0;
        voice.AddFinishedHandler(_ => finished++);

        using var error = _application.AddUnhandledExceptionHandler(
            (_, args) => Assert.Fail($"a queued action threw: {args.Exception}"));

        _clock.Advance(99);
        _backend.Timer.Fire();
        _backend.Drain();
        Assert.IsFalse(voice.IsFinished, "a 100 ms clip is not over at 99 ms");

        _clock.Advance(1);
        _backend.Timer.Fire();
        _backend.Drain();

        Assert.IsTrue(voice.IsFinished, "the timeline fired at the clip's end");
        Assert.AreEqual(1, finished, "the handler runs once");
        Assert.AreEqual(clip.Duration, voice.Position, "a finished voice stops at the end of the clip");
    }

    [TestMethod]
    public void ALoopingVoiceWrapsInsideTheClipInsteadOfFinishing()
    {
        using var engine = new AudioEngine(platform: null);
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        var voice = clip.Start(loop: true);

        _clock.Advance(150);
        _backend.Timer.Fire();
        _backend.Drain();

        Assert.IsFalse(voice.IsFinished, "a looping voice has no end to reach");
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), voice.Position, "the position wraps at the end of the clip");
    }

    [TestMethod]
    public void PausingFreezesThePositionAndResumingContinuesIt()
    {
        using var engine = new AudioEngine(platform: null);
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        var voice = clip.Start();

        _clock.Advance(40);
        voice.Pause();
        _clock.Advance(1000);

        Assert.IsTrue(voice.IsPaused);
        Assert.AreEqual(TimeSpan.FromMilliseconds(40), voice.Position, "the clock ran but the voice did not");

        voice.Resume();
        _clock.Advance(10);

        Assert.AreEqual(TimeSpan.FromMilliseconds(50), voice.Position);
    }

    [TestMethod]
    public void AVoiceTheApplicationStoppedDoesNotReportThatItFinished()
    {
        // "Finished" means the clip played out, which is what a chained sequence reacts to. A voice
        // the application stopped is not something it needs to be told about.
        using var engine = new AudioEngine(platform: null);
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        var voice = clip.Start();
        var finished = 0;
        voice.AddFinishedHandler(_ => finished++);

        voice.Stop();
        _clock.Advance(1000);
        _backend.Timer.Fire();
        _backend.Drain();

        Assert.AreEqual(0, finished);
        Assert.IsFalse(voice.IsFinished);
    }

    [TestMethod]
    public void VolumeIsClampedRatherThanTrusted()
    {
        using var engine = new AudioEngine(platform: null);
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[8], sampleRate: 8000)));

        var voice = clip.Start(volume: 4f);
        Assert.AreEqual(1f, voice.Volume);

        voice.Volume = -2f;
        Assert.AreEqual(0f, voice.Volume);

        voice.Volume = float.NaN;
        Assert.AreEqual(0f, voice.Volume, "a NaN volume would silence the whole mix, so it is not passed on");
    }

    [TestMethod]
    public void AVoiceOnARealDeviceIsOpenedOnItAndDrivenThroughTheSeam()
    {
        var platform = new RecordingPlatform();
        platform.Devices.Add(new PlatformDevice("speakers", "Speakers", IsDefault: true));
        platform.Devices.Add(new PlatformDevice("headset", "Headset", IsDefault: false));

        using var engine = new AudioEngine(platform);
        var headset = engine.Devices.First(device => device.Id == "headset");
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        var voice = clip.Start(headset, loop: true, volume: 0.5f);

        Assert.AreEqual(1, platform.Voices.Count);
        var opened = platform.Voices[0];
        Assert.AreEqual("headset", opened.Device.Id);
        Assert.IsTrue(opened.Loops, "the platform loops natively, so the engine asks it to");
        Assert.AreEqual(0.5f, opened.Volume);
        Assert.AreEqual(1, opened.Starts);

        voice.Volume = 0.25f;
        Assert.AreEqual(0.25f, opened.Volume, "a volume change reaches the device, not a field");

        voice.Pause();
        Assert.AreEqual(1, opened.Pauses);

        voice.Resume();
        Assert.AreEqual(1, opened.Resumes);

        voice.Dispose();
        Assert.IsTrue(opened.IsDisposed, "disposing the voice releases the stream");
    }

    [TestMethod]
    public void TheDevicesEndOfTheClipReachesTheApplicationThroughTheUiThread()
    {
        var platform = new RecordingPlatform();
        platform.Devices.Add(new PlatformDevice("speakers", "Speakers", IsDefault: true));

        using var engine = new AudioEngine(platform);
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        var voice = clip.Start();
        var finished = 0;
        voice.AddFinishedHandler(_ => finished++);

        platform.Voices[0].ReportEnded();

        Assert.IsTrue(voice.IsFinished, "the state is set where the platform reported it");
        Assert.AreEqual(0, finished, "but the handler waits for the UI thread");

        _backend.Drain();
        Assert.AreEqual(1, finished);
    }

    [TestMethod]
    public void ADeviceThatWentAwayIsReportedAsLost()
    {
        var platform = new RecordingPlatform();
        platform.Devices.Add(new PlatformDevice("speakers", "Speakers", IsDefault: true));

        using var engine = new AudioEngine(platform);
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        var voice = clip.Start();
        platform.Voices[0].ReportEnded(lost: true);

        Assert.IsTrue(voice.IsLost);
        Assert.IsFalse(voice.IsFinished, "a lost voice did not play out");
    }

    [TestMethod]
    public void ADeviceFromAnEarlierRefreshFallsBackToTheDefault()
    {
        var platform = new RecordingPlatform();
        platform.Devices.Add(new PlatformDevice("speakers", "Speakers", IsDefault: true));

        using var engine = new AudioEngine(platform);
        var stale = engine.Devices[0];
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        platform.Devices.Clear();
        platform.Devices.Add(new PlatformDevice("headset", "Headset", IsDefault: true));
        engine.Refresh();

        var voice = clip.Start(stale);

        Assert.AreEqual("headset", voice.Device.Id, "a device that is no longer listed is not played on");
    }

    [TestMethod]
    public void PlayingWithoutKeepingTheVoiceStillReleasesIt()
    {
        var platform = new RecordingPlatform();
        platform.Devices.Add(new PlatformDevice("speakers", "Speakers", IsDefault: true));

        using var engine = new AudioEngine(platform);
        var clip = engine.Load(new MemoryStream(Wav.Pcm(new short[800], sampleRate: 8000)));

        clip.Play();

        Assert.AreEqual(1, platform.Voices.Count, "the sound is playing");
        Assert.IsFalse(platform.Voices[0].IsDisposed, "and nothing has finished yet");

        platform.Voices[0].ReportEnded();
        _backend.Drain();

        Assert.IsTrue(platform.Voices[0].IsDisposed, "a voice nobody kept releases itself when it ends");
    }
}
