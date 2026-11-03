# Feature cost policy

How XPlatUI adds features without taxing the applications that do not use them.

Status: in force. Every new feature states, in its pull request, which rung it sits on, why it could
not sit lower, and the measured cost for an application that does not use it.

---

## 1. Why this policy exists

XPlatUI's reason to exist is a small, trimmed, AOT-compiled deliverable: a UI library that ships as
a few megabytes of native code with no runtime, on Windows, macOS and Linux. Every feature we add
works against that, because the trimmer can only remove code it can prove is unreachable. A feature
that hangs off a type every application touches, or that registers itself when a window is created,
is paid for by every application forever, including the ones that never heard of it.

The policy is therefore about *shape*, not about build flags: features must be shaped so that
"available but unused" actually compiles away.

## 2. The rule

An application that does not use a feature should pay as close to nothing for it as we can make it:
no code in the published AOT binary, no startup work, no per-window native registration, no memory.

This is not a demand for a literal zero in every case. It is a demand for **bare minimal bloat**,
established by measurement rather than by assertion. A small, always-present seam that lets
everything else be removed is a good trade and is explicitly allowed. A feature that silently costs
every application code and startup time it never asked for is not.

## 3. Why this is a design constraint, not a build setting

Trimming is static reachability from the entry point. Turning on `PublishTrimmed` and `PublishAot`
does not make an unused feature disappear; it removes only what nothing reaches. So the question for
every new feature is "what reaches this code, and does that path exist in an app that does not use
it?". The patterns that answer "yes" are the ones to avoid:

| Pattern | Why the code survives trimming |
|---|---|
| A new member on `XPlatControl` or on a backend view type: a virtual method, an interface implementation, or an event | The type is always retained, and a retained type keeps its overrides and interface implementations to fill its vtable. Field-like events add a delegate field to every instance and keep their accessors. All of it is kept even if nothing calls it. Section 5 is the shape that avoids this. |
| Work performed at window creation | Runs for every window in every app, and is reachable from `Show`, so the code behind it is kept. |
| Reflection: `Activator.CreateInstance`, `Type.GetType`, assembly scanning, attribute-driven registration | The trimmer cannot prove what is reachable, so it keeps the world (or warns and keeps it). |
| Module initializers, or static constructors and static state on a type every app touches | They run the first time the type is touched, which every app does. |
| Feature code inside an always-used control | The app names the control, so the whole type and its helpers are kept. |

The corollary worth stating plainly: a feature cannot be made zero-cost by hiding it behind a
property the app sets at runtime if the code behind that property is reachable from a retained
type. Reachability is decided statically, not by the value of a `bool` at run time. That is why §4
has a second and third rung rather than just "make it lazy".

## 4. The three rungs

In order of preference. Each feature sits on the lowest rung it can actually reach, and the reason
it could not reach a lower one is written down. The one exception is a feature obscure enough that
keeping it out of the main package is worth more than the trimming it would have earned: that
belongs on rung 3 even if rung 1 or 2 would have worked, and the reason is written down the same
way.

### Rung 1: zero cost by construction

Design it so that the only path to the feature's code runs through application code that opted in.
The app referencing the type, or calling the method, is what keeps it alive. No hooks, no
registration, no changes to always-retained types.

This is the default and should be the answer for most new controls: a control the app must name
costs nothing when it is not named.

When a feature wants to present itself as new members on a control that already exists, §5 is the
shape that keeps it on this rung.

### Rung 2: opt-in registration or hook

If the feature genuinely must do work at application startup or window creation, the *registration*
becomes the opt-in point. Nothing registered means nothing runs, and the feature's implementation is
trimmed away along with the registration call. The framework supplies the seam; the feature supplies
the hook implementation; the application supplies the one line that turns it on.

The opt-in is coarse on purpose. It is a decision the application makes once, not a decision each
control or each window makes, so an application that calls it pays the feature's full runtime cost
even if it never exercises the feature, and that is accepted. Fine-grained gating would mean
conditioning the work on a value the application can change at any moment, which is the same
reachability problem §3 describes: the code behind the value is retained either way, so the gating
buys nothing at publish time and risks a feature that is broken half the time at run time.

### Rung 3: separate package

If neither of the above is practical, the implementation does not go into the base `XPlatUI`
projects at all. It goes into its own project that applications reference explicitly, so the base
deliverable never contains it and no amount of trimming cleverness is required.

The library ships as a small set of packages, and this rung is where the rest of them live:

| Package | Contents |
|---|---|
| `XPlatUI` | Everything platform-independent: the controls, the drawing model, the application and window types, and the hook seam. An application that uses only the common controls needs nothing else. |
| `XPlatUI.Backend.<platform>` | One per supported platform. Exactly one is referenced by an application, and it is the only place that platform's native code lives. |
| `XPlatUI.Feature.<feature>` | A feature that could not reach rung 1 or 2, or that is rare enough not to belong in the main package. One package covers every platform the feature supports. |

An application references `XPlatUI` and one backend. That is enough for a simple application using
the common controls, and it is what the project templates contain. Everything else is a reference
the application adds, which is the opt-in in its packaging form: a feature that is not referenced
cannot be used and cannot be in the deliverable.

A feature author writes one `<PackageReference>` with no `Condition`, on every platform. The package
carries one assembly per platform, and NuGet selects the right platform's assembly at restore, from
the consuming project's target framework. Nothing is dispatched at run time, and the platforms that
were not selected are not in the deliverable.

One consequence applies to every rung-3 feature: its surface is extension members, because a
separate assembly cannot add members to a type declared in `XPlatUI`. §5's shape is therefore
not a preference here but the only option, and a feature that genuinely needs a member on a core
type cannot take this rung at all.

Rung 3 is a per-platform claim, not a property of the feature. A platform whose loader keeps a
registered type's exports alive can make a feature's native half unremovable no matter which package
it ships in; on macOS, AppKit keeps `NSDraggingDestination` exports whether or not anything
registers. The package layout is the same either way, but a measurement row does not get to claim a
saving the platform's loader will not deliver, and a feature that touches a registered platform type
is measured rather than assumed.

#### The project layout

One project, four folders:

```
XPlatUI.Feature.<feature>/
    common/     portable code, compiled into every platform
    windows/    net10.0-windows
    macos/      net10.0-macos
    linux/      net10.0
```

The project multi-targets every platform the feature supports, and each inner build compiles
`common/` plus exactly one platform folder:

```xml
<PropertyGroup>
  <TargetFrameworks>net10.0-windows;net10.0-macos;net10.0</TargetFrameworks>
</PropertyGroup>

<ItemGroup>
  <Compile Remove="windows/**;macos/**;linux/**" />
  <Compile Include="windows/**" Condition="'$(TargetFramework)' == 'net10.0-windows'" />
  <Compile Include="macos/**" Condition="'$(TargetFramework)' == 'net10.0-macos'" />
  <Compile Include="linux/**" Condition="'$(TargetFramework)' == 'net10.0'" />
</ItemGroup>
```

