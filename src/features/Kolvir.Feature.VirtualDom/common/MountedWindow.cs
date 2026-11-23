namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// One window node, the real window it owns, and the content mounted inside it.
/// </summary>
/// <remarks>
/// The application reconciler's record of what is currently on screen. It keeps the latest node
/// because the diff is driven by the previous node - only the node knows which properties it assigned
/// - and it keeps the pristine values captured from the window when it was created, which is what
/// makes "restore this property" answerable without a static snapshot per type.
/// </remarks>
internal sealed class MountedWindow
{
    internal MountedWindow(VWindow node, Window window, Panel host, in WindowPristine pristine)
    {
        Node = node;
        Window = window;
        Host = host;
        Pristine = pristine;
    }

    /// <summary>The node that produced this window on the latest render.</summary>
    internal VWindow Node { get; set; }

    /// <summary>The real window, which survives a patch, a move and a reorder.</summary>
    internal Window Window { get; }

    /// <summary>The panel that is the window's content and holds what the node rendered.</summary>
    internal Panel Host { get; }

    /// <summary>What the window's properties were before any node touched them.</summary>
    internal WindowPristine Pristine { get; }

    /// <summary>True while the framework itself is asking the window to close.</summary>
    internal bool ClosingFromFramework { get; set; }

    /// <summary>
    /// True while the mount is applying this window's properties from a render, and through the
    /// loop turn that completes a deferred size assignment. Geometry and state notifications raised
    /// in that window are the mount echoing its own work, so they are dropped.
    /// </summary>
    internal bool SuppressGeometryNotifications { get; set; }

    private List<IDisposable>? _notificationTokens;

    private MountHandle? NodeContent { get; set; }

    private ComponentMount? ComponentContent { get; set; }

    private IComponent? ContentComponent { get; set; }

    /// <summary>
    /// Reconciles <paramref name="content"/> into the host panel, which is the one place the
    /// application tree meets the control tree.
    /// </summary>
    /// <remarks>
    /// A plain node goes through the tree mount and is patched in place. A bare
    /// <see cref="VComponent"/> root goes through the component mount, keeping the child's own state
    /// and render count separate from the application's; the same component instance across renders
    /// keeps its mount, exactly as it would as a child of a control tree. A content root that changes
    /// shape disposes the mount it had and starts the other kind, because the two kinds of mount own
    /// their controls differently.
    /// </remarks>
    internal void UpdateContent(VNode? content)
    {
        if (content is VComponent componentNode)
        {
            if (ComponentContent is not null && ReferenceEquals(ContentComponent, componentNode.Component))
            {
                return;
            }

            DisposeContent();
            ContentComponent = componentNode.Component;
            ComponentContent = VirtualDomRuntime.Mount(Host, ContentComponent);
            return;
        }

        if (ComponentContent is not null)
        {
            DisposeContent();
        }

        if (content is null)
        {
            DisposeContent();
            return;
        }

        if (NodeContent is null)
        {
            NodeContent = VirtualDomRuntime.Mount(Host, content);
        }
        else
        {
            NodeContent.Update(content);
        }
    }

    /// <summary>Keeps a window notification subscription for the life of this record.</summary>
    /// <param name="token">The subscription to release when the record ends.</param>
    internal void AddNotificationToken(IDisposable token) =>
        (_notificationTokens ??= []).Add(token);

    /// <summary>Detaches every window notification subscription this record added. Idempotent.</summary>
    internal void DisposeNotifications()
    {
        var tokens = _notificationTokens;
        _notificationTokens = null;

        if (tokens is null)
        {
            return;
        }

        foreach (var token in tokens)
        {
            token.Dispose();
        }
    }

    /// <summary>Detaches everything the content mount owns, whichever kind it is.</summary>
    internal void DisposeContent()
    {
        var nodeContent = NodeContent;
        NodeContent = null;

        var componentContent = ComponentContent;
        ComponentContent = null;
        ContentComponent = null;

        nodeContent?.Dispose();
        componentContent?.Dispose();
    }
}
