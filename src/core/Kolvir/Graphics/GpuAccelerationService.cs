namespace Kolvir;

/// <summary>
/// The part of <see cref="IGpuAccelerationService"/> every backend answers the same way: keeping the
/// enumeration, flattening it into options, holding <see cref="Current"/> and raising
/// <see cref="CurrentChanged"/>.
/// </summary>
/// <remarks>
/// <para>
/// A backend supplies one method, <see cref="Enumerate"/>, and gets the rest: the CPU path is added
/// if the enumeration left it out, the flat option list is built once, and <see cref="Current"/> starts
/// on the CPU path and moves when a window says what it built.
/// </para>
/// <para>
/// Enumeration runs on the first read, inside a lock, and its result is kept for the life of the
/// process. <see cref="ReportCurrent"/> may be called from any thread at any time, including before
/// anything has been read.
/// </para>
/// </remarks>
public abstract class GpuAccelerationService : IGpuAccelerationService
{
    private readonly object _gate = new();
    private IReadOnlyList<GpuAccelerationType>? _types;
    private IReadOnlyList<GpuAccelerationOption>? _options;
    private GpuAccelerationOption? _current;

    /// <summary>
    /// Asks the machine what it can draw with. Called once, on the first read.
    /// </summary>
    /// <returns>
    /// The families that work here, in order of preference, with their GPUs. Leave the CPU path out:
    /// it is added whether or not it is returned.
    /// </returns>
    protected abstract IReadOnlyList<GpuAccelerationType> Enumerate();

    /// <inheritdoc />
    public IReadOnlyList<GpuAccelerationType> Types
    {
        get
        {
            lock (_gate)
            {
                Build();
                return _types!;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GpuAccelerationOption> Options
    {
        get
        {
            lock (_gate)
            {
                Build();
                return _options!;
            }
        }
    }

    /// <inheritdoc />
    public GpuAccelerationOption Current
    {
        get
        {
            lock (_gate)
            {
                Build();
                return _current!;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? CurrentChanged;

    /// <summary>
    /// Reads a value back into the option it names.
    /// </summary>
    /// <param name="value">A value such as <c>d3d:1</c>, or a value saved from an earlier run.</param>
    /// <param name="option">The matching option, or null when nothing matches.</param>
    /// <returns>
    /// False when the value names no family, or names a device this machine does not list - which is
    /// what a saved setting carried over from another machine looks like.
    /// </returns>
    public bool TryFindOption(string? value, out GpuAccelerationOption? option)
    {
        option = null;

        if (!GpuAccelerationSyntax.TryParse(value, out var kind, out _))
        {
            return false;
        }

        foreach (var type in Types)
        {
            if (type.Kind == kind)
            {
                return type.TryFindOption(value, out option);
            }
        }

        return false;
    }

    /// <summary>
    /// Records the choice a window actually built, which is what <see cref="Current"/> reports.
    /// </summary>
    /// <param name="value">
    /// The value the window is drawing with, such as <c>d3d:1</c> or <c>cpu</c>. Null, empty, or a
    /// malformed value means the window fell back to the CPU path, and is recorded as such; a value that
    /// parses but is not one of this machine's options is ignored, because the window is then on a path
    /// the service never listed and reporting it would leave a settings list with nothing to select.
    /// </param>
    /// <returns>True when <see cref="Current"/> changed.</returns>
    protected bool ReportCurrent(string? value)
    {
        GpuAccelerationOption? next = null;
        EventHandler? changed;

        lock (_gate)
        {
            Build();

            if (GpuAccelerationSyntax.TryParse(value, out _, out _))
            {
                if (TryFindOption(value, out var found))
                {
                    next = found;
                }
            }
            else
            {
                next = _options!.FirstOrDefault(static option => option.Kind == GpuAccelerationKind.Cpu);
            }

            if (next is null || next == _current)
            {
                return false;
            }

            _current = next;
            changed = CurrentChanged;
        }

        // Raised outside the lock: a handler that reads Current or builds a dialog must not meet a
        // held lock, and must not be able to deadlock the window that reported.
        changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Build()
    {
        if (_types is not null)
        {
            return;
        }

        // The CPU path is last whether the backend listed it or not, so a settings list always ends
        // with the choice that cannot fail.
        List<GpuAccelerationType> types = [.. Enumerate()];
        var cpu = types.Find(static type => type.Kind == GpuAccelerationKind.Cpu) ?? GpuAccelerationType.Cpu;
        types.RemoveAll(static type => type.Kind == GpuAccelerationKind.Cpu);
        types.Add(cpu);

        List<GpuAccelerationOption> options = [];

        foreach (var type in types)
        {
            options.AddRange(type.Options);
        }

        _types = types;
        _options = options;
        _current = options.Find(static option => option.Kind == GpuAccelerationKind.Cpu) ?? options[0];
    }
}
