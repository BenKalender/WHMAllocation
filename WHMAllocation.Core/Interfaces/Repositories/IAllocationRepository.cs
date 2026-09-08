using WHMAllocation.Core.Entities;

namespace WHMAllocation.Core.Interfaces.Repositories;

public interface IAllocationRepository
    : IBaseRepository<Allocation>
{
    Task<List<Allocation>> GetByOrderLineIdAsync(
        Guid orderLineId);

    Task<List<Allocation>> GetBySkuIdAsync(
        Guid skuId);
}