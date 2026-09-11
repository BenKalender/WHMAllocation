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

    /// <summary>
    /// Every order with its lines and products, newest first. Read-side query for list screens.
    /// </summary>
    Task<List<Order>> GetAllAsync();

    /// <summary>
    /// One order with the full graph a detail screen needs: lines, products, and each
    /// allocation's SKU together with the warehouse location holding it.
    /// </summary>
    Task<Order?> GetDetailAsync(Guid orderId);
}