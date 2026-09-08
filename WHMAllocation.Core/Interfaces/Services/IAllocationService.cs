namespace WHMAllocation.Core.Interfaces.Services;

public interface IAllocationService
{
    Task AllocateReleasedOrdersAsync();
}