Each folder is compiled on its own branch and no other. The folders are named after the platform
rather than the backend, so a feature author chooses a folder by the operating system being
supported, and which backend that folder targets follows from the framework, since `net10.0-windows`
can only mean Win32.

Sharing is a folder, not an assembly. `common/` compiles into each platform's output, so nothing has
to be `public` for the sake of another platform, the folders need no `InternalsVisibleTo` between
them, and there is no second assembly for each `lib/` folder. `#if` is still available inside
`common/` for differences too small to justify a file in each platform folder, and a feature with
nothing portable simply has an empty `common/`. The same `$(TargetFramework)` condition is how a
platform's `PackageReference` is applied, which is what produces the per-platform dependency groups
below.

A feature that drives native code the backend owns is the one case that crosses an assembly
boundary, and it crosses it through a public seam rather than a friend assembly. No backend declares
`InternalsVisibleTo`, so no feature needs special access to one: what the backend keeps is a handful
of public members whose names describe the platform facility - the window handle, the widget input
arrives on, the window's own native teardown, a coordinate conversion - and the feature builds on
those the way any consumer would. §6.5 lists exactly what each platform keeps and why it cannot
move.

A consuming solution is simpler than either alternative. The project reference is one unconditional
line, and MSBuild builds only the inner build that matches the consuming project, so there is no
host detection and no condition choosing which platform to reference. Only the feature project's own
build enumerates all three, which matters in one case: a direct or solution build of the project
needs the macOS workload, because that is the one platform whose reference assemblies do not come
with the SDK. A machine without it fails with `NETSDK1147`. The development solution lists the
feature project and `XPlatUI.Backend.AppKit`, which carries the same requirement, so the workload is
a prerequisite for building the solution and a contributor meets it once with
`dotnet workload install macos`. That is a deliberate trade: the feature and the backend are visible
in the IDE and compiled by the ordinary build, at the cost of one prerequisite, rather than being
reachable only through the pack pipeline.

Only the macOS workload is actually a workload, so one macOS machine packs the whole package:
`-p:EnableWindowsTargeting=true` brings the Windows reference pack to it, and the Linux framework
needs nothing extra. Without the property the `net10.0-windows` inner build stops with `NETSDK1100`.
For local work, `-p:TargetFramework=net10.0-windows` builds one inner build without editing the
file; the plural `-p:TargetFrameworks=...` does not work as a narrowing switch, because a global
property reaches the restore graph but not the referenced projects' own builds, so those build the
frameworks they declare against assets restored for one, and the build stops with `NETSDK1005`.

Pin the macOS target framework version rather than writing the floating `net10.0-macos`. The packed
`lib/` folder name carries whatever platform version the build machine's workload supplies, and an
unpinned build produces `lib/net10.0-macos26.0/`. A consumer on an older workload then resolves to a
lower platform version and does not match the folder. A feature project whose `ProjectReference` to
`XPlatUI.Backend.AppKit` has to name that backend's framework cannot pin it, so the pin is a
publishing decision that arrives with the first published package rather than a rule the project can
keep on its own.

#### Shipping: one pack

`dotnet pack` on the project produces the whole package, because each inner build contributes its
own `lib/` folder and its own dependency group:

1. One `lib/<tfm>/` folder per platform, each holding that platform's assembly. Nothing else needs
   to go in it, because `common/` was compiled into it.
2. One dependency group per platform, naming `XPlatUI` plus whatever feed packages that platform
   needs, generated from the same `$(TargetFramework)` conditions that selected the source folder.

No merge step, no hand-written groups, no `SuppressDependenciesWhenPacking`, and no `NU5128`.

A dependency group per platform is what makes a platform-only package tolerable. Dependencies are
selected by the same nearest-framework rule as assemblies, so a macOS-only package listed in the
macOS group is never restored by a Windows application.

What makes that safe is that every platform gets a group, even an empty one. When the matching group
is absent NuGet falls back to a compatible one, and `net10.0` is always compatible with
`net10.0-windows`, so a missing Windows group silently hands a Windows application the Linux
dependencies. Each group must also be the platform's whole closure rather than its extras, because
nothing is inherited from the `net10.0` group once a platform group is chosen.

The same rule governs the `lib/` folders, where the failure is worse because it is silent. NuGet
chooses one `lib/` folder per package per target framework, so a missing Windows folder is not an
error: the `net10.0-windows` consumer compiles against the `net10.0` folder instead, and a Windows
application ships the Linux assembly. A project that multi-targets cannot produce that shape by
accident, because each folder is written by the same inner build that the `TargetFrameworks` list
requires.

#### The pack job

Both of those failures are silent, so the pipeline does not take `dotnet pack`'s exit code as the
answer. `.github/workflows/feature-pack.yml` packs every `XPlatUI.Feature.*` project on a macOS
runner with `EnableWindowsTargeting=true`, then hands each package to
`.github/scripts/check-feature-package.py`, which reads the `.nuspec` out of the archive, lists the
`lib/` folders, and fails if a folder has no dependency group of the same name or a group has no
folder. The check reports the layout as a table in the job summary, so a review sees the platforms
the package covers without downloading it.

#### The `net10.0` fallback guard

A missing `lib/` folder cannot be caught at build time, so a feature's Linux build carries a runtime
guard: a module initializer that fails the process if the `net10.0` assembly is loaded anywhere but
Linux. The `net10.0` folder is the only one that needs it, because it is the only one that is
compatible with another platform's target framework.

```csharp
// linux/PlatformGuard.cs
internal static class PlatformGuard
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Verify()
    {
        if (!OperatingSystem.IsLinux())
        {
            Environment.FailFast(
                "XPlatUI.Feature.<feature>: the net10.0 (Linux) assembly was loaded on "
                + RuntimeInformation.OSDescription
                + ". The package is missing this platform's lib folder.");
        }
    }
}
```

`FailFast` rather than an exception, because this is a packaging bug rather than a condition an
application can handle. The alternative is a feature that misbehaves somewhere unrelated, and an
exception can be caught and masked. The process dies with a distinctive exit code and the message on
standard error. `OperatingSystem.IsLinux()` is a constant check with no reflection, so it is safe
under trimming and AOT. The analyzer warns `CA2255`, that module initializers are intended for
application code; that is the intended use, so it is suppressed at the attribute with a comment.
The guard's own cost is one method and one string in the Linux assembly, inside a package the
application has already chosen to reference.

