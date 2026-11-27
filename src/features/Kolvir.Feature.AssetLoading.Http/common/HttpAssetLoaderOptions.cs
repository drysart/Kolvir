namespace Kolvir.Feature.AssetLoading.Http;

/// <summary>
/// The settings an application gives the HTTP asset loader: how many requests may be in flight, how
/// long one may take, who it says it is, and which transport it uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>One object, registered once, read while it runs.</b> It is a mutable object rather than a
/// snapshot of settings, because the limits are the point of the feature and a limit is only worth
/// having if it can be raised while the application is running
/// (dev-docs/ASSET-LOADING.md section 4.2). The loader resolves this instance from dependency
/// injection and reads it on every walk and every fetch, so a write is visible to the next decision
/// rather than to the next restart. An application that wants one line to raise a limit keeps the
/// instance it registered and assigns to it:
/// </para>
/// <code>
/// var http = new HttpAssetLoaderOptions { MaxConcurrentRequests = 8 };
/// services.AddHttpAssetLoading(http);
///
/// // Later, from anywhere: the queued work starts moving on the next walk.
/// http.MaxConcurrentRequests = 16;
/// </code>
/// <para>
/// <b>Threading.</b> There is no expectation that the writer is on the UI thread. The two limits are
/// written by whoever changed them, read by the UI thread's walk and by pool threads deciding
/// whether a fetch may start, and released by whichever thread finished a fetch. Every property is
/// guarded by one private lock, so a read never sees half of a change; raising a limit releases the
/// lock before notifying, so a handler that calls back into this object cannot deadlock against it.
/// A limit that is <em>lowered</em> is also written under the same lock and notifies nobody: the
/// capacity count is already above it, and everything in flight stays in flight.
/// </para>
/// <para>
/// <b>What is read when.</b> The two limits are read at each decision, so a change takes effect
/// immediately. <see cref="Timeout"/> and <see cref="UserAgent"/> are read when a fetch starts, so a
/// change applies to the next fetch rather than to one already running. <see cref="HttpClient"/> and
/// <see cref="MessageHandler"/> select the transport, and the transport is chosen once, on the first
/// fetch: swapping a message handler under requests it is carrying cannot be done without losing
/// them, so this is a startup setting by construction rather than by a rule the loader enforces
/// later.
/// </para>
/// </remarks>
public sealed class HttpAssetLoaderOptions
{
    /// <summary>
    /// Twelve. Six is the per-host connection count every HTTP/1.1 browser has used for decades,
    /// and a page in this library usually talks to two hosts: the one it was served from and one CDN.
    /// Two hosts' worth is therefore the first number that does not artificially serialise a normal
    /// page, and any application with more than two hosts is the case this limit exists to protect
    /// against rather than the case it should throttle. It is above the per-host default on purpose:
    /// the total is the backstop, not the working limit.
    /// </summary>
    internal const int DefaultMaxConcurrentRequests = 12;

    /// <summary>
    /// Six, which is the number an HTTP/1.1 browser opens to one origin and what a server is
    /// provisioned for. HTTP/2 multiplexes many requests over one connection, so this is a limit on
    /// requests in flight rather than on sockets, but the server's work is still per request and six
    /// is the figure that has been found not to starve one server while a page loads.
    /// </summary>
    internal const int DefaultMaxConcurrentRequestsPerHost = 6;

    /// <summary>
    /// Thirty seconds. <see cref="HttpClient"/>'s own default is 100 seconds because it is tuned for
    /// general API calls, and 100 seconds is far longer than an image the user is waiting for is
    /// worth holding a request slot for. Thirty is longer than a slow mobile image needs and short
    /// enough that a stalled server gives its slot back within a scroll rather than within a coffee
    /// break.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The product token the loader sends, so a server or a CDN that refuses a request with no
    /// <c>User-Agent</c> - which many do - gets one that names this library and its version.
    /// </summary>
    internal static readonly string DefaultUserAgent = CreateDefaultUserAgent();

    private readonly object _sync = new();

