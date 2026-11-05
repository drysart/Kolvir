namespace XPlatUI.Animations;

/// <summary>
/// Where an <see cref="XPlatAnimationSet"/> is in its life.
/// </summary>
public enum XPlatAnimationState
{
    /// <summary>
    /// Nothing has run yet: no value has been assigned and no frame has been asked for.
    /// </summary>
    NotStarted,

    /// <summary>
    /// The timeline is advancing. It is attached to the scheduler, so the application is running a
    /// frame clock.
    /// </summary>
    Running,

    /// <summary>
    /// The timeline is frozen at the point it was paused. It is detached from the scheduler, so if
    /// it was the last running set the frame clock has stopped.
    /// </summary>
    Paused,

    /// <summary>
    /// The timeline reached its end, was ended early, or was disposed. Every animation is at its
    /// final value.
    /// </summary>
    Finished,
}
