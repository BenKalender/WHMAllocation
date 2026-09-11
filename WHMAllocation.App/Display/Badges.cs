using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Results;

namespace WHMAllocation.App.Display;

/// <summary>
/// Maps domain values onto the theme's Bootstrap badge and alert classes, so the pages do not
/// each repeat the mapping.
/// </summary>
public static class Badges
{
    public static string ForStatus(OrderStatus status) => status switch
    {
        OrderStatus.Released => "text-bg-info",
        OrderStatus.Allocated => "text-bg-success",
        OrderStatus.PartiallyAllocated => "text-bg-warning",
        OrderStatus.Cancelled => "text-bg-secondary",
        _ => "text-bg-light"
    };

    public static string StatusText(OrderStatus status) => status switch
    {
        OrderStatus.PartiallyAllocated => "Partially Allocated",
        _ => status.ToString()
    };

    public static string ForPriority(Priority priority) => priority switch
    {
        Priority.High => "text-bg-danger",
        Priority.Normal => "text-bg-warning",
        Priority.Low => "text-bg-secondary",
        _ => "text-bg-light"
    };

    /// <summary>Alert styling for a completed allocation run.</summary>
    public static string ForRun(AllocationRunResult result) =>
        result.AllocatedNothing ? "alert-warning" : "alert-success";

    /// <summary>Alert styling for a cancellation: only an actual cancellation is a success.</summary>
    public static string ForCancellation(CancellationResult result) =>
        result.Succeeded ? "alert-success" : "alert-warning";

    /// <summary>
    /// Alert styling for a correction, by escalating impact: releasing an order is the most
    /// disruptive thing it can do.
    /// </summary>
    public static string ForCorrection(CorrectionResult result) => result.Outcome switch
    {
        CorrectionOutcome.SkuNotFound => "alert-danger",
        CorrectionOutcome.OrdersDeallocated => "alert-danger",
        CorrectionOutcome.ShortfallPartiallyCovered => "alert-warning",
        _ => "alert-success"
    };

    /// <summary>Human-readable summary of a correction, including what it cost.</summary>
    public static string Describe(CorrectionResult result) => result.Outcome switch
    {
        CorrectionOutcome.SkuNotFound =>
            "SKU not found.",
        CorrectionOutcome.NoShortfall =>
            $"Stock updated. {result.NewAvailableQuantity} available, no reservation affected.",
        CorrectionOutcome.ShortfallCovered =>
            $"Short by {result.Shortfall}, fully covered from substitute SKUs. No order lost stock.",
        CorrectionOutcome.ShortfallPartiallyCovered =>
            $"Short by {result.Shortfall}, {result.CoveredBySubstitutes} covered by substitutes. "
            + $"{result.DowngradedOrderIds.Count} order(s) now partially allocated.",
        CorrectionOutcome.OrdersDeallocated =>
            $"Short by {result.Shortfall}, {result.CoveredBySubstitutes} covered. "
            + $"{result.DeallocatedOrderIds.Count} complete-delivery order(s) released in full and returned to Released.",
        _ => result.Outcome.ToString()
    };

    /// <summary>Human-readable summary of a cancellation.</summary>
    public static string Describe(CancellationResult result) => result.Outcome switch
    {
        CancellationOutcome.Cancelled =>
            $"Cancelled. {result.ReleasedQuantity} unit(s) returned to stock across {result.AffectedLines} line(s).",
        CancellationOutcome.OrderNotFound => "That order no longer exists.",
        CancellationOutcome.OrderAlreadyCancelled => "This order was already cancelled. Nothing changed.",
        CancellationOutcome.OrderLineNotFound => "That order line no longer exists.",
        CancellationOutcome.OrderLineAlreadyCancelled => "This line was already cancelled. Nothing changed.",
        _ => result.Outcome.ToString()
    };

    /// <summary>Why a single order was skipped, or how far it got.</summary>
    public static string Describe(OrderAllocationOutcome outcome) => outcome switch
    {
        OrderAllocationOutcome.FullyAllocated => "Fully allocated",
        OrderAllocationOutcome.PartiallyAllocated => "Partially allocated",
        OrderAllocationOutcome.SkippedNoStock => "No stock available",
        OrderAllocationOutcome.SkippedCompleteDeliveryNotPossible => "Refused: cannot ship complete",
        OrderAllocationOutcome.SkippedNoLines => "No live lines",
        _ => outcome.ToString()
    };
}
