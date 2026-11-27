using System.Net;
using System.Net.Http;

namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>
/// One request the fake transport was handed, kept so a test can answer it and inspect what the
/// loader sent.
/// </summary>
/// <remarks>
/// It registers with the request's cancellation token, so aborting a fetch gives this a cancelled
/// <see cref="Task"/> exactly as a real socket would: the response never arrives and the await it was
/// holding throws. That is what lets "the last cancellation aborts the request" be asserted without
/// a real connection. A test can turn that off
/// (<see cref="FakeHttpMessageHandler.ObserveCancellation"/>) to hold a fetch in the window between
/// its last handle leaving and the transport unwinding.
/// </remarks>
internal sealed class FakeRequest
{
    private readonly TaskCompletionSource<HttpResponseMessage> _response = new();

    /// <summary>Captures the request before the loader disposes it.</summary>
    /// <param name="request">What the loader sent.</param>
    /// <param name="token">The token the request was sent with.</param>
    /// <param name="observeCancellation">
    /// True to stop when the token is cancelled, the way a real transport does; false to leave the
    /// request pending so a test can hold a fetch in the aborting state.
    /// </param>
    public FakeRequest(HttpRequestMessage request, CancellationToken token, bool observeCancellation)
    {
        Uri = request.RequestUri;
        Method = request.Method;
        UserAgent = request.Headers.TryGetValues("User-Agent", out IEnumerable<string>? values)
            ? string.Join(", ", values)
            : null;
        Token = token;

        if (!observeCancellation)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            _response.TrySetCanceled(token);
        }
        else
        {
            token.Register(static state => ((FakeRequest)state!).Cancel(), this);
        }
    }

    /// <summary>Where the request went.</summary>
    public Uri? Uri { get; }

    /// <summary>The method it used.</summary>
    public HttpMethod Method { get; }

    /// <summary>The raw <c>User-Agent</c> header, or null when none was sent.</summary>
    public string? UserAgent { get; }

    /// <summary>The token the loader sent the request with, which is the fetch's own.</summary>
    public CancellationToken Token { get; }

    /// <summary>The response the loader is waiting for.</summary>
    public Task<HttpResponseMessage> Response => _response.Task;

    /// <summary>Answers the request, which resumes the loader on this thread.</summary>
    /// <param name="status">The status code to answer with.</param>
    /// <param name="body">The body to answer with.</param>
    public void Respond(HttpStatusCode status, byte[] body) =>
        _response.TrySetResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });

    /// <summary>Stops the request the way an aborted connection does.</summary>
    private void Cancel() => _response.TrySetCanceled(Token);
}

/// <summary>
/// The transport every test uses: a message handler that records what it was asked for and answers
/// when the test says so, or immediately when the test asked for that.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly object _sync = new();
    private readonly List<FakeRequest> _requests = [];
    private readonly Dictionary<int, TaskCompletionSource> _countWaiters = new();

    /// <summary>The body every automatic answer carries.</summary>
    public byte[] Body { get; set; } = TestPng.Bytes;

    /// <summary>The status every automatic answer uses.</summary>
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    /// <summary>True to answer as soon as the request arrives; false to wait for the test.</summary>
    public bool RespondAutomatically { get; set; } = true;

    /// <summary>When set, the request fails with this instead of producing a response.</summary>
    public Exception? SendError { get; set; }

    /// <summary>
    /// True to stop a request when its token is cancelled; false to leave it pending, so a test can
    /// hold a fetch in the state between its last handle leaving and the transport unwinding.
    /// </summary>
    public bool ObserveCancellation { get; set; } = true;

    /// <summary>The requests, in the order the loader made them.</summary>
    public IReadOnlyList<FakeRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>How many requests the loader has made.</summary>
    public int Count => Requests.Count;

    /// <summary>Answers one recorded request.</summary>
    /// <param name="index">Which request, in arrival order.</param>
    public void Respond(int index) => Requests[index].Respond(StatusCode, Body);

    /// <summary>Answers every request that is still waiting.</summary>
    public void RespondToEverything()
    {
        for (int index = 0; index < Count; index++)
        {
            Requests[index].Respond(StatusCode, Body);
        }
    }

    /// <summary>
    /// Completes when the loader has made <paramref name="count"/> requests.
    /// </summary>
    /// <param name="count">How many requests to wait for.</param>
    /// <returns>A task the transport completes when the request arrives.</returns>
    /// <remarks>
    /// This is the suite's substitute for a timer: a test that expects a queued request to start
    /// awaits the transport seeing it, which is the event itself rather than an amount of time that
    /// is usually long enough.
    /// </remarks>
    public Task WaitForCountAsync(int count)
    {
        lock (_sync)
        {
            if (_requests.Count >= count)
            {
                return Task.CompletedTask;
            }

            if (!_countWaiters.TryGetValue(count, out TaskCompletionSource? waiter))
            {
                waiter = new TaskCompletionSource();
                _countWaiters.Add(count, waiter);
            }

            return waiter.Task;
        }
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var pending = new FakeRequest(request, cancellationToken, ObserveCancellation);

        lock (_sync)
        {
            _requests.Add(pending);

            foreach (int count in _countWaiters.Keys.Where(count => _requests.Count >= count).ToArray())
            {
                _countWaiters[count].TrySetResult();
                _countWaiters.Remove(count);
            }
        }

        if (SendError is not null)
        {
            return Task.FromException<HttpResponseMessage>(SendError);
        }

        if (RespondAutomatically)
        {
            pending.Respond(StatusCode, Body);
        }

        return pending.Response;
    }
}
