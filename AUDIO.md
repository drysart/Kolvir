# Audio

A cross-platform way to play sound: load a clip, play it, stop it, and know where it is in its own
duration. It is the audio counterpart of `Image` - the same shape of object, the same clock rule, the
same ownership rule - with a device underneath where the picture had a drawing surface.

Scope is deliberate and small: **UI sounds**. One-shot playback, looping, per-voice volume, pause and
stop, a position that can drive a progress bar, and a list of output devices to choose between. Not in
this feature, and not planned on the strength of it: recording and capture, a mixing graph or any DSP,
MIDI, and streaming music with seek. If music playback arrives it arrives as playback of whole decoded
clips first, because that is what the rest of this document already has to solve.

## 1. The rung: 3

`Kolvir.Feature.Audio`, one package, the four-folder layout of `FEATURE-COST-POLICY.md` section 4.

Rung 1 is unavailable for the same reason it was for badging: sound belongs to an application rather
than to a control, `Application` is retained by every application, and there is no call site whose
mere existence keeps the code out of the deliverable.

Rung 2 is wrong for a reason badging only half shares. A device and its thread have to exist before a
sound is played, so the work would have to happen at application startup, and rung 2 puts that work in
`Kolvir.Backend.<platform>` - which every application of that platform ships whether it makes a noise
or not. Worse than the bytes: audio needs native interop on all three platforms, and the backend
assemblies are already large, correctness-critical code that would be assembled for a feature most
applications never turn on.

So the feature takes rung 3 and the package is the opt-in. Nothing in `Kolvir` and nothing in a
`Kolvir.Backend.*` project names it: audio needs no window, so the package reaches the platform
directly and the backends keep their existing seams untouched. That is a stronger separation than
badging achieved, where the Win32 half still needed a message filter.

The surface consequence is section 5's rule again: a separate assembly cannot add members to
`Application`, so everything is an extension block.

## 2. What each platform actually offers

| | Windows | macOS | Linux (GTK4) |
|---|---|---|---|
| Enumerate outputs | `IMMDeviceEnumerator` (id, friendly name, default role) | HAL: `AudioObjectGetPropertyData` on `kAudioHardwarePropertyDevices`, filtered to devices with output streams | `pa_context_get_sink_info_list` |
| Default device | `eConsole`/`eMultimedia` role | `kAudioHardwarePropertyDefaultOutputDevice` | `default_sink_name` from `pa_context_get_server_info` |
| Play to a chosen endpoint | `IXAudio2::CreateMasteringVoice` with the MMDevice id | `AudioQueue` with `kAudioQueueProperty_CurrentDevice` set to the device UID | `pa_simple_new(dev: sinkName, ...)` |
| Mixing and resampling | XAudio2 gives both, plus voice limits | CoreAudio's converter | neither: the package converts to the sink's rate itself |
| Decode | Media Foundation, or ours | AVFoundation/AudioToolbox, or ours | GStreamer, or ours |
| With no sound server | normal on any desktop | normal | **the common case on a bare VM or a container** |

Measured, not assumed - filled in as section 9's spikes run:

- **XAudio2 2.9 has no device enumeration.** `IXAudio2::GetDeviceCount`/`GetDeviceDetails` are the
  XAudio2 2.8 API; 2.9 deprecated them and the documented replacement is MMDevice. So enumeration and
  playback come from two different APIs, and the id that ties them together is the MMDevice id
  string that `CreateMasteringVoice` accepts.
- **`AVAudioEngine` cannot choose an output.** It always renders to the system default; per-device
  selection on macOS means CoreAudio. `AudioQueue` takes `kAudioQueueProperty_CurrentDevice` as a
  device *UID* (a `CFString`), not a device id, which is a different identifier from the one
  enumeration returns.
- **`pa_simple` cannot enumerate but can target.** The simple API has no sink list, so enumeration
  needs the asynchronous `pa_context` and its main loop, while playback can stay on the blocking
  simple API and still pick the sink.
- **Everything here is in-box except Linux's `libpulse`.** That is the one runtime dependency, and the
  one place a missing library has to read as "no audio device" rather than as an exception.

## 3. The API

```csharp
services.AddAudio();                                       // the opt-in

var audio = services.GetRequiredService<IAudioEngine>();

foreach (var device in audio.Devices)                      // real devices, then the null device
{
    Console.WriteLine($"{device.Name} ({(device.IsDefault ? "default" : "available")})");
}

var clip = audio.Load(stream);                             // AudioClip: duration, channels, rate
clip.Play();                                               // default device, fire and forget
clip.Play(volume: 0.4f);

using var voice = clip.Start(device: device, loop: true);
voice.Volume = 0.6f;
voice.Pause();
voice.Stop();

var where = voice.Position;                                // where in the clip, right now
```

