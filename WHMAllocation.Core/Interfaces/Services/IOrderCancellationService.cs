namespace WHMAllocation.Core.Interfaces.Services;

public interface IOrderCancellationService
{
    Task CancelOrderAsync(Guid orderId);

    Task CancelOrderLineAsync(Guid orderLineId);
}