The guard is a detector, not the guarantee. The guarantee is target framework selection, which is a
build-time decision; the guard exists because the failure it detects is otherwise silent. Two limits
are worth knowing:

- A module initializer does not keep its own assembly alive. If trimming inlines away everything
  reachable in the assembly, the assembly and its initializer are deleted together and the guard
  never runs. A real feature assembly is not in danger, but the guard should not be the only code in
  its folder, and a test that exercises it is worth having.
- The guard runs when the assembly is loaded, which in a framework-dependent build is the first use
  of the feature. An application that never touches the feature never loads the assembly, and has
  nothing to guard.

Packaging does not change the namespace (§5.6). A feature in `XPlatUI.Feature.Clocks` is still
`namespace XPlatUI.Clocks`, so moving a feature between rung 2 and rung 3 does not touch application
code.

This rung has a cost of its own (a project to version and publish, a workload requirement to build
it, and a package to keep correct), so it is chosen for a stated reason rather than by default.

## 5. Expressing a feature as an extension

The most natural way for a feature to present itself is as new members on `XPlatControl` or on one
of its subclasses: a property here, a method there, an event when something happens. Those members
must not be added to the type. The feature's implementation goes in its own namespace and is exposed
through extension members, so the surface reads as part of the control while the code stays
removable.

This is a shape rule rather than a size rule, and it is the main way a feature stays on rung 1.

### 5.1 Why this is the trim-safe shape

An extension member compiles to a static method on a static class. It adds nothing to the extended
type: no new member, no vtable slot, no interface implementation, no instance field. An extension
member that nothing calls is trimmed, one member at a time, which is finer granularity than a member
of a retained type ever gets.

For contrast, a field-like event on a retained type is a real member: every instance in every
application carries a delegate field (8 bytes on 64-bit) whether or not anything ever raises it, and
the accessors stay with the type.

Converting an event to the `Add`/`Remove` pair in §5.4 does not change that cost for a core event,
which still needs its backing field. What the pair buys is that the same shape works for a feature,
which has no field on the control to hang a C# event on.

### 5.2 What the language allows

