using NotchWin.Core.Models;
using NotchWin.Core.Pricing;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class PricingCatalogTests
{
    [Fact]
    public void MatchesDatedModelIds_ByPrefix()
    {
        var catalog = new PricingCatalog();

        var price = catalog.TryGetPrice("claude-sonnet-4-20250514");

        Assert.NotNull(price);
    }

    [Fact]
    public void ReturnsNull_ForUnknownModel()
    {
        var catalog = new PricingCatalog();

        Assert.Null(catalog.TryGetPrice("some-future-model-nobody-has-priced-yet"));
        Assert.Null(catalog.EstimateCostUsd(new TokenUsage(1000, 1000), "some-future-model-nobody-has-priced-yet"));
    }

    [Fact]
    public void UserOverride_TakesPrecedenceOverDefault()
    {
        var overrides = new Dictionary<string, ModelPrice>
        {
            ["claude-sonnet-4"] = new ModelPrice(InputPerMillion: 1, OutputPerMillion: 2),
        };
        var catalog = new PricingCatalog(overrides);

        var cost = catalog.EstimateCostUsd(new TokenUsage(Input: 1_000_000, Output: 1_000_000), "claude-sonnet-4-20250514");

        Assert.Equal(3m, cost); // 1,000,000 input @ $1/M + 1,000,000 output @ $2/M = $3
    }

    [Fact]
    public void LoadFrom_MissingFile_FallsBackToDefaults()
    {
        var catalog = PricingCatalog.LoadFrom(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json"));

        Assert.NotNull(catalog.TryGetPrice("claude-sonnet-4"));
    }
}
