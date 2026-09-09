namespace WHMAllocation.Core.Interfaces;

public interface IUnitOfWork
{
    Task SaveChangesAsync();
}