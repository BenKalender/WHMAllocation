using WHMAllocation.Core.Entities;

namespace WHMAllocation.Core.Interfaces.Repositories;

public interface IOrderRepository : IBaseRepository<Order>
{
    Task<List<Order>> GetReleasedOrdersAsync();

    /// <summary>
    /// Loads the order owning <paramref name="orderLineId"/>, including all of its
    /// lines and their allocations, so the order status can be recalculated after a
    /// single line is cancelled.
    /// </summary>
    Task<Order?> GetByOrderLineIdAsync(Guid orderLineId);
}