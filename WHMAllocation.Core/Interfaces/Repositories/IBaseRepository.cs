using WHMAllocation.Core.Entities;

namespace WHMAllocation.Core.Interfaces.Repositories;

public interface IBaseRepository<T>
    where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id);

    Task AddAsync(T entity);

    Task UpdateAsync(T entity);

    Task DeleteAsync(T entity);
}