The rules, each one already the rule somewhere else in the library:

- **The position is computed, never accumulated.** A voice records the anchor it started from and the
  timestamp it started at; `Position` is that anchor plus the elapsed clock, minus pauses. The same
  rule as `ImageData`, for the same reason: a progress bar driven by it stays right even when the
  audio thread is late, and a paused voice is frozen at a stored position rather than at wherever a
  counter happened to be.
- **Nothing calls the application on the audio thread.** `Finished` and device changes are posted
  through `Application.BeginInvoke`, which is the rule `Timer` follows. The audio thread allocates
  nothing, takes no lock the UI thread holds, and never touches the control tree.
- **The application owns the clip; the engine owns the voices.** Assigning a clip to two voices is
  ordinary, and disposing a clip that is playing is the application's mistake - the same contract as
  `ImageData` and `PictureBox.Image`.
- **A voice belongs to the device it started on.** If that endpoint disappears the voice goes `Lost`
  and stops; it does not migrate to the new default. `Play()` with no device resolves the default at
  the moment of the call.
- **Decoding is validation.** `Load` throws on a stream it cannot read, on every machine, whether or
  not there is anything to play it through. A machine with no audio must not be a machine where bad
  files go unnoticed.
- **`audio.Path` is the diagnostic**, in the same form as `GraphicsPath`: `xaudio2: Speakers
  (Realtek)`, `coreaudio: LG UltraFine Display Audio`, or `audio (no sound server)`.

## 4. Devices, and the null device

`Devices` is never empty and `DefaultDevice` is never null. Both invariants are kept by a
`NullAudioDevice` that is always in the list and becomes the default when no real device exists. The
alternative - `null` for the default and an empty list - would put the same `if` in every application
that plays a sound, and the interesting case is not "no audio device" anyway: it is "no sound server
on this Linux machine", which is the normal state of a VM, a container and a CI runner.

The rules that make it a device rather than a stub:

- **It is clock-driven, not instant.** A voice on the null device takes exactly as long as it would on
  speakers: `Position` advances, `Loop` wraps at the duration, and `Finished` arrives when the clip
  would have ended. A device that completed immediately would break the one thing applications do
  with audio - play the next sound when this one finishes - on precisely the machines least likely to
  be watched.
- **It is honest about why.** `AudioDeviceInfo` carries `IsNull` and a `Reason` string - `no sound
  server`, `no output device`, `permission denied` - and `Path` reports it. Silence that cannot be
  explained is the failure mode this pattern risks.
- **It is a real implementation.** It is not a mock for tests: it ships, applications may select it
  deliberately (a silent mode, an offline render), and a test that runs against it is testing the
  same code path an application without sound runs.
- **It never produces samples.** Voices on it advance a timeline; nothing is mixed and nothing is
  written, so it costs a timer per engine rather than a thread per device.

## 5. What the platforms cannot do

Stated in the feature's own terms, the way the badge feature states its own:

- **Linux with no sound server** - no real device; the null device is the default and `Reason` says
  so. Playback is silent and correct; nothing throws.
- **Linux with no `libpulse`** - the same outcome, with `Reason` naming the missing library. The
  library is loaded with `NativeLibrary.TryLoad`, so a machine without it degrades rather than
  crashes at type initialisation.
- **A device removed mid-playback** - the voice stops and reports `Lost`. It does not follow the new
  default. Windows and PulseAudio both report device changes; macOS reports them through the HAL
  property listeners.
- **Per-voice effects, panning, per-device latency reporting** - not offered anywhere. A feature that
  exists on one platform and not the others is the kind of asymmetry that reads as broken.

## 6. Layout

```
Kolvir.Feature.Audio/
    common/     IAudioEngine, AudioDeviceInfo, AudioClip, Voice, the timeline, WAV decoding
    windows/    MMDevice enumeration, XAudio2 playback
    macos/      CoreAudio enumeration, AudioQueue playback
    linux/      libpulse enumeration and playback
```

`common/` holds everything that is not a platform call, including the null device, the voice timeline
and the WAV decoder, because all three are ordinary code. The platform folders hold `IPlatformAudio`:
enumerate, open an endpoint at a format, write frames, close. That is a small interface, and the
voice timeline, looping, volume and the null device are written once against it.

The package does not reference SkiaSharp and does not depend on a display: it references `Kolvir` for
`Application`, `TimeProvider` and the service-extensions pattern, and nothing else.

## 7. Testing

