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
| Default device | `eConsole`/`eMultimedia` role | `kAudioHardwarePropertyDefaultOutputDevice` | `pa_context_get_sink_info_by_name("@DEFAULT_SINK@")` |
| Play to a chosen endpoint | WASAPI: `IMMDevice::Activate(IAudioClient)` on the MMDevice id | `AudioQueue` with `kAudioQueueProperty_CurrentDevice` set to the device UID | `pa_simple_new(dev: sinkName, ...)` |
| Mixing and resampling | `AUTOCONVERTPCM` + `SRC_DEFAULT_QUALITY` convert to the mix format; mixing is ours | AudioQueue's converter; mixing is ours | neither: the package converts to the sink's rate itself |
| Decode | Media Foundation, or ours | AVFoundation/AudioToolbox, or ours | GStreamer, or ours |
| With no sound server | normal on any desktop | normal | **the common case on a bare VM or a container** |

Measured, not assumed - all three spikes ran on `linux`, `mac` and `windows`; the runs are recorded
in section 9.

- **XAudio2 2.9 has no device enumeration.** `IXAudio2::GetDeviceCount`/`GetDeviceDetails` are the
  XAudio2 2.8 API; 2.9 deprecated them, and the header confirms slots 11 and 12 are now
  `GetPerformanceData` and `SetDebugConfiguration`. So enumeration has to come from MMDevice whatever
  plays the sound.
- **XAudio2's voice interfaces are not reachable from .NET without a wrapper.** Its vtables are
  hand-declared (about 30 slots for `IXAudio2Voice` plus `IXAudio2SourceVoice`). Attempting it: the
  master voice's `GetVoiceDetails` returned garbage and `IXAudio2SourceVoice::GetState` fast-failed.
  Binding a device *does* work - `XAudio2Create` plus `CreateMasteringVoice` succeed on the default and
  on an explicit MMDevice id - but nothing above a mastering voice can be driven safely. Since a
  wrapper library is excluded by section 8, Windows playback is WASAPI, which is in-box COM with a
  15-slot interface and a documented contract.
- **WASAPI accepts a format the device is not running at.** `Initialize` with `AUTOCONVERTPCM` and
  `SRC_DEFAULT_QUALITY` takes S16 44.1 kHz stereo on a device whose mix format is 48 kHz, so the
  package pushes decoded clip PCM and the platform resamples. That is one converter fewer to write,
  and the same call shape on every Windows device.
- **A render loop can be polled instead of evented.** Asking for `AUDCLNT_STREAMFLAGS_EVENTCALLBACK`
  without calling `SetEventHandle` fails with `AUDCLNT_E_EVENTHANDLE_NOT_SET` (0x88890014); polling
  `GetCurrentPadding` and filling the free space paces correctly against the device clock, measured at
  15,964 frames in 400 ms against an expected 17,640.
- **`AVAudioEngine` cannot choose an output.** It always renders to the system default; per-device
  selection on macOS means CoreAudio. `AudioQueue` takes `kAudioQueueProperty_CurrentDevice` as a
  device *UID* (a `CFString`), not a device id, which is a different identifier from the one
  enumeration returns - and the property takes a *pointer to* the `CFStringRef`, not the reference,
  which is a segfault if you get it wrong.
- **`AudioQueue` runs with no run loop.** `AudioQueueNewOutput` with a null run loop and a null run
  loop mode starts and plays on its own threads, which is what a UI-sound engine wants: no
  `CFRunLoop` to own and no thread of ours in the path.
- **`pa_simple` cannot enumerate but can target.** The simple API has no sink list, so enumeration
  needs the asynchronous `pa_context` and its main loop, while playback can stay on the blocking
  simple API and still pick the sink by name.
- **`pa_context_get_server_info` could not be decoded.** `default_sink_name` read as garbage from the
  `pa_server_info` layout, so the default is resolved with `pa_context_get_sink_info_by_name` on the
  literal name `@DEFAULT_SINK@`, which returns an ordinary `pa_sink_info`.
- **Everything here is in-box except Linux's `libpulse`.** That is the one runtime dependency, and the
  one place a missing library has to read as "no audio device" rather than as an exception.
- **One machine with no hardware is a useful test.** The Windows machine used for the spike has no
  PnP sound device and no registry render endpoints, but one active MMDevice render endpoint:
  `Remote Audio`, a virtual endpoint. Enumeration and the default-role lookup agreed on it and WASAPI
  rendered to it. A virtual endpoint is still an endpoint, so the null-device path is not what that
  machine exercised - it is exercised by having no endpoint at all, which on Linux is the normal
  state and is why section 4 exists.

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
- **`audio.Path` is the diagnostic**, in the same form as `GraphicsPath`: `wasapi: Remote Audio`,
  `coreaudio: LG UltraFine Display Audio`, or `audio (no sound server)`.

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
    windows/    MMDevice enumeration, WASAPI playback
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
  by a harness on `mac`, `linux` and `windows` and recorded in section 9, the same way the badge
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
| Windows playback is WASAPI, not XAudio2 | XAudio2 enumerates nothing in 2.9, so MMDevice was needed anyway, and its voice interfaces cannot be driven from .NET without hand-declaring a 30-slot vtable that returned garbage when tried (`GetVoiceDetails`) and fast-failed (`GetState`). WASAPI is documented in-box COM, accepts our format through `AUTOCONVERTPCM`, and paces to the device clock. Rejected: XAudio2 behind a wrapper library (excluded above), and WASAPI exclusive mode (section 10 on latency). |
| WAV in managed code first | Zero native cost and it covers UI sounds. Vorbis/MP3 later, as vendored single-file decoders in this package rather than by dragging in Media Foundation or GStreamer for a beep. |
| Position computed from a clock | Section 3. Rejected: a frame counter incremented by the audio callback, which is wrong the moment a callback is late and wrong in a different way when the clock is a test's. |
| No hotplug events in v1 | A snapshot list and `Refresh()`. The change notifications exist on all three platforms and are a follow-up, not a v1 requirement. |

