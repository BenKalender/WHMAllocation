namespace WHMAllocation.Core.Results;

/// <summary>
/// Why a single order ended up the way it did during an allocation run.
/// </summary>
public enum OrderAllocationOutcome
{
    /// <summary>Every requested unit was reserved.</summary>
    FullyAllocated = 1,

    /// <summary>Some units were reserved, but not all that were asked for.</summary>
    PartiallyAllocated = 2,

    /// <summary>No stock was available at all; the order is untouched and stays Released.</summary>
    SkippedNoStock = 3,

    /// <summary>
    /// The order requires complete delivery and at least one of its products could not be
    /// covered in full, so deliberately nothing was reserved.
    /// </summary>
    SkippedCompleteDeliveryNotPossible = 4,

    /// <summary>The order has no live lines — it is empty, or every line is cancelled.</summary>
    SkippedNoLines = 5
}

/// <summary>What happened to one order during an allocation run.</summary>
public sealed record OrderAllocationResult(
    Guid OrderId,
    string OrderNumber,
    OrderAllocationOutcome Outcome,
    int RequestedQuantity,
    int AllocatedQuantity)
{
    /// <summary>True when nothing was reserved and the order was left as it was.</summary>
    public bool IsSkipped =>
        Outcome is OrderAllocationOutcome.SkippedNoStock
            or OrderAllocationOutcome.SkippedCompleteDeliveryNotPossible
            or OrderAllocationOutcome.SkippedNoLines;
}

/// <summary>
/// Summary of an allocation run, so a caller can report what happened rather than having to
/// re-query the orders to find out.
/// </summary>
public sealed record AllocationRunResult(IReadOnlyList<OrderAllocationResult> Orders)
{
    public static AllocationRunResult Empty { get; } = new([]);

    public int OrdersProcessed => Orders.Count;

    public int FullyAllocatedCount =>
        Orders.Count(x => x.Outcome == OrderAllocationOutcome.FullyAllocated);

    public int PartiallyAllocatedCount =>
        Orders.Count(x => x.Outcome == OrderAllocationOutcome.PartiallyAllocated);

    public int SkippedCount => Orders.Count(x => x.IsSkipped);

    public int TotalQuantityAllocated => Orders.Sum(x => x.AllocatedQuantity);

    /// <summary>True when the run reserved nothing at all — worth surfacing differently to a user.</summary>
    public bool AllocatedNothing => TotalQuantityAllocated == 0;
}
