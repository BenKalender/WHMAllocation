using WHMAllocation.Core.Enums;

namespace WHMAllocation.Core.Results;

/// <summary>
/// The distinct ways a cancellation request can end. These were previously indistinguishable:
/// every one of them returned silently.
/// </summary>
public enum CancellationOutcome
{
    /// <summary>The order or line was cancelled and its stock released.</summary>
    Cancelled = 1,

    /// <summary>No order exists with the given id.</summary>
    OrderNotFound = 2,

    /// <summary>The order was already cancelled; nothing was changed and no stock moved.</summary>
    OrderAlreadyCancelled = 3,

    /// <summary>No line exists with the given id.</summary>
    OrderLineNotFound = 4,

    /// <summary>The line was already cancelled; nothing was changed and no stock moved.</summary>
    OrderLineAlreadyCancelled = 5
}

/// <summary>Outcome of cancelling an order or a single line.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="ReleasedQuantity">Units returned to stock. Zero for every non-success outcome.</param>
/// <param name="AffectedLines">How many lines were cancelled.</param>
/// <param name="FinalStatus">The order's status afterwards, or <c>null</c> when the order was not found.</param>
public sealed record CancellationResult(
    CancellationOutcome Outcome,
    int ReleasedQuantity,
    int AffectedLines,
    OrderStatus? FinalStatus)
{
    public bool Succeeded => Outcome == CancellationOutcome.Cancelled;

    /// <summary>
    /// A request that changed nothing. Kept distinct from success so a caller can tell
    /// "already cancelled" from "just cancelled".
    /// </summary>
    public static CancellationResult NoChange(CancellationOutcome outcome, OrderStatus? finalStatus = null) =>
        new(outcome, ReleasedQuantity: 0, AffectedLines: 0, finalStatus);
}