    private int _maxConcurrentRequests = DefaultMaxConcurrentRequests;
    private int _maxConcurrentRequestsPerHost = DefaultMaxConcurrentRequestsPerHost;
    private TimeSpan _timeout = DefaultTimeout;
    private string? _userAgent = DefaultUserAgent;

    /// <summary>
    /// Invoked after a limit was raised, so the factory can tell the handles that are still waiting
    /// to ask again. Subscribed by the factory, never by an application, and deliberately not an
    /// <c>event</c>: this is internal wiring, not a public notification surface
    /// (FEATURE-COST-POLICY.md section 5.4).
    /// </summary>
    internal Action? LimitsRaised;

    /// <summary>
    /// The most fetches this loader allows in flight across every host at once. Twelve by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <b>fetch</b>, not a handle: two controls asking for the same URI share one request and
    /// therefore consume one slot, which is why the limit counts what the loader is holding rather
    /// than what the manager is holding (dev-docs/ASSET-LOADING.md section 3.1).
    /// </para>
    /// <para>
    /// Raising it admits work that is already queued, without a restart. The loader notifies every
    /// waiting handle that its answer may have changed, the manager re-walks its queue, and the
    /// requests that now fit start. Lowering it admits nothing new and aborts nothing that is
    /// running: a fetch already in flight keeps its slot until it ends, and the next admission waits
    /// for the count to fall back under the new limit. Zero is legal and pauses new admissions while
    /// running work finishes.
    /// </para>
    /// <para>
    /// It is read on every decision and written by whoever changes it, possibly from another thread.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxConcurrentRequests
    {
        get
        {
            lock (_sync)
            {
                return _maxConcurrentRequests;
            }
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            bool raised;

            lock (_sync)
            {
                raised = value > _maxConcurrentRequests;
                _maxConcurrentRequests = value;
            }

            if (raised)
            {
                NotifyLimitsRaised();
            }
        }
    }

    /// <summary>
    /// The most fetches this loader allows in flight to one host at once. Six by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The key is the host, not the origin.</b> An origin is scheme plus host plus port, and the
    /// limit exists to protect a server from an application that scrolls faster than the server can
    /// answer. The same machine is the same machine whether it is read over <c>http</c> or
    /// <c>https</c>, and whether a test server listens on one port or another; keying on the origin
    /// would let an application double the load on one server simply by mixing schemes, which is the
    /// opposite of what the limit is for. The host is also what a server, a proxy and a rate limiter
    /// see, so it is the unit the number can be reasoned about with.
    /// </para>
    /// <para>
    /// Raising it admits that host's queued work once the manager re-walks; the loader notifies every
    /// waiting handle, because a per-host cap is one number that applies to every host, and a handle
    /// on another host is left queued by its own gate if the total is what is stopping it. Lowering
    /// it admits nothing new and aborts nothing in flight. Zero pauses new admissions for every host.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxConcurrentRequestsPerHost
    {
        get
        {
            lock (_sync)
            {
                return _maxConcurrentRequestsPerHost;
            }
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            bool raised;

            lock (_sync)
            {
                raised = value > _maxConcurrentRequestsPerHost;
                _maxConcurrentRequestsPerHost = value;
            }

            if (raised)
            {
                NotifyLimitsRaised();
            }
        }
    }

    /// <summary>
    /// How long one fetch may take before it is cancelled and its callers are failed with a
    /// <see cref="TimeoutException"/>. Thirty seconds by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is applied by the loader rather than by the <see cref="HttpClient"/> it builds, so a client
    /// the application supplied can keep its own timeout and the shorter of the two wins. The value
    /// is read when a fetch starts; a change applies to the next fetch, because shortening the
    /// timeout of a request already on the wire would be a different feature.
    /// </para>
    /// <para>
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> turns the timeout off, which is the one
    /// way to do it: a zero or negative interval is refused rather than guessed at, because "no
    /// timeout" and "instant timeout" are too easy to confuse.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and is not
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.</exception>
    public TimeSpan Timeout
    {
        get
        {
            lock (_sync)
            {
                return _timeout;
            }
        }

        set
        {
            if (value != System.Threading.Timeout.InfiniteTimeSpan && value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The timeout must be positive, or System.Threading.Timeout.InfiniteTimeSpan to turn it off.");
            }

            lock (_sync)
            {
                _timeout = value;
            }
        }
    }

