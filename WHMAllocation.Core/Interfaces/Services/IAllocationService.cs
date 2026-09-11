using WHMAllocation.Core.Results;

namespace WHMAllocation.Core.Interfaces.Services;

public interface IAllocationService
{
    /// <summary>
    /// Allocates stock to every released order, highest priority first and FIFO within a
    /// priority.
    /// </summary>
    /// <returns>
    /// Per-order outcomes and run totals, so the caller can report what happened without
    /// re-querying.
    /// </returns>
    Task<AllocationRunResult> AllocateReleasedOrdersAsync();
}
