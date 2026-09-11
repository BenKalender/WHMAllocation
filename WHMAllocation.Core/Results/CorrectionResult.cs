namespace WHMAllocation.Core.Results;

/// <summary>
/// How far a stock correction had to go to stay consistent. Ordered by escalating impact.
/// </summary>
public enum CorrectionOutcome
{
    /// <summary>No SKU exists with the given id.</summary>
    SkuNotFound = 1,

    /// <summary>
    /// The corrected count still covers every reservation, so only availability changed.
    /// An increase always lands here.
    /// </summary>
    NoShortfall = 2,

    /// <summary>The shortfall was fully absorbed by substitute SKUs; no order lost anything.</summary>
    ShortfallCovered = 3,

    /// <summary>
    /// Substitutes could not cover the gap, and at least one order that allows partial
    /// delivery now holds less than it asked for.
    /// </summary>
    ShortfallPartiallyCovered = 4,

    /// <summary>
    /// At least one order requiring complete delivery could not be kept whole, so all of its
    /// allocations were released and it returned to Released.
    /// </summary>
    OrdersDeallocated = 5
}

/// <summary>Outcome of a manual SKU quantity correction.</summary>
/// <param name="Outcome">How far the correction had to escalate.</param>
/// <param name="NewAvailableQuantity">The SKU's available quantity afterwards.</param>
/// <param name="Shortfall">Units that were reserved but no longer physically exist. Zero when there was none.</param>
/// <param name="CoveredBySubstitutes">Units of the shortfall re-sourced from other SKUs of the same product.</param>
/// <param name="DeallocatedOrderIds">Complete-delivery orders released in full and returned to Released.</param>
/// <param name="DowngradedOrderIds">Orders moved from Allocated to PartiallyAllocated.</param>
public sealed record CorrectionResult(
    CorrectionOutcome Outcome,
    int NewAvailableQuantity,
    int Shortfall,
    int CoveredBySubstitutes,
    IReadOnlyList<Guid> DeallocatedOrderIds,
    IReadOnlyList<Guid> DowngradedOrderIds)
{
    public bool Succeeded => Outcome != CorrectionOutcome.SkuNotFound;

    /// <summary>True when orders were left holding less than they requested.</summary>
    public bool HasUncoveredShortfall => Shortfall > CoveredBySubstitutes;

    public static CorrectionResult SkuNotFound { get; } =
        new(CorrectionOutcome.SkuNotFound, 0, 0, 0, [], []);

    public static CorrectionResult NoShortfall(int newAvailableQuantity) =>
        new(CorrectionOutcome.NoShortfall, newAvailableQuantity, 0, 0, [], []);
}