## 9. Steps

| # | Step | State |
|---|---|---|
| 1 | Spike Linux: `libpulse` enumeration of sinks and playback to a chosen sink on `linux` (pipewire-pulse) | **done** |
| 2 | Spike macOS: CoreAudio enumeration and `AudioQueue` playback to a chosen device on `mac` | **done** |
| 3 | Spike Windows: MMDevice enumeration and playback on an id, including the no-device path | **done** |
| 4 | `common/`: `IAudioEngine`, `AudioDeviceInfo`, `AudioClip`, `Voice`, the timeline, the null device, WAV decoding, `AddAudio()` | |
| 5 | `linux/`: `IPlatformAudio` over libpulse, with `NativeLibrary.TryLoad` and the reason strings | |
| 6 | `windows/`: MMDevice enumeration and WASAPI render | |
| 7 | `macos/`: CoreAudio enumeration and AudioQueue output | |
| 8 | Tests: the timeline, the null device's invariants, WAV decoding, volume | |
| 9 | Demo **Audio** page; `README.md` section; `FEATURE-COST-POLICY.md` worked example | |

### Spike runs

Three throwaway console programs, kept out of the repository; what they printed is the evidence for
section 2.

**linux** - Ubuntu 24.04, x64, .NET 10.0.15, `pipewire-pulse`, one sink.

```
libpulse: both libraries loaded
context state: 4 (READY)
  sink 50: name 'xrdp-sink' description 'xrdp-sink' format 3 rate 44100 channels 2
  default sink: "xrdp-sink"
sinks: 1, default: 'xrdp-sink'
  played 440 Hz to default (dev: null): write ok, drain ok
  played 440 Hz to first sink 'xrdp-sink': write ok, drain ok
PASS
```

**mac** - macOS arm64, .NET 10.0.12, three output devices.

```
CoreAudio and AudioToolbox: loaded
  device 117: name 'USB Audio Device' uid 'AppleUSBAudioEngine:C-Media Electronics Inc.:...'
  device 111: name 'LG UltraFine Display Audio' uid 'AppleUSBAudioEngine:LG Electronics Inc.:...'
  device 105: name 'Mac mini Speakers' uid 'BuiltInSpeakerDevice'
outputs: 3, default id 117 'USB Audio Device'
  played 440 Hz to default (CurrentDevice not set): queue started, ran and stopped
  played 440 Hz to uid 'AppleUSBAudioEngine:C-Media Electronics Inc.:...': queue started...
  played 440 Hz to uid 'AppleUSBAudioEngine:LG Electronics Inc.:...': queue started...
  played 440 Hz to uid 'BuiltInSpeakerDevice': queue started, ran and stopped
PASS
```

Each device play also read `kAudioQueueProperty_CurrentDevice` back and compared it with the UID it
had just set, so "started" means "started on that device" rather than "started".

**windows** - Windows 11 x64, .NET 10.0.14, `Audiosrv` running, no hardware endpoint.

```
render endpoints (active): 1, (all states): 1
  device {3.0.0.00000002}.{6C26BA7D-F0B2-4225-B422-8168C5261E45}: name 'Remote Audio'
default render endpoint: {3.0.0.00000002}.{6C26BA7D-F0B2-4225-B422-8168C5261E45}
device list and default agree
xaudio2: mastered a voice on the default device
wasapi: rendered 15964 frames in 400 ms (expected about 17640), buffer 970 frames
PASS
```

The `xaudio2` line is kept because it is the measurement that chose WASAPI: binding works, and
everything the package would have needed above it does not.

## 10. Risks

- **Linux is where this will look broken.** A VM or a container has no sound server, so the honest
  output is "no output device" - which is why the null device and its reason string exist, and why
  the demo page has to say which device is live rather than pretending.
- **`libpulse` absent changes the failure mode.** It has to be `TryLoad` plus a reason, never a
  `DllNotFoundException` out of a static initialiser.
- **Device selection on macOS was the awkward corner, and it is settled.** `AVAudioEngine` cannot do
  it, so the spike used `AudioQueue` plus `kAudioQueueProperty_CurrentDevice`, on a machine with three
  output devices, and confirmed the selected device by reading the property back. The remaining
  awkwardness is bookkeeping: the UID is a different identifier from the `AudioDeviceID` the HAL
  enumerates with, so `AudioDeviceInfo` has to carry both.
- **Latency is not a goal and will not be one.** Shared-mode output is 10-40 ms; a UI sound wants
  under a hundred. An application that needs tighter than that is out of scope, and saying so early
  is cheaper than being asked to add WASAPI exclusive mode later.
- **A silent success can hide a misconfiguration.** An application that expected sound and got none
  has only `Path` and `Reason` to go on, so both have to be exact rather than generic.
