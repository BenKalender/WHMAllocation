using Microsoft.EntityFrameworkCore;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Infrastructure.Persistence;

namespace WHMAllocation.Infrastructure.Repositories;

public class BaseRepository<T> : IBaseRepository<T>
    where T : BaseEntity
{
    protected readonly AppDbContext _dbContext;

    protected readonly DbSet<T> _dbSet;

    public BaseRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id)
    {
        return await _dbSet.FindAsync(id);
    }

    public virtual async Task AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
    }

    public virtual Task UpdateAsync(T entity)
    {
        entity.LastChangedAt = DateTime.UtcNow;

        _dbSet.Update(entity);

        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(T entity)
    {
        _dbSet.Remove(entity);

        return Task.CompletedTask;
    }
}