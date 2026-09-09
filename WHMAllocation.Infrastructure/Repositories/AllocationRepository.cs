using Microsoft.EntityFrameworkCore;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Infrastructure.Persistence;

namespace WHMAllocation.Infrastructure.Repositories;

public class AllocationRepository : BaseRepository<Allocation>, IAllocationRepository
{
    public AllocationRepository(AppDbContext dbContext)
        : base(dbContext)
    {
    }

    public async Task<List<Allocation>> GetByOrderLineIdAsync(Guid orderLineId)
    {
        return await _dbContext.Allocations
            .Where(x => x.OrderLineId == orderLineId)
            .ToListAsync();
    }

    public async Task<List<Allocation>> GetBySkuIdAsync(Guid skuId)
    {
        return await _dbContext.Allocations
            .Where(x => x.SkuId == skuId)
            .ToListAsync();
    }

    public async Task<List<Allocation>> GetActiveAllocationsBySkuIdAsync(Guid skuId)
    {
        return await _dbContext.Allocations
            .Include(x => x.OrderLine)
                .ThenInclude(x => x.Order)
            .Where(x =>
                x.SkuId == skuId &&
                x.IsActive)
            .ToListAsync();
    }
}