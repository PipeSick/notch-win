namespace NotchWin.Core.Pricing;

/// <summary>Price per million tokens, in USD, for one model. Any field left null is treated as 0.</summary>
public sealed record ModelPrice(
    double InputPerMillion,
    double OutputPerMillion,
    double CacheWritePerMillion = 0,
    double CacheReadPerMillion = 0);
