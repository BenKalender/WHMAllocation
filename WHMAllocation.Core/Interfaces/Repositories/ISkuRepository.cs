using WHMAllocation.Core.Entities;

namespace WHMAllocation.Core.Interfaces.Repositories;

public interface ISkuRepository : IBaseRepository<Sku>
{
    Task<List<Sku>> GetAvailableSkusByProductAsync(Guid productId);
}