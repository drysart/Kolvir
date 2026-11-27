namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>
/// The options object: its documented defaults, the values it refuses, and the fact that a limit is
/// live rather than a snapshot.
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading.Http")]
public sealed class HttpAssetLoaderOptionsTests
{
    [TestMethod]
    public void TheDefaultsAreTheDocumentedOnes()
    {
        var options = new HttpAssetLoaderOptions();

        Assert.AreEqual(12, options.MaxConcurrentRequests);
        Assert.AreEqual(6, options.MaxConcurrentRequestsPerHost);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.Timeout);
        StringAssert.StartsWith(options.UserAgent!, "Kolvir.Feature.AssetLoading.Http/");
        Assert.IsNull(options.HttpClient);
        Assert.IsNull(options.MessageHandler);
    }

    [TestMethod]
    public void ALimitOfZeroIsLegalAndPausesAdmissions()
    {
        var options = new HttpAssetLoaderOptions { MaxConcurrentRequests = 0 };

        Assert.AreEqual(0, options.MaxConcurrentRequests, "zero is how an application pauses new admissions without aborting work");
    }

    [TestMethod]
    public void ANegativeLimitIsRefused()
    {
        var options = new HttpAssetLoaderOptions();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MaxConcurrentRequests = -1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MaxConcurrentRequestsPerHost = -1);
    }

    [TestMethod]
    public void ATimeoutMustBePositiveOrInfinite()
    {
        var options = new HttpAssetLoaderOptions();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.Timeout = TimeSpan.Zero);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.Timeout = TimeSpan.FromSeconds(-1));

        options.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        Assert.AreEqual(System.Threading.Timeout.InfiniteTimeSpan, options.Timeout);
    }

    [TestMethod]
    public void AUserAgentWithALineBreakIsRefused()
    {
        var options = new HttpAssetLoaderOptions();

        Assert.ThrowsExactly<ArgumentException>(() => options.UserAgent = "MyApp/1.0\r\nX-Injected: yes");
        Assert.ThrowsExactly<ArgumentException>(() => options.UserAgent = string.Empty);

        options.UserAgent = null;

        Assert.IsNull(options.UserAgent, "null is how an application sends no User-Agent at all");
    }
}