    /// <summary>
    /// The <c>User-Agent</c> the loader sends on requests it makes itself, or null to send none.
    /// Defaults to this package's name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is sent as a raw header value, not parsed into products, so an application can put its own
    /// application name in front without matching a grammar. A value containing a carriage return or
    /// a line feed is refused: a header value is one line, and letting a newline through is how a
    /// request is split.
    /// </para>
    /// <para>
    /// It applies to the client the loader creates, including one created over
    /// <see cref="MessageHandler"/>. An application that supplies <see cref="HttpClient"/> has already
    /// decided its own default headers, so the loader does not add a second <c>User-Agent</c> to
    /// requests that client sends.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The value is empty or contains a line break.</exception>
    public string? UserAgent
    {
        get
        {
            lock (_sync)
            {
                return _userAgent;
            }
        }

        set
        {
            if (value is not null)
            {
                if (value.Length == 0)
                {
                    throw new ArgumentException("The user agent must be null or a non-empty value.", nameof(value));
                }

                if (value.Contains('\r') || value.Contains('\n'))
                {
                    throw new ArgumentException("The user agent must be one header line.", nameof(value));
                }
            }

            lock (_sync)
            {
                _userAgent = value;
            }
        }
    }

    /// <summary>
    /// An <see cref="HttpClient"/> the application already has, which the loader uses instead of
    /// creating one. Null by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is used as it is: its base address, default headers, credentials, proxy and
    /// <see cref="HttpClient.Timeout"/> are the application's decisions, and the loader only sends
    /// absolute request URIs through it. Its lifetime stays the application's - the loader never
    /// disposes a client it did not create. Set it, or <see cref="MessageHandler"/>, or neither;
    /// setting both is an error the first fetch reports, because there would be no way to tell which
    /// one was meant.
    /// </para>
    /// <para>
    /// The transport is chosen once, on the first fetch, so this is a registration-time setting even
    /// though the object is mutable.
    /// </para>
    /// </remarks>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> the loader builds its own <see cref="HttpClient"/> over:
    /// the seam for a proxy, a test transport, or a delegating handler that adds authentication.
    /// Null by default, which uses the platform's default handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A proxy is supplied here</b>: <c>new HttpClientHandler { Proxy = proxy }</c>. It is not a
    /// property of its own because a handler already has one, and a second way to say the same thing
    /// would have to define what happens when both are set. The handler's lifetime stays the
    /// application's: the client the loader creates over it is told not to dispose it.
    /// </para>
    /// <para>
    /// It is what lets the suite run with no network at all - a fake handler returning canned
    /// responses is the transport - and it is therefore a supported configuration rather than a test
    /// hook. Like <see cref="HttpClient"/>, it is read once, on the first fetch.
    /// </para>
    /// </remarks>
    public HttpMessageHandler? MessageHandler { get; set; }

    /// <summary>Tells the factory that a cap went up, so the waiting handles can be re-asked.</summary>
    private void NotifyLimitsRaised()
    {
        // Outside the lock: the handler re-reads the limits (the walk asks CanLoadNow) and sleeping
        // under this lock is how a change notification turns into a stall.
        LimitsRaised?.Invoke();
    }

    /// <summary>Composes the default User-Agent from this package's own assembly metadata.</summary>
    /// <remarks>
    /// The version is read from the assembly's name rather than from an attribute, because assembly
    /// metadata is always present under trimming whereas an attribute lookup is a reflection call
    /// the trimmer has to be told to keep.
    /// </remarks>
    private static string CreateDefaultUserAgent()
    {
        Version? version = typeof(HttpAssetLoaderOptions).Assembly.GetName().Version;

        return version is null
            ? "Kolvir.Feature.AssetLoading.Http"
            : $"Kolvir.Feature.AssetLoading.Http/{version.Major}.{version.Minor}";
    }
}
