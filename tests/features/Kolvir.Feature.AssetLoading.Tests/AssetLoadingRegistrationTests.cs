using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// The registration and the provider: what <c>AddAssetLoading()</c> puts in the container, what
/// <c>AddAssetLoaderFactory</c> appends, and how the factories are asked
/// (dev-docs/ASSET-LOADING.md sections 2.1, 2.2 and 2.3).
/// </summary>
[TestClass]
[TestCategory("Feature")]
[TestCategory("AssetLoading")]
public sealed class AssetLoadingRegistrationTests : AssetLoadingTestBase
{
    [TestMethod]
    public void AddAssetLoadingRegistersOneManagerAndOneProvider()
    {
        ServiceProvider provider = CreateProvider(services =>
        {
            services.AddAssetLoading();
            services.AddAssetLoading();
        });

        Assert.AreSame(
            provider.GetRequiredService<IAssetLoadManager>(),
            provider.GetRequiredService<IAssetLoadManager>(),
            "one queue for the application, and calling the opt-in twice changes nothing");

        Assert.AreSame(
            provider.GetRequiredService<IAssetLoaderProvider>(),
            provider.GetRequiredService<IAssetLoaderProvider>(),
            "and one provider over the one ordered list of factories");
    }

    [TestMethod]
    public void AddAssetLoadingAloneIsALegitimateStateThatAnswersFalseForEveryUri()
    {
        ServiceProvider provider = CreateProvider(services => services.AddAssetLoading());
        IAssetLoaderProvider loaderProvider = provider.GetRequiredService<IAssetLoaderProvider>();

        Assert.IsFalse(loaderProvider.TryCreateAssetLoader<string>("https://example.invalid/a.png", out IAssetLoader<string>? loader));
        Assert.IsNull(loader);
    }

    [TestMethod]
    public void TheFirstFactoryThatClaimsTheUriWins()
    {
        var first = new StubAssetLoaderFactory<string>();
        var second = new StubAssetLoaderFactory<string>();

        ServiceProvider provider = CreateProvider(services =>
        {
            services.AddAssetLoading();
            services.AddAssetLoaderFactory(first);
            services.AddAssetLoaderFactory(second);
        });

        IAssetLoaderProvider loaderProvider = provider.GetRequiredService<IAssetLoaderProvider>();

        Assert.IsTrue(loaderProvider.TryCreateAssetLoader("any", out IAssetLoader<string>? loader));
        Assert.IsNotNull(loader);
        Assert.AreEqual(1, first.CreateCount, "the first claim wins");
        Assert.AreEqual(0, second.CreateCount, "and the factories behind it are not asked");
    }

    [TestMethod]
    public void AFactoryThatDeclinesLetsTheNextOneAnswer()
    {
        var first = new StubAssetLoaderFactory<string>("first://");
        var second = new StubAssetLoaderFactory<string>("second://");

        ServiceProvider provider = CreateProvider(services =>
        {
            services.AddAssetLoading();
            services.AddAssetLoaderFactory(first);
            services.AddAssetLoaderFactory(second);
        });

        IAssetLoaderProvider loaderProvider = provider.GetRequiredService<IAssetLoaderProvider>();

        Assert.IsTrue(loaderProvider.TryCreateAssetLoader("second://a", out IAssetLoader<string>? loader));
        Assert.IsNotNull(loader);
        CollectionAssert.AreEqual(new[] { "second://a" }, first.Asked, "registration order is the order they are asked");
        CollectionAssert.AreEqual(new[] { "second://a" }, second.Asked);
        Assert.AreEqual(0, first.CreateCount);
        Assert.AreEqual(1, second.CreateCount);
    }

    [TestMethod]
    public void ATypedFactoryForwardsTheUntypedQuestionByExactTypeEquality()
    {
        var factory = new StubAssetLoaderFactory<object>();

        ServiceProvider provider = CreateProvider(services =>
        {
            services.AddAssetLoading();
            services.AddAssetLoaderFactory(factory);
        });

        IAssetLoaderProvider loaderProvider = provider.GetRequiredService<IAssetLoaderProvider>();

        Assert.IsTrue(loaderProvider.TryCreateAssetLoader<object>("any", out IAssetLoader<object>? exact));
        Assert.IsNotNull(exact, "the type it produces is the type that matches");

        // Not assignability: a factory producing object would otherwise claim every request in the
        // application, and deciding assignability needs reflection over the type.
        Assert.IsFalse(loaderProvider.TryCreateAssetLoader<string>("any", out IAssetLoader<string>? derived));
        Assert.IsNull(derived);
        CollectionAssert.AreEqual(
            new[] { "any" },
            factory.Asked,
            "the equality test comes before the URI logic, so a mismatched type never reaches it");
    }

    [TestMethod]
    public void AFactoryThatAnswersTheUntypedMemberItselfSeesTheResultType()
    {
        var factory = new TypedPredicateFactory();

        ServiceProvider provider = CreateProvider(services =>
        {
            services.AddAssetLoading();
            services.AddAssetLoaderFactory(factory);
        });

        IAssetLoaderProvider loaderProvider = provider.GetRequiredService<IAssetLoaderProvider>();

        Assert.IsTrue(loaderProvider.TryCreateAssetLoader<int>("number", out IAssetLoader<int>? number));
        Assert.IsNotNull(number);
        Assert.IsFalse(loaderProvider.TryCreateAssetLoader<string>("number", out _), "the same URI is refused for another type");
    }

    [TestMethod]
    public void AFactoryThatClaimsATypeAndHandsBackTheWrongLoaderFailsLoudly()
    {
        ServiceProvider provider = CreateProvider(services =>
        {
            services.AddAssetLoading();
            services.AddAssetLoaderFactory(new MismatchedFactory());
        });

        IAssetLoaderProvider loaderProvider = provider.GetRequiredService<IAssetLoaderProvider>();

        Action ask = () => loaderProvider.TryCreateAssetLoader<int>("number", out _);
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(ask);

        StringAssert.Contains(thrown.Message, nameof(MismatchedFactory));
    }

    [TestMethod]
    public void TheRegisteredFactoriesAreAskingInRegistrationOrder()
    {
        var first = new StubAssetLoaderFactory<string>();
        var second = new StubAssetLoaderFactory<string>();
        var third = new TypedPredicateFactory();

        ServiceProvider provider = CreateProvider(services =>
        {
            services.AddAssetLoaderFactory(first);
            services.AddAssetLoaderFactory(second);
            services.AddAssetLoaderFactory(third);
        });

        CollectionAssert.AreEqual(
            new IAssetLoaderFactory[] { first, second, third },
            provider.GetServices<IAssetLoaderFactory>().ToArray(),
            "the order the application registered them in is the order the provider asks, with nothing sorting it");
    }
}
