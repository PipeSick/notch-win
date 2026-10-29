using System.Text.Json;

namespace NotchWin.Core.Pricing;

/// <summary>
/// Model -> price lookup used for the "estimated API value" numbers shown next to token counts.
/// Prices are never hardcoded as ground truth: this ships with a small set of indicative defaults
/// (accurate as of this project's last update, almost certainly stale by the time you read this)
/// and loads user overrides from a JSON file so the numbers can be kept current without a rebuild.
/// Unknown models simply get no cost estimate rather than a guessed-wrong one.
/// </summary>
public sealed class PricingCatalog
{
    private readonly Dictionary<string, ModelPrice> _prices;

    public PricingCatalog(IReadOnlyDictionary<string, ModelPrice>? overrides = null)
    {
        _prices = new Dictionary<string, ModelPrice>(DefaultPrices, StringComparer.OrdinalIgnoreCase);
        if (overrides is not null)
        {
            foreach (var (model, price) in overrides)
            {
                _prices[model] = price;
            }
        }
    }

    /// <summary>Loads a catalog from a pricing.json file (see docs/AI_AGENT_SOURCES.md for the format).</summary>
    public static PricingCatalog LoadFrom(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath))
        {
            return new PricingCatalog();
        }

        try
        {
            using var stream = File.OpenRead(jsonFilePath);
            var overrides = JsonSerializer.Deserialize<Dictionary<string, ModelPrice>>(stream);
            return new PricingCatalog(overrides);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new PricingCatalog();
        }
    }

    /// <summary>
    /// Looks up a price by exact model id first, then by longest matching prefix (model ids often
    /// carry a dated suffix, e.g. "claude-sonnet-4-5-20250929" vs. the catalog's "claude-sonnet-4-5").
    /// </summary>
    public ModelPrice? TryGetPrice(string modelId)
    {
        if (_prices.TryGetValue(modelId, out var exact))
        {
            return exact;
        }

        string? bestKey = null;
        foreach (var key in _prices.Keys)
        {
            if (modelId.StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
                (bestKey is null || key.Length > bestKey.Length))
            {
                bestKey = key;
            }
        }

        return bestKey is null ? null : _prices[bestKey];
    }

    public decimal? EstimateCostUsd(Models.TokenUsage usage, string modelId)
    {
        var price = TryGetPrice(modelId);
        if (price is null)
        {
            return null;
        }

        var cost =
            usage.Input / 1_000_000.0 * price.InputPerMillion +
            usage.Output / 1_000_000.0 * price.OutputPerMillion +
            usage.CacheWrite / 1_000_000.0 * price.CacheWritePerMillion +
            usage.CacheRead / 1_000_000.0 * price.CacheReadPerMillion;

        return (decimal)cost;
    }

    private static readonly IReadOnlyDictionary<string, ModelPrice> DefaultPrices = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        // Indicative only — edit pricing.json instead of trusting these long-term.
        ["claude-opus-4"] = new ModelPrice(InputPerMillion: 15, OutputPerMillion: 75, CacheWritePerMillion: 18.75, CacheReadPerMillion: 1.5),
        ["claude-sonnet-4"] = new ModelPrice(InputPerMillion: 3, OutputPerMillion: 15, CacheWritePerMillion: 3.75, CacheReadPerMillion: 0.3),
        ["claude-3-5-haiku"] = new ModelPrice(InputPerMillion: 0.8, OutputPerMillion: 4, CacheWritePerMillion: 1, CacheReadPerMillion: 0.08),
        ["gpt-5"] = new ModelPrice(InputPerMillion: 5, OutputPerMillion: 15),
        ["gpt-5-codex"] = new ModelPrice(InputPerMillion: 5, OutputPerMillion: 15),
        ["o4-mini"] = new ModelPrice(InputPerMillion: 1.1, OutputPerMillion: 4.4),
    };
}
