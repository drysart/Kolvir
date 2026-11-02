namespace XPlatUI.DragDrop;

/// <summary>
/// The drag and drop members a control gains by opting into the feature: whether it accepts drops,
/// and the four phases a drop target is told about.
/// </summary>
/// <remarks>
/// <para>
/// These are extension members rather than members of <see cref="XPlatControl"/>, because drag and
/// drop is a rung-3 feature: an application that never calls the backend's <c>AddDragDrop</c> gets
/// none of this code, and a control carries no state for it. The state is created the first time
/// something asks for it and lives in a table keyed by the control, so a control that never accepts
/// a drop costs nothing at all.
/// </para>
/// <para>
/// The receiver is an ordinary control, so nothing has to be created differently:
/// <code>
/// panel.AllowDrop = true;
/// panel.AddDragDropHandler((_, e) =&gt; Open(e.Data));
/// </code>
/// </para>
/// </remarks>
public static class XPlatDragDropControlExtensions
{
    extension(XPlatControl control)
    {
        /// <summary>
        /// True to let the user drop data on this control, which makes the control a candidate for
        /// the drag phases. When a control under the pointer does not accept drops, the nearest
        /// ancestor that does gets them instead, so a panel can accept drops for everything it
        /// contains.
        /// </summary>
        public bool AllowDrop
        {
            get => XPlatDragDropState.Find(control)?.AllowDrop == true;
            set => XPlatDragDropState.Get(control).AllowDrop = value;
        }

        /// <summary>
        /// Adds a handler that runs when a drag enters the control. It only runs when
        /// <see cref="AllowDrop"/> is set.
        /// </summary>
        /// <param name="handler">The handler to add.</param>
        public IDisposable AddDragEnterHandler(Action<object, XPlatDragEventArgs> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            var state = XPlatDragDropState.Get(control);
            state.AddDragEnter(handler);
            return new XPlatHandlerToken<Action<object, XPlatDragEventArgs>>(
                state.RemoveDragEnter,
                handler);
        }

        /// <summary>Removes a handler that <see cref="AddDragEnterHandler"/> added.</summary>
        /// <param name="handler">The handler to remove.</param>
        public void RemoveDragEnterHandler(Action<object, XPlatDragEventArgs> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            XPlatDragDropState.Find(control)?.RemoveDragEnter(handler);
        }

        /// <summary>
        /// Adds a handler that runs as a drag moves over the control. It only runs when
        /// <see cref="AllowDrop"/> is set.
        /// </summary>
        /// <param name="handler">The handler to add.</param>
        public IDisposable AddDragOverHandler(Action<object, XPlatDragEventArgs> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            var state = XPlatDragDropState.Get(control);
            state.AddDragOver(handler);
            return new XPlatHandlerToken<Action<object, XPlatDragEventArgs>>(
                state.RemoveDragOver,
                handler);
        }

        /// <summary>Removes a handler that <see cref="AddDragOverHandler"/> added.</summary>
        /// <param name="handler">The handler to remove.</param>
        public void RemoveDragOverHandler(Action<object, XPlatDragEventArgs> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            XPlatDragDropState.Find(control)?.RemoveDragOver(handler);
        }

        /// <summary>
        /// Adds a handler that runs when a drag leaves the control or the drag is cancelled.
        /// </summary>
        /// <param name="handler">The handler to add.</param>
        public IDisposable AddDragLeaveHandler(Action<object> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            var state = XPlatDragDropState.Get(control);
            state.AddDragLeave(handler);
            return new XPlatHandlerToken<Action<object>>(state.RemoveDragLeave, handler);
        }

        /// <summary>Removes a handler that <see cref="AddDragLeaveHandler"/> added.</summary>
        /// <param name="handler">The handler to remove.</param>
        public void RemoveDragLeaveHandler(Action<object> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            XPlatDragDropState.Find(control)?.RemoveDragLeave(handler);
        }

        /// <summary>
        /// Adds a handler that runs when the user releases a drag over the control. It only runs
        /// when <see cref="AllowDrop"/> is set.
        /// </summary>
        /// <param name="handler">The handler to add.</param>
        public IDisposable AddDragDropHandler(Action<object, XPlatDragEventArgs> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            var state = XPlatDragDropState.Get(control);
            state.AddDragDrop(handler);
            return new XPlatHandlerToken<Action<object, XPlatDragEventArgs>>(
                state.RemoveDragDrop,
                handler);
        }

        /// <summary>Removes a handler that <see cref="AddDragDropHandler"/> added.</summary>
        /// <param name="handler">The handler to remove.</param>
        public void RemoveDragDropHandler(Action<object, XPlatDragEventArgs> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            XPlatDragDropState.Find(control)?.RemoveDragDrop(handler);
        }
    }
}
