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

        // Only a detached entity needs attaching. Anything the context already tracks is
        // handled by change detection on save, and calling Update on it would be actively
        // harmful: on an Added entity it flips the state to Modified, emitting an UPDATE for
        // a row that does not exist yet. That happens for real when an inventory correction
        // creates a substitute allocation and then has to deallocate the whole order.
        if (_dbContext.Entry(entity).State == EntityState.Detached)
            _dbSet.Update(entity);

        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(T entity)
    {
        _dbSet.Remove(entity);

        return Task.CompletedTask;
    }
}