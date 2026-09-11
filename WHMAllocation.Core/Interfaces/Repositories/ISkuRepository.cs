using WHMAllocation.Core.Entities;

namespace WHMAllocation.Core.Interfaces.Repositories;

public interface ISkuRepository : IBaseRepository<Sku>
{
    Task<List<Sku>> GetAvailableSkusByProductAsync(Guid productId);

    /// <summary>
    /// Every SKU with its product and warehouse location, including those in locked locations
    /// and those that have run dry. Read-side query for the stock screen.
    /// </summary>
    Task<List<Sku>> GetAllAsync();
}