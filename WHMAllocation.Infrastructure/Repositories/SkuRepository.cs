using Microsoft.EntityFrameworkCore;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Infrastructure.Persistence;

namespace WHMAllocation.Infrastructure.Repositories;

public class SkuRepository
    : BaseRepository<Sku>,
      ISkuRepository
{
    public SkuRepository(AppDbContext dbContext)
        : base(dbContext)
    {
    }

    public async Task<List<Sku>> GetAvailableSkusByProductAsync(Guid productId)
    {
        return await _dbContext.Skus
            .Include(x => x.WarehouseLocation)
            .Where(x =>
                x.ProductId == productId &&
                x.Quantity > 0 &&
                !x.WarehouseLocation.IsLocked)
            .ToListAsync();
    }

    public async Task<List<Sku>> GetAllAsync()
    {
        return await _dbContext.Skus
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.WarehouseLocation)
            .OrderBy(x => x.Product.ProductNumber)
            .ThenBy(x => x.WarehouseLocation.Code)
            .ToListAsync();
    }
}