Verified against the toolchain this repository builds with (`net10.0`, C# 14):

| Member kind | Available | Notes |
|---|---|---|
| Methods | Yes | The classic extension method. |
| Properties, including setters | Yes | Extension blocks (`extension(T value) { ... }`), not only methods. |
| Properties in object initializers | Yes | `new XPlatLabel { AllowDrop = true }` still compiles, so the object-initializer style the demo uses survives. |
| Indexers, operators, static members | Yes | Available if a feature needs them. |
| Events | No | `CS9282`, "This member is not allowed in an extension block". Use the handler methods in §5.4 instead. |

Extension blocks are a C# 14 feature, so the policy assumes the library and its consumers are on the
same compiler. They are, because the library targets `net10.0`.

### 5.3 Access and state

- **Access.** An extension block in the same assembly can read and write `internal` members of the
  extended type; it cannot reach `private` ones. That is enough for a rung 1 feature, which lives in
  `XPlatUI` and can use internal seams instead of widening the public API. A feature that has to
  live in its own assembly (rung 3) uses the backend's public seam (§6.5) and nothing else: it gets
  no friend access, and no backend names it.
- **State.** Extension members cannot add fields to the control, so per-control state belongs to the
  feature: a lazily created state object, or a `ConditionalWeakTable<XPlatControl, TState>` keyed
  on the control. Both are trimmed with the feature, but both cost a lookup per access, so a hot
  path caches the state rather than looking it up again.
- **No reflection discovery.** Extension members are invisible to `GetMethods()` on the type. That
  is good for trimming, and it means nothing may register a feature by scanning for its members.

### 5.4 Events: `Add` and `Remove` handler methods

Extension members cannot be events: `CS9282`, "This member is not allowed in an extension block",
for `event`, `add` and `remove` alike. Rather than work around that with a helper object, which
makes the feature read as bolted on, the library does not use the C# `event` keyword at all. Every
event is a pair of methods taking an `Action`:

```csharp
public IDisposable AddDroppedHandler(Action<object, XPlatDragEventArgs> handler);
public void RemoveDroppedHandler(Action<object, XPlatDragEventArgs> handler);
```

The callback is the sender followed by the payload, and an event that carries no payload has no
second parameter. That is most of them:

```csharp
public IDisposable AddClosedHandler(Action<object> handler);
public IDisposable AddKeyDownHandler(Action<object, XPlatKeyEventArgs> handler);
```

No wrapper type is involved. The payload is whatever the event already carries, so a handler
signature is the delegate the event would have had, written out, and nothing exists only to be
unpacked again at the top of the lambda.

Subscribing then reads like a member of the control, and because the pair is made of methods it can
be written as extension methods for a feature:

```csharp
using var subscription = control.AddDroppedHandler((sender, e) => { ... });
control.RemoveDroppedHandler(handler);
```

This is a codebase-wide convention rather than a rule for opt-in features only. Core events on
`XPlatControl`, `XPlatWindow`, `XPlatApplication` and the backend interfaces use the same shape, so
there is one way to subscribe to anything.

Rules:

- The name is `Add` or `Remove`, then the event name, then `Handler`: `AddDroppedHandler`. The event
  name is the part that appears in documentation, so it keeps a normal event name.
- `AddXHandler` returns an `IDisposable` that removes the handler on dispose, so a scope can own a
  subscription without holding the handler itself.
  subscription whose owner has already been torn down.
- `RemoveXHandler` stays alongside the token, because the two suit different call sites. A consumer
  that already holds the handler callback can pass it again rather than storing a token next to it,
  and a handler registered from a place with no natural scope has no obvious owner for a token
  anyway. The pair is also what makes the shape look like the event it replaces; a lone `Add` would
  read as a subscription that can only be ended by holding a token. Both are one line per event.
- Handlers run on the UI thread in subscription order. A handler that throws stops those after it,
  as a C# event would, and the exception reaches `XPlatApplication.UnhandledException`.
- The backing storage is private and unchanged: one field per event, exactly as a field-like event
  has today, so a core event costs no more than it does now. The field sits with its `Add` and
  `Remove` at the top of the type, ahead of the members that use it, so a reader looking for what a
  type can raise finds one block rather than a scattering among the methods. The new cost is one
  small allocation per subscription for the returned token. A feature keeps its own subscriptions in
  its per-control state (§5.3), which is trimmed along with the feature.
- No `event` keyword anywhere, including interfaces and backend types. A review that finds one has
  found a bug in the convention.
- The payload is whatever the event already carries. `XPlatDragEventArgs`, `XPlatMouseEventArgs` and
  friends stay as they are, and a feature that needs a payload defines its own type rather than
  wrapping a value in a generic one.
- A payload class is a plain class with no base type, so a feature that defines one has nothing to
  inherit and no `EventArgs` naming to imitate.

### 5.5 When a member does belong on the control

Three exceptions, each of which has to be stated in the pull request:

1. **The control cannot work without it.** Core behaviour is not optional and does not belong behind
   an opt-in. A text box that cannot accept typing is not a feature.
2. **Subclasses must be able to override it.** Use an interface the control implements, never a
   virtual on the base class: a virtual or override on a retained type is kept for every
   application, while an interface is only retained by the controls that implement it.
3. **The extension shape would break a common idiom.** If the alternative makes ordinary application
   code awkward, say so, take the member, and measure what it costs.

### 5.6 Naming

- One namespace per feature: `XPlatUI.DragDrop`, rather than members sprinkled across `XPlatUI`.
- The `using` is the visible opt-in. An application that never writes `using XPlatUI.DragDrop;`
  never sees the surface, which also reminds the reader that it is not paying for it.
- Extension classes are named for the type they extend plus the feature:
  `XPlatControlDragDropExtensions`.
- Handler methods follow §5.4: `AddDragDropHandler`, `RemoveDragDropHandler`.
- A feature that needs per-control state keeps it behind an extension property or an extension
  method on the control, never behind an object the application has to hold on to.

## 6. The opt-in mechanism

A feature that has to run code is turned on by a service registration, and the framework runs it
through a hook.

### 6.1 Two phases, because services and work happen at different times

`XPlatApplication` builds its container in one place and runs the loop in another, so a feature may
need two distinct things:

1. **Contribute services**, before `BuildServiceProvider`. This already exists as
   `XPlatApplicationOptions.ConfigureServices`, which receives MEDI's `IServiceCollection` and is
   documented to stay inside the AOT-safe subset (generic `AddSingleton<TService, TImpl>()`, factory
   overloads, `AddWindowScoped<T>`; no assembly scanning).
2. **Run work**, after the container exists, at one of the phases in §6.3. This is the hook.

A feature that needs both registers a service and a hook, in the same opt-in call.

### 6.2 The application hook

```csharp
public enum XPlatApplicationPhase
{
	Creating,
	Created,
	Stopping,
	Stopped,
	Disposed,
}

public interface IXPlatApplicationHook
{
	void OnPhase(XPlatApplicationPhase phase, XPlatApplication application);
}
```

The application-level hook is a singleton, because the work it does is process-wide. Per-window work
has its own hook with its own lifetime; see §6.3, which also says when each phase fires and why they
are split where they are.

`XPlatApplication` resolves them once, immediately after `BuildServiceProvider`, and keeps the
instances for the rest of its life:

```csharp
_applicationHooks = _provider.GetServices<IXPlatApplicationHook>().ToArray();
RaisePhase(XPlatApplicationPhase.Creating);
```

One lookup for the whole run, the same instances and the same order at every phase, and nothing
resolved while the application is tearing itself down. `RaisePhase` is the one place that knows the
ordering rules of §6.3: it walks the array forwards or backwards, calls the backend's typed hooks on
the outside, and contains an exception so that one hook cannot stop the next.

The application opts in through the registration it already has:

```csharp
new XPlatApplicationOptions
{
	ConfigureServices = services => services.AddSingleton<IXPlatApplicationHook, DragDropHook>(),
}
```

or, preferably, through a feature extension that does the same thing with a name that says what it
turns on:

```csharp
ConfigureServices = services => services.AddDragDrop(),
```

A hook that is a single call does not need a class of its own. One overload in
`XPlatServiceCollectionExtensions` wraps the action in a hook that already exists:

```csharp
public static IServiceCollection AddXPlatApplicationHook(
	this IServiceCollection services,
	XPlatApplicationPhase phase,
	Action<XPlatApplication> hookFunc)
{
	ArgumentNullException.ThrowIfNull(services);
	ArgumentNullException.ThrowIfNull(hookFunc);

	return services.AddSingleton<IXPlatApplicationHook>(
		_ => new ActionBasedAppHook(phase, hookFunc));
}

/// <summary>Shorthand for a hook at <see cref="XPlatApplicationPhase.Creating"/>.</summary>
public static IServiceCollection AddXPlatApplicationStartupHook(
	this IServiceCollection services,
	Action<XPlatApplication> startupHookFunc) =>
	services.AddXPlatApplicationHook(XPlatApplicationPhase.Creating, startupHookFunc);

internal sealed class ActionBasedAppHook : IXPlatApplicationHook
{
	private readonly XPlatApplicationPhase _phase;
	private readonly Action<XPlatApplication> _hookFunc;

	public ActionBasedAppHook(XPlatApplicationPhase phase, Action<XPlatApplication> hookFunc)
	{
		_phase = phase;
		_hookFunc = hookFunc;
	}

	public void OnPhase(XPlatApplicationPhase phase, XPlatApplication application)
	{
		if (phase == _phase)
		{
			_hookFunc(application);
		}
	}
}
```

Window hooks get the same treatment: `AddXPlatWindowHook` takes one optional callback per point, so
a feature that only cares about teardown still writes a single lambda. The wrappers are seam types,
so the seam pays for them once whether or not anything calls them; see §7.

There is no `onDispose` parameter on either helper, and no `IDisposable` on either wrapper.
`Disposed` is a phase, so a hook with teardown work registers the same lambda at `Disposed` instead
of handing over a disposable. That is one mechanism rather than two, and it keeps the teardown in
registration order like every other phase. A hook that still implements `IDisposable` for its own
reasons is disposed by the container afterwards, windows before the application.

The phases are the teardown order, and it is the nesting order. A window's `Disposed` fires before
its scope is disposed, and every window is disposed before the application's `Stopped` and
`Disposed`. Window teardown always precedes application teardown, and the application's `Disposed`
precedes the root provider's and the backend's shutdown. A hook that needs it the other way round
says so in its own `Stopping` or `Stopped` callback rather than relying on `Dispose` ordering.

The trim property that makes this work: `GetServices<T>()` on a service nobody registered returns an
empty sequence, and the only thing retained is the interface itself. The concrete hook type is
reachable solely from the registration call, so an app without that call publishes without it.

### 6.3 Hook points and lifetimes

A single startup hook is not enough, because most expensive work is per window rather than per
process. There are two kinds of hook, with two lifetimes:

| Kind | Lifetime | Resolved from | What it is for |
|---|---|---|---|
| Application hook | Singleton | The root provider | Process-wide platform initialisation that must precede any window (OLE initialisation, application-level pasteboard types), and process-wide teardown after the loop returns. |
| Window hook | Window-scoped | `window.Services` | Per-window work: native registration once the handle exists (drop targets, extra pasteboard types, GTK controllers), and its teardown when the window closes. |

Each kind is one interface taking a phase value rather than one interface per moment, because one
instance serves every phase and a feature normally cares about more than one of them.

#### Application phases

| Phase | When it fires | Direction | Raised from |
|---|---|---|---|
| `Creating` | The container exists, the process services are installed, and no window exists yet. | Ascending | The end of the `XPlatApplication` constructor |
| `Created` | Every window shown before `Run()` is visible, and the loop is about to start. | Ascending | Immediately before `Backend.Run()` |
| `Stopping` | The loop has returned. Every window, the backend and every service are still alive. | Descending | The start of `TearDown` |
| `Stopped` | Every window is gone, and the services are about to be. | Descending | Inside `TearDown`, after the windows are disposed |
| `Disposed` | The container is about to be disposed. Raised on every teardown path, including one that never ran a loop. | Descending | Immediately before the provider is disposed |

The splits are placed where they carry information:

- **`Creating` before `Created`** is everything the application does between building its container
  and entering its loop: creating windows, showing them, sizing the main one. Work that has to
  precede the first window needs `Creating`. Work that wants the finished window tree needs
  `Created`.
- **`Stopping` before `Stopped`** is ordering rather than capability. `Stopping` is the first thing
  that happens after the loop returns, so the whole application is still intact. `Stopped` is the
  last moment at which a singleton service can be resolved, because the container disposes the hooks
  next.
- **`Disposed` after `Stopped`** is the one teardown that always happens. It is the mirror of the
  window's `Disposed`: the hook and every service it can resolve are still alive, and the container
  goes immediately afterwards. The backend shuts down after that, so no phase ever sees a shut-down
  backend.

`Disposed` is therefore the last phase, and the one to use for work that has to happen exactly once
per launch. A hook that implements `IDisposable` for its own reasons is still disposed by the
container, after the phase has run.

`Stopping` and `Stopped` are raised only when a loop ran, matching the guard `Exiting` already has.
A forwarded second instance returns from `Run()` without entering one, so it sees `Creating` and
`Disposed` and nothing else. **`Creating` is therefore paired with `Disposed`, not `Stopping`:**
the two are not guaranteed to both happen, and `Disposed` is the only teardown that is.

#### Window phases

```csharp
public enum XPlatWindowPhase
{
	Created,
	Showing,
	Shown,
	Hiding,
	Hidden,
	Closing,
	Closed,
	Disposed,
}

public interface IXPlatWindowHook
{
	void OnPhase(XPlatWindowPhase phase, XPlatWindow window);
}
```

| Phase | When it fires | Direction | Raised today |
|---|---|---|---|
| `Created` | The native window exists and is not visible yet. | Ascending | Yes |
| `Showing` | `Show` has been called; the native window is not ordered front yet. | Ascending | Yes |
| `Shown` | The window is visible. | Ascending | Yes |
| `Hiding` | The window is about to become invisible. | Descending | Reserved |
| `Hidden` | The window is invisible and still alive. | Descending | Reserved |
| `Closing` | The window is about to close; the native handle is still alive. | Descending | Yes |
| `Closed` | The native window is gone and the backend is disposed. | Descending | Yes |
| `Disposed` | The window object is being disposed; its scope is still alive. | Descending | Yes |

`Hiding` and `Hidden` are declared and raised by nothing, because `XPlatWindow` has no `Visible`
property and no `Hide` method yet. Declaring them now costs one enum member and stops the hide and
show feature from renumbering a public enum later.

**Ascending phases run hooks in registration order; descending phases run them in reverse.** In both
lists the split is the turn of a lifecycle: the phases that bring something up ascend, and the
phases that take it down descend. Reversing the teardown half is what makes a pair of hooks nest: if
`A` registers before `B`, `A` sets up before `B` and tears down after `B`, whatever order the phases
arrive in. The rule holds at application level as well, where the two hooks may not even live in the
same assembly.

The window lifetime already exists, so this costs nothing new. `XPlatWindow` creates an
`IServiceScope` in its constructor, exposes it as `Services`, and disposes it when it closes.
`AddWindowScoped<T>` is `AddScoped` with the window handed to the factory, and `XPlatWindowAccessor`
is how a scoped service reaches its window. A window hook is therefore registered like this, and one
window's instance is nobody else's:

```csharp
services.AddWindowScoped<IXPlatWindowHook>((window, provider) => new DragDropWindowHook(window));
```

The ordering matters and is easy to get wrong. `XPlatWindow` disposes the backend, then raises
`Closed`, then disposes the scope. `Closing` therefore fires while the native handle is alive and
`Closed` after it is gone, which is the split a feature needs: revoking a drop target before the
window is destroyed belongs in `Closing`, and releasing managed bookkeeping belongs in `Closed` or
`Disposed`. A scoped hook's own `Dispose` runs last of all, after the native window is gone.

`Disposed` is the last point at which a hook can still reach other scoped services, and it fires
after the backend is disposed. Two consequences, both rules in §6.4: a typed `Disposed` callback is
managed-only, and the backend's phase entry point has to stay callable once it has been disposed.

The window phases take the window so a portable hook can reach the native handle through
`window.Backend` without the model having to expose anything new. A control-level "attached to tree"
point is deliberately out of scope: the window phases are enough for the features on the roadmap,
and each added point is permanent cost in the seam.

### 6.4 Rules for hook implementations

- **Standalone classes, never backend views.** A hook implemented on `Win32SkiaView`,
  `AppKitSkiaView` or `Gtk4SkiaView` would be retained with those types and defeat the purpose. The
  hook lives in a small class whose only reference is the registration call.
- **Explicit registration, never scanning.** Assembly scanning is the one common pattern that is not
  trimming-safe, and the codebase already forbids it for `ConfigureServices`. Hook discovery is
  registration order, full stop.
- **Registration order is invocation order**, ascending in the phases that bring something to life
  and reversed in the phases that take it away (§6.3). Features may rely on the nesting this gives
  them, which is why it is written down rather than left to chance.
- **Runs on the UI thread.** Application hooks run on the thread that constructs and runs the
  application, before the loop starts and after it returns; window hooks run on the UI thread.
  Documented rather than assumed.
- **Per-window state lives on the hook instance.** A window hook is scoped, so each window gets its
  own instance and the created, shown, closing and closed callbacks share state without a table
  keyed by window. A hook that finds itself needing such a table is registered at the wrong
  lifetime.
- **Failures surface.** A hook that throws goes through the existing `UnhandledException` path
  rather than silently aborting startup.
- **Resolve from the right provider.** Application hooks come from the root provider; window hooks
  come from `window.Services`. Resolving a scoped hook from the root is the trap: MEDI either throws
  under scope validation or hands back a single instance that outlives every window.
- **Dependencies point one way.** A singleton must never hold a window hook, because that would keep
  one window's instance alive for the life of the process.
- **Portable hooks are the outer layer.** They run before a backend's typed hooks (§6.5) in an
  ascending phase and after them in a descending one, so a typed hook always tears down before the
  portable hook that set it up.
- **Typed hooks never derive from portable ones.** An `IXPlatWin32WindowHook` that also implemented
  `IXPlatWindowHook` would be found by both loops and invoked twice.
- **A backend's window phase entry point survives its own `Dispose`.** The window's `Disposed` phase
  fires after the backend is disposed and before the scope goes, so `RunWindowHooks` must touch
  nothing native by then and must not assume the backend is alive.
- **Use `Disposed` for teardown, not `IDisposable`.** An application hook and a window hook both
  have a `Disposed` phase, raised while the hook and the services it can resolve are still alive, so
  a hook releases in the phase and does not need to implement `IDisposable` at all. `Disposed` is
  also the only application phase every launch reaches, so it is where anything that must happen
  exactly once belongs; `Stopping` and `Stopped` are for work that needs the loop's exit code or the
  window tree, and they are the only phases that can be skipped.
- **Register native work at `Shown`, not `Created`.** The native window does not exist until the
  backend is asked to show it, so a hook that needs the handle registers at `Shown` and releases
  through whatever the backend disposes while the window still exists, rather than through
  `Closing`, because a window can be disposed without ever closing.

### 6.5 Platform-specific hooks

A feature that is native on every platform is three layers, and only the middle one differs.

| Layer | Lives in | Cost when the feature is unused |
|---|---|---|
| Contract: service interface, event args, control extension members | `XPlatUI`, in the feature's namespace | Trimmed per member |
| Implementation: `Win32DragDropWindowHook`, `AppKitDragDropWindowHook`, `Gtk4DragDropWindowHook` | The feature's platform folder, or the backend assembly for a rung-2 feature | Trimmed, unless something retained names it |
| Selection: which implementation exists | The backend project reference in the app's project file | None |

Core never references a backend, and an application references exactly one backend per target
framework. The choice is therefore made when the application is compiled: no runtime dispatch, no
reflection, no operating system check.

**The registration call site has one name.** Each backend declares the same extension method, in the
feature's namespace, so the application calls it without knowing which platform it is on:

```csharp
using XPlatUI.DragDrop;

options.ConfigureServices = services => services.AddDragDrop();
```

That method is the opt-in, so it must be the only thing that names the implementation. If a
backend's window creation path mentioned `Win32DropTarget`, the whole COM implementation would be
retained in every Win32 application that never drags anything. It does not: the hook does the
per-window wiring, so the always-on path contains no feature code and there is nothing to gate, look
up or branch on. For a rung-3 feature the backend does not name the hook either, because the hook
lives in the feature package, so the retained backend contains no feature type at all.

The contract across backends is a naming convention, and a backend that forgets it fails at compile
time on that platform, which is the failure we want. `Headless` declares no feature extensions: it
is referenced alongside a real backend, so a duplicate name would be an ambiguity error, and it has
no windows to attach anything to.

**Typed hooks remove the downcast.** A portable hook reaches its backend through
`application.Backend` or `window.Backend`, both typed as the interface, so native work would start
with a cast. Instead each backend declares its own hook interfaces, which name the backend by its
concrete type:

```csharp
public interface IXPlatWin32ApplicationHook
{
	void OnPhase(
		XPlatApplicationPhase phase, XPlatApplication application, Win32ApplicationBackend backend);
}

public interface IXPlatWin32WindowHook
{
	void OnPhase(XPlatWindowPhase phase, XPlatWindow window, Win32WindowBackend backend);
}
```

They live in the backend assembly, so core pays nothing, and they are unrelated to the portable
interfaces (§6.4). `XPlatApplication` runs the portable hooks and then hands the phase to the
backend, which runs its own loop over its typed interface:

```csharp
// IXPlatApplicationBackend, after the portable hooks for the phase
void RunApplicationHooks(XPlatApplicationPhase phase, XPlatApplication application);

// IXPlatWindowBackend, after the portable hooks for the phase
void RunWindowHooks(XPlatWindowPhase phase, XPlatWindow window);
```

Two seam members with empty bodies in `Headless`, and nothing else in the always-on path: a hook
that nothing registers is trimmed away along with the registration. In exchange no feature type is
reachable from the backend, which is the property a conditional lookup in the window creation path
would have destroyed.

At application level the container already gets the layering right for free: it disposes singletons
in reverse creation order, and the `Creating` phase resolves every portable hook before the backend
resolves any typed one, so typed application hooks are disposed first.

**A rung-3 feature reuses all of this.** The typed interfaces live in the backend assembly, so a
feature package cannot add to them, and does not need to: the feature's platform folder references
the backend package, implements `IXPlatWin32WindowHook` there, and registers itself from its own
`Add<Feature>()`. The backend's loop finds it in the container whether the implementation came from
the backend or from a package that referenced the backend.

What changes is only where the hook and its registration live. A backend must not reference a
feature package, so the hook moves into the feature's platform folder and the same-named
`Add<Feature>()` extension follows it there, declared in each platform folder (§4). The native
primitives the hook drives stay behind in the backend, as public members that describe the platform
facility rather than the feature: the feature is the only thing that names them, but it names them
the way any consumer would, so no backend declares `InternalsVisibleTo` and no feature needs special
access to one. The application still references one package per feature, unconditionally, and
forgetting a platform is still a compile error on that platform.

**Everything the feature owns moves, not just the hook.** A hook that called into a backend member
named after the feature would leave the feature in the backend, and the backend is the assembly
every application of that platform ships whether it opted in or not. So the implementation moves as
well, and what stays behind is a seam whose names say nothing about the feature:

| What stays in the backend | Why it cannot move |
|---|---|
| `Handle`, `Host`, `View`, `InputArea` | The native window and the widget input arrives on are the backend's, and every feature needs them |
| `RegisterNativeTeardown` | Something to release while the native window still exists, which is a property of window lifetime rather than of any one feature |
| `ScreenToLogical` | The backend owns the scale factor and the window origin |

`RegisterNativeTeardown` is the interesting one. A native object a feature registers with the
window stays callable until the window is destroyed, so it must be released before that, and it must
also be released when a window is disposed without ever being closed. The backend already disposes
at exactly that point, so it takes an `IDisposable` and hands back a registration the caller can
dispose early. The caller holds the registration rather than the window holding one slot, so the
window keeps a list and two features cannot displace each other. The member is a window-lifetime
facility that any feature can use, and its name contains no feature type, so a backend that no
application has opted into retains a list of `IDisposable` and nothing else.

**One platform can be an exception, and the reason has to be structural.** AppKit dispatches a drag
to the `NSView` that registered for the dragged types, through `NSDraggingDestination` overrides on
that view's own class; there is no separate destination object to hand the window, as there is on
Windows and GTK. The five overrides therefore cannot leave the view subclass, and the AppKit backend
keeps them in `AppKitSkiaView`. What they *mean* still moves: the view names nothing from the
feature, hands over an `AppKitDragPayload` and asks an `IAppKitDragSink` for an `AppKitDragEffect`,
both of which are backend types. The feature's hook implements that sink and translates, so the
backend assembly contains no feature type. The sink, its payload and its effect are public for the
same reason the other seams are: the implementation lives in a feature package, and a public type in
the backend is the only thing the two can share. A view that is never given a sink never registers
for a dragged type, which is what keeps the cost off an application that did not opt in. This is the
bar for an exception: not "it was easier there", but "the platform's API puts the code on a type the
feature cannot supply". Windows and GTK pass it, because an OLE drop target is an object registered
against a handle and a `Gtk.DropTarget` is a controller added to a widget, so both moved in full.

**A platform that cannot support a feature still declares it.** The extension method exists
everywhere so application code compiles, and the platform-neutral service reports `IsSupported` or
throws `PlatformNotSupportedException` from the operation. A silent no-op is how a feature ends up
appearing to succeed while doing nothing.

## 7. The seam is an accepted cost

The two interfaces, the two enumerations and the two dispatch loops are paid by every application,
forever. That cost is accepted rather than budgeted. The seam is what makes the extensibility
points possible, so there is no byte target for it and no hook is rejected for costing more than
another. What the seam gets instead is a stability promise and a shape rule:

- Keep the seam to a handful of tiny types with no dependencies of their own.
- No feature-specific types may appear in the seam; a hook signature that names a drag payload or a
  menu item has already lost.
- Adding a hook point is a deliberate act, not a convenience, and the reason it cannot be expressed
  as an extension member (§5) is recorded in the pull request.

The seam's cost is still reported (§8), because a number that only ever grows unnoticed is how a
small seam becomes a large one. It is a trend to watch, not a threshold to pass.

Two things in the seam are retained whether or not anything uses them, deliberately:

- The action wrappers from §6.2 are seam types, so they are retained even when no application calls
  the overload. One class and one method each is the price of not forcing a class on a one-line
  hook.
- `RunApplicationHooks` and `RunWindowHooks` (§6.5) exist only to keep features out of the backends.
  Both are empty where nothing is registered, and both are what lets a platform feature be reachable
  from the opt-in alone. They stay.

## 8. How we verify

Claims about cost are worth nothing without a number. Every feature lands with:

1. **A size measurement.** Publish a minimal application that does not use the feature with
   `PublishTrimmed` and `PublishAot`, on `win-x64`, `linux-x64` and `osx-arm64`, and compare against
   the baseline below. The delta is quoted in the pull request.
2. **A warning review.** Build with `-p:TrimmerSingleWarn=false` and read the IL2xxx/IL3xxx output
   for the new code. That is where accidental rooting shows up first.
3. **A startup check.** Confirm that an application which does not use the feature performs no new
   work at startup or window creation: no new native calls, no new registrations, no new
   allocations. The seam itself is the one accepted exception: every application performs an empty
   `GetServices` lookup for each hook kind, once for the application and once per window. That is
   the cost §7 accepts, and it does not grow with the number of features.
4. **A use check.** Confirm that an application that does use the feature still gets it, still
   publishes cleanly, and still trims.

This needs a stable measurement fixture: `XPlatUI.Minimal`, one window and one label, kept in the
repository and published by the same command every time. Without a fixed fixture the numbers are not
comparable and the policy becomes folklore.

```
dotnet publish XPlatUI.Minimal/XPlatUI.Minimal.csproj -c Release -r <rid> -p:EmbedSkiaSharpNative=false
```

The fixture is NativeAOT with no publish profile, so `-c Release -r <rid>` is the whole
configuration. `-p:EmbedSkiaSharpNative=false` produces the loose row, which is the one features are
measured on, because it is where the library's own contribution is visible; the default publish
embeds the Skia blob and that fixed cost swamps it. Restore walks the backend selector's three
frameworks, so the publishing machine needs the macOS workload.

| Measurement | win-x64 | linux-x64 | osx-arm64 |
|---|---|---|---|
| Minimal fixture, Skia loose | 3,070,976 | 4,062,656 | 4,234,528 |
| Seam delta over the fixture without the seam | 10,240 | 4,384 | 0 |

The seam delta is the two interfaces, two enums and two dispatch loops of §7, measured by publishing
the fixture before and after they were added. It is 0.32 percent of the fixture on Windows and 0.10
percent on Linux; the two are the same portable code, so the difference is linker layout rather than
content. macOS moved by nothing measurable, because its arm64 image is linked in 16 KB pages and the
seam fits in the slack of the pages it already had.

The opt-in half has one worked example, the only feature measured end to end so far. An application
that registers the drag and drop hook publishes at 3,222,016 bytes on win-x64 against 3,070,976
without it, so the feature costs 151,040 bytes when it is actually used. The same feature measured
4,223,456 on linux-x64 and 4,234,544 on osx-arm64 against 4,062,656 and 4,234,528 without it while
its types still lived in the core assembly; moving them into `XPlatUI.Feature.DragDrop` changed no
code, only the assembly each type is compiled into, and Windows moved by 3,584 bytes from linker
layout alone. Replacing the backends' friend assemblies with the public seam of §6.5 added a further
8,192 bytes on win-x64: the teardown registration list, the per-window lease dictionary the two
window hooks now keep, and the handful of interop declarations the Windows half carries itself
instead of borrowing from the backend. On osx-arm64 the feature is rung 2 rather than rung 1 by the
letter of the rule: the arm64 image is quantised to 16 KB pages, the retained types fit in slack the
linker had already paid for, and opting in costs 16 bytes.

That last measurement is also why the types no longer live in the core assembly. While they did, an
application that never opted in still paid 16,432 bytes for them on osx-arm64, because a member on
`XPlatUiHost` and virtuals on `XPlatControl` made them reachable from types that are always
retained. Nothing in `XPlatUI` may name a feature type, on any platform, for exactly this reason.

Scrolling is the rung 1 worked example. `XPlatScrollBar` and the scrolling half of `XPlatPanel` are
reached only by an application that names them, and the fixture publishes at 3,070,976 bytes with
them in the tree - the same number as before they existed. The one always-retained member is
`XPlatControl.GetChildOffset`, a `protected internal virtual` returning `SKPoint.Empty` that
`HitTest` and the host's coordinate mapping consult, and it is there because a scrolling container
has to tell the framework where it painted its children; that is the exception §9 asks a feature to
state. It is stated here, and it is the reason the fixture's zero is a weaker claim than it looks:
the fixture has no panel, so what the number actually covers is the retained virtual, not the
scrolling code behind it.

Mouse wheel input is the second worked example, and it is the case where the exception had to be
taken. A wheel turn has to reach whichever control is under the pointer, and no opt-in hook can
carry it there, because the control that wants it is a panel the application created rather than a
feature it registered. So four members are always retained: `XPlatMouseEventArgs.Delta` (a float
and a property on an args class that is already retained), `XPlatControl.OnMouseWheel` plus its
`AddMouseWheelHandler`/`RemoveMouseWheelHandler` pair, and `XPlatUiHost.ProcessMouseWheel`, which
each backend forwards to. `XPlatControl.Focusable` is retained alongside them for the same reason:
the bars a panel creates for itself must not be focusable, and gating that per-instance needs a
property on the base class rather than a flag the panel can keep privately. The fixture publishes at
3,072,000 bytes with all four, against 3,070,976 without them: 1,024 bytes, one image-alignment step,
measured on win-x64 only. This is exception 1 of §5.5 rather than the rule - a container that cannot
be scrolled by the wheel is not a working container - and the additions are portable managed code
with no per-platform surface, so the Linux and macOS images, which are quantised to larger pages,
were not re-measured. The overlay scrollbar style is still a later phase, so `XPlatPanel` draws
classic non-overlay bars only.

Invalidation is the third worked example, and the first one whose cost every platform pays. The
`Invalidate` overloads, `PartialUpdates`, `PendingUpdateBounds`, `IsUpdatePending` and
`IncludeInNextPaint` are members of always-retained types, because a control that cannot say which
part of itself changed cannot be partially painted, and the framework has to ask that question of
every control including the ones an application wrote. Alongside them the paint loop gained the
subtree culling that makes the answer worth having, and each backend gained the few lines that
present only the painted area. The fixture publishes at 3,085,824 bytes on win-x64 against 3,072,512
at `4b8e8c2`, so 13,312 bytes; 4,079,936 against 4,062,768 on linux-x64, so 17,168 bytes; and
4,234,576 against 4,218,064 on osx-arm64, so 16,512 bytes. Each pair was published from a worktree
of the baseline commit rather than compared against the table above, whose rows predate the
scrolling and wheel work. All three platforms pay about the same, which is what a change made of
portable managed code plus a per-backend presentation call should look like; nothing new runs at
startup, and `PartialUpdates` defaults to off so a backend that rasterizes whole surfaces is
unaffected. This is exception 1 of §5.5, and the exception is the point: partial painting is a
property of the framework's paint loop rather than a capability a control opts into.

`XPlatListBox` is the fourth worked example and the cleanest rung 1 case so far. Nothing
always-retained names it: it derives from `XPlatControl`, so it is reached only when an application
constructs one, and the two enums it introduced (`XPlatSelectionMode`, `XPlatDrawItemState`) are
reachable only from its own members. The scrolling it needs was extracted out of `XPlatPanel` into
`XPlatScrollBars`, an `internal sealed` class that the list box and the panel both hold a private
field of, so the extraction added no retained member of its own - it moved code from one rung 1 type
to another. The one edit to an always-retained type is `XPlatControl.ToRectI`, which widened from
`private` to `protected internal static`; the method already existed and was already retained, so
the change is visibility rather than content. The fixture publishes at 3,092,992 bytes on win-x64
both before and after, so the delta is 0. That zero is weaker than it reads for the same reason the
scrolling zero is: the fixture has no list box, so what it measures is the absence of new retained
members rather than the size of the control. The use check is the demo application, which builds a
list box in its card table and publishes with `-p:TrimmerSingleWarn=false` reporting no IL2xxx or
IL3xxx warning from the new code.

Debug symbols are excluded from every row: `.pdb` on Windows, `.dbg` on Linux, and dSYM bundles on
macOS. They are not shipped to users.

## 9. Checklist for a new feature

- [ ] Does any always-retained type (a control, a backend view, `XPlatUiHost`, `XPlatApplication`)
	  gain a member or an interface implementation? If so, why is that unavoidable?
- [ ] Does anything run at application startup or window creation? If so, is it behind an opt-in
	  hook?
- [ ] Is there any reflection, assembly scanning, attribute-driven registration or
	  `Activator.CreateInstance`?
- [ ] Is there any module initializer, or static constructor or static state on a shared type?
- [ ] If the feature gates per-window or per-control work on a runtime value, can the application
	  change that value after the window is shown? If so, the work has to happen unconditionally.
- [ ] Is the feature's code reachable only from the opt-in call, and therefore trimmed when the call
	  is absent?
- [ ] Was the publish size measured for an app that does not use it, on all three platforms, and
	  quoted in the pull request?
- [ ] Were the trim/AOT warnings for the new code reviewed?
- [ ] If it could not be made opt-in, is it in a separate package instead, with the reason recorded?
- [ ] If the feature wants members on `XPlatControl` or a subclass, is it exposed as extension
	  members in its own namespace instead, with the exception stated if not?
- [ ] If it needs events, does it expose them as `AddXHandler`/`RemoveXHandler` extension methods?
- [ ] If it is native, is the implementation in each backend or in the feature's platform folder,
	  is the registration a same-named extension in the feature's namespace, and does each backend's
	  always-on path name none of it? Does `Headless` stay free of the extension?
- [ ] If a hook needs the concrete backend, is it a typed hook (§6.5) rather than a downcast, and is
	  it unrelated to the portable interface?
- [ ] Is there any C# `event` keyword in the change, including on interfaces and backend types? It
	  has to be an `AddXHandler`/`RemoveXHandler` pair instead (§5.4).
- [ ] If it ships as its own package, does every platform get a `lib/` folder and a dependency
	  group, is the group the platform's whole closure, and is an empty group present where a
	  platform needs no extra packages?
- [ ] If it ships as its own package, does the `net10.0` assembly carry the fallback guard (§4), and
	  is it in a platform folder with other code rather than alone?
- [ ] If it needs per-control state, where does that state live, and what does each access cost?

## 10. Non-goals

- Retrofitting existing features. The policy applies to new work; menus, dialogs, timers and single
  instance keep their current shape until there is a reason to revisit them.
- Making an API awkward to hit a byte target. If the zero-cost shape hurts the API, say so, take
  rung 2 or 3, and record the reason.
- A promise of literally zero cost. The standard is bare minimal, measured, and justified.
- A trimming/AOT test suite. The checks in §8 are a publish comparison and a warning review run as
  each feature lands, not a new CI project.