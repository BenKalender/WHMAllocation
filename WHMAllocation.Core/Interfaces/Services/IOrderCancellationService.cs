using WHMAllocation.Core.Results;

namespace WHMAllocation.Core.Interfaces.Services;

public interface IOrderCancellationService
{
    /// <summary>
    /// Cancels an entire order, releasing all of its allocated stock.
    /// </summary>
    /// <returns>
    /// <see cref="CancellationOutcome.Cancelled"/> on success, or the reason nothing changed —
    /// the order was missing, or it was already cancelled.
    /// </returns>
    Task<CancellationResult> CancelOrderAsync(Guid orderId);

    /// <summary>
    /// Cancels a single order line, releasing its allocated stock and re-deriving the parent
    /// order's status.
    /// </summary>
    /// <returns>
    /// <see cref="CancellationOutcome.Cancelled"/> on success, or the reason nothing changed —
    /// the line was missing, already cancelled, or its order was already cancelled.
    /// </returns>
    Task<CancellationResult> CancelOrderLineAsync(Guid orderLineId);
}
