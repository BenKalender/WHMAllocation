using WHMAllocation.Core.Entities;

namespace WHMAllocation.Core.Interfaces.Repositories;

public interface IOrderRepository
    : IBaseRepository<Order>
{
    Task<List<Order>> GetReleasedOrdersAsync();
}