- **A virtual device driven by the test clock.** `TimeProvider` is already the library's clock, so a
  test plays a two-second clip, advances the clock, and asserts the position, the loop wrap and the
  arrival of `Finished` without sleeping. The null device is this device with the mixing stage
  removed, so the same tests cover it.
- **A capturing device for the samples.** Internal to the test project: the same timeline, with the
  frames it would have written retained, so volume scaling and WAV decoding are checked on numbers
  rather than on ears.
- **One test per invariant that the null device exists for**: `Devices` never empty, `DefaultDevice`
  never null, a queued sequence of clips takes the same wall-clock time with no devices as with one,
  and a corrupt stream fails identically either way.
- **No CI job opens a device.** CI has no sound server on any leg, so the platform halves are checked
  by a harness on `mac`, `linux` and `windows` and recorded in section 8, the same way the badge
  feature recorded its platform runs. The demo gains an **Audio** page - a tone, a sweep, a looping
  clip, a device picker and a position readout - which is how a human checks the half no test can.

## 8. Decisions

| Decision | Why, and what was rejected |
|---|---|
| Rung 3 package | Section 1. Rejected: a seam in `Kolvir.Backend.*` (every application pays, and the backends are the wrong place for a feature most apps never enable). |
| v1 is UI sounds only | One-shot, loop, volume, position, pause/stop. Rejected for now: streaming music with seek, mixing graph, DSP, capture, MIDI. |
| Devices are enumerable and selectable | Requested, and it is cheaper to do now than to retrofit: it decides the API shape (`Start(device:)`), and on Linux it forces the asynchronous `pa_context` for enumeration rather than the simple API alone. |
| A `NullAudioDevice`, always present and the default when nothing else is | Never-empty `Devices`, never-null `DefaultDevice`, no `if` at every call site, and a silent machine is the common case on Linux VMs and CI. It is clock-driven so timing-dependent application logic keeps working, and it carries a `Reason` so silence is explainable. |
| No third-party audio library | Rejected SDL2 (brings a competing window and event loop), miniaudio (a native blob in the deliverable), NAudio (Windows only). Everything used is in-box except `libpulse`, which is a system library rather than a shipped one. |
| WAV in managed code first | Zero native cost and it covers UI sounds. Vorbis/MP3 later, as vendored single-file decoders in this package rather than by dragging in Media Foundation or GStreamer for a beep. |
| Position computed from a clock | Section 3. Rejected: a frame counter incremented by the audio callback, which is wrong the moment a callback is late and wrong in a different way when the clock is a test's. |
| No hotplug events in v1 | A snapshot list and `Refresh()`. The change notifications exist on all three platforms and are a follow-up, not a v1 requirement. |

## 9. Steps

| # | Step | State |
|---|---|---|
| 1 | Spike Linux: `libpulse` enumeration of sinks and playback to a chosen sink on `linux` (pipewire-pulse) | |
| 2 | Spike macOS: CoreAudio enumeration and `AudioQueue` playback to a chosen device on `mac` | |
| 3 | Spike Windows: MMDevice enumeration and `CreateMasteringVoice` on an id, including the no-device path | |
| 4 | `common/`: `IAudioEngine`, `AudioDeviceInfo`, `AudioClip`, `Voice`, the timeline, the null device, WAV decoding, `AddAudio()` | |
| 5 | `linux/`: `IPlatformAudio` over libpulse, with `NativeLibrary.TryLoad` and the reason strings | |
| 6 | `windows/`: MMDevice enumeration and XAudio2 voices | |
| 7 | `macos/`: CoreAudio enumeration and AudioQueue output | |
| 8 | Tests: the timeline, the null device's invariants, WAV decoding, volume | |
| 9 | Demo **Audio** page; `README.md` section; `FEATURE-COST-POLICY.md` worked example | |

## 10. Risks

- **Linux is where this will look broken.** A VM or a container has no sound server, so the honest
  output is "no output device" - which is why the null device and its reason string exist, and why
  the demo page has to say which device is live rather than pretending.
- **`libpulse` absent changes the failure mode.** It has to be `TryLoad` plus a reason, never a
  `DllNotFoundException` out of a static initialiser.
- **Device selection on macOS is the awkward corner.** `AVAudioEngine` cannot do it, so the spike has
  to prove `AudioQueue` plus `kAudioQueueProperty_CurrentDevice` works with the UID enumeration
  returns, on a machine with two output devices.
- **Latency is not a goal and will not be one.** Shared-mode output is 10-40 ms; a UI sound wants
  under a hundred. An application that needs tighter than that is out of scope, and saying so early
  is cheaper than being asked to add WASAPI exclusive mode later.
- **A silent success can hide a misconfiguration.** An application that expected sound and got none
  has only `Path` and `Reason` to go on, so both have to be exact rather than generic.
