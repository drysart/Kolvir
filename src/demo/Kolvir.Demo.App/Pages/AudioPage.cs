using System.Globalization;
using Kolvir.Audio;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The Audio page: the devices the machine offers and the voices they play.
/// </summary>
internal sealed class AudioPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public AudioPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>The page's readout: the device list, what is playing and how far in it is.</summary>
    private Label? _audioReadout;

    /// <summary>The Audio page's device picker, whose selected index reads into <see cref="_audioDeviceList"/>.</summary>
    private ComboBox? _audioDevices;

    /// <summary>The devices the picker's rows stand for, in the order they were added.</summary>
    private List<AudioDeviceInfo> _audioDeviceList = [];

    /// <summary>The voice the page last started, which is what its readout follows.</summary>
    private Voice? _audioVoice;

    /// <summary>
    /// The timer that refreshes the audio readout while a voice plays.
    /// </summary>
    /// <remarks>
    /// Only while something is playing: a position that is not moving has nothing to report, and a
    /// page that keeps a timer running to say so would be a page that keeps the loop awake.
    /// </remarks>
    private Timer? _audioPositionTimer;

    private Voice? AudioVoice => _audioVoice is { IsFinished: false, IsLost: false } ? _audioVoice : null;

    /// <summary>
    /// Reads the device list once there is a window to ask through: the page is built before there is
    /// an application to ask, and the list is a live reading of what the machine offers.
    /// </summary>
    /// <param name="window">The window the sample is displayed in.</param>
    internal override void OnWindowAttached(Window window) => RefreshAudioDevices();


    /// <summary>
    /// The Audio page: pick an output, play a generated sound on it, and watch the position the voice
    /// reports while it plays.
    /// </summary>
    /// <remarks>
    /// The page exists to make three things visible that no test can. Which devices this machine
    /// offers and which one is the default, because that list comes from a live sound server. Whether
    /// a sound is actually heard, since a working call and an audible one are different claims. And
    /// what the position does while it plays, which is the promise an application builds a progress
    /// bar or a synchronised animation on.
    /// </remarks>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Audio"));

        card.Controls.Add(Caption("Output device"));
        card.Controls.Add(CreateAudioDevicePicker());

        var buttons = Row(34f);
        buttons.Controls.Add(AudioButton("Click", () => PlayAudio(DemoAudio.Click(), loop: false)));
        buttons.Controls.Add(AudioButton("Tone", () => PlayAudio(DemoAudio.Tone(), loop: false)));
        buttons.Controls.Add(AudioButton("Sweep", () => PlayAudio(DemoAudio.Sweep(), loop: false)));
        buttons.Controls.Add(AudioButton("Loop sweep", () => PlayAudio(DemoAudio.Sweep(), loop: true)));
        buttons.Controls.Add(AudioButton("Pause", () => AudioVoice?.Pause()));
        buttons.Controls.Add(AudioButton("Resume", () => AudioVoice?.Resume()));
        buttons.Controls.Add(AudioButton("Stop", () => AudioVoice?.Stop()));
        buttons.Controls.Add(AudioButton("Refresh devices", RefreshAudioDevices));
        card.Controls.Add(buttons);

        var readout = new Label(string.Empty)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 64f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        _audioReadout = readout;
        card.Controls.Add(readout);

        card.Controls.Add(new Label(
            "Every engine has a device, even one with no sound card: when there is nothing to play " +
            "through the list ends with \u201cNo audio device\u201d, which counts down in real time and " +
            "reports the end of the sound without producing a sample. Loop is the platform's own " +
            "where it has one - XAudio2 repeats the buffer, AudioQueue re-enqueues it, PulseAudio " +
            "rewinds - so a loop costs nothing per pass. The position is computed from the clock " +
            "rather than counted by the audio thread, which is why it stays right when a callback " +
            "is late, and why pausing it freezes it.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 92f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
        });

        RefreshAudioDevices();

        return card;
    }

    /// <summary>A button that sits beside the ones before it, on the Audio page's row.</summary>
    private Button AudioButton(string text, Action action)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        button.AddClickHandler(_ => action());
        return button;
    }

    /// <summary>Builds the device picker and keeps its rows in step with <see cref="_audioDeviceList"/>.</summary>
    private ComboBox CreateAudioDevicePicker()
    {
        var picker = new ComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            EmptyText = "Audio is not enabled for this application",
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        picker.AddSelectedIndexChangedHandler(_ => RefreshAudioReadout());
        _audioDevices = picker;

        return picker;
    }

    /// <summary>Re-reads the device list, which is what a plug-in or a changed system default needs.</summary>
    private void RefreshAudioDevices()
    {
        if (_audioDevices is null)
        {
            return;
        }

        var previous = SelectedAudioDevice();
        var engine = Shell.AudioEngine;

        _audioDeviceList = [.. engine?.Devices ?? []];
        _audioDevices.Items.Clear();

        foreach (var device in _audioDeviceList)
        {
            _audioDevices.Items.Add(DescribeAudioDevice(device));
        }

        // Hold the choice across a refresh when the device is still there, and fall back to the
        // default when it is not: a device that vanished is the case a refresh exists for.
        int index = previous is null ? -1 : _audioDeviceList.FindIndex(device => device.Id == previous.Id);

        if (index < 0)
        {
            index = engine is null ? -1 : _audioDeviceList.FindIndex(device => device.IsDefault);
        }

        if (index >= 0)
        {
            _audioDevices.SelectedIndex = index;
        }

        RefreshAudioReadout();
    }

    /// <summary>The device the picker is showing, or null when it is showing none.</summary>
    private AudioDeviceInfo? SelectedAudioDevice()
    {
        int index = _audioDevices?.SelectedIndex ?? -1;

        return index >= 0 && index < _audioDeviceList.Count ? _audioDeviceList[index] : null;
    }

    /// <summary>Plays one generated sound on the chosen device, replacing whatever was playing.</summary>
    private void PlayAudio(byte[] wav, bool loop)
    {
        if (Shell.AudioEngine is not { } engine)
        {
            RefreshAudioReadout();
            return;
        }

        // One voice at a time: the page is a demonstration, and a second sound under the first makes
        // the readout ambiguous rather than the demonstration richer.
        _audioVoice?.Dispose();

        var clip = engine.Load(new MemoryStream(wav));
        var voice = clip.Start(SelectedAudioDevice() ?? engine.DefaultDevice, loop);
        voice.AddFinishedHandler(_ => RefreshAudioReadout());

        _audioVoice = voice;
        _audioPositionTimer ??= CreateAudioPositionTimer();
        _audioPositionTimer.Start();
        RefreshAudioReadout();
    }

    /// <summary>Refreshes the readout while a voice is playing, and stops when nothing is moving.</summary>
    private Timer CreateAudioPositionTimer()
    {
        var timer = new Timer(TimeSpan.FromMilliseconds(50));
        timer.AddTickHandler(_ =>
        {
            RefreshAudioReadout();

            if (AudioVoice is null)
            {
                timer.Stop();
            }
        });
        return timer;
    }

    /// <summary>Reports what the engine bound to, what is playing, and how far in it is.</summary>
    private void RefreshAudioReadout()
    {
        if (_audioReadout is null)
        {
            return;
        }

        if (Shell.AudioEngine is not { } engine)
        {
            _audioReadout.Text = "This application did not call AddAudio, so there is no engine to report.";
            return;
        }

        var silent = engine.Devices.Count(device => device.IsNull) == engine.Devices.Count;
        var voice = _audioVoice;

        var state = voice switch
        {
            null => "nothing playing",
            { IsPaused: true } paused =>
                $"paused at {Seconds(paused.Position)} of {Seconds(paused.Duration)}",
            { IsFinished: true } finished => $"finished after {Seconds(finished.Duration)}",
            { IsLost: true } => "stopped: the device went away",
            { Loops: true } looping =>
                $"looping, {Seconds(looping.Position)} into {Seconds(looping.Duration)}",
            _ => $"playing {Seconds(voice.Position)} of {Seconds(voice.Duration)}",
        };

        _audioReadout.Text =
            $"{engine.Path} \u00b7 {engine.Devices.Count} device" +
            $"{(engine.Devices.Count == 1 ? string.Empty : "s")}" +
            $"{(silent ? " (no sound card, so the silent one is in use)" : string.Empty)}\n" +
            $"Default: {engine.DefaultDevice.Name}" +
            $"{(engine.DefaultDevice.IsNull && engine.DefaultDevice.Reason is { Length: > 0 } reason ? $" - {reason}" : string.Empty)}\n" +
            $"Voice: {state}";

        static string Seconds(TimeSpan value) =>
            value.TotalSeconds.ToString("0.00", CultureInfo.CurrentCulture) + " s";
    }

    /// <summary>One row of the picker: the name, and what makes this device worth choosing or not.</summary>
    private static string DescribeAudioDevice(AudioDeviceInfo device) => device switch
    {
        { IsNull: true } => $"{device.Name} ({device.Reason ?? "silent"})",
        { IsDefault: true } => $"{device.Name} (default)",
        _ => device.Name,
    };
}
