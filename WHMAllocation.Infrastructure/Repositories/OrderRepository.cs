using Microsoft.EntityFrameworkCore;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Infrastructure.Persistence;

namespace WHMAllocation.Infrastructure.Repositories;

public class OrderRepository : BaseRepository<Order>, IOrderRepository
{
    public OrderRepository(AppDbContext dbContext)
        : base(dbContext)
    {
    }

    public override async Task<Order?> GetByIdAsync(Guid id)
    {
        return await _dbContext.Orders
            .Include(x => x.OrderLines)
                .ThenInclude(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<List<Order>> GetReleasedOrdersAsync()
    {
        return await _dbContext.Orders
            .Include(x => x.OrderLines)
                .ThenInclude(x => x.Allocations)
            .Where(x => x.Status == OrderStatus.Released)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync();
    }

    
}