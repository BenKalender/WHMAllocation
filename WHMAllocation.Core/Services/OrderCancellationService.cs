using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;

namespace WHMAllocation.Core.Services;

public class OrderCancellationService : IOrderCancellationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orderRepository;
    private readonly IAllocationRepository _allocationRepository;
    private readonly ISkuRepository _skuRepository;

    public OrderCancellationService(
        IUnitOfWork unitOfWork,
        IOrderRepository orderRepository,
        IAllocationRepository allocationRepository,
        ISkuRepository skuRepository)
    {
        _unitOfWork = unitOfWork;
        _orderRepository = orderRepository;
        _allocationRepository = allocationRepository;
        _skuRepository = skuRepository;
    }

    public async Task CancelOrderAsync(Guid orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);

        if (order is null)
            return;

        foreach (var orderLine in order.OrderLines)
        {
            await CancelOrderLineAsync(orderLine.Id);
        }

        order.Status = OrderStatus.Cancelled;

        await _orderRepository.UpdateAsync(order);
        
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task CancelOrderLineAsync(Guid orderLineId)
    {
        var allocations = await _allocationRepository.GetByOrderLineIdAsync(orderLineId);

        foreach (var allocation in allocations)
        {
            if (!allocation.IsActive)
                continue;            

            var sku = await _skuRepository.GetByIdAsync(allocation.SkuId);

            if (sku is null)
                continue;        

            sku.Quantity += allocation.Quantity;

            allocation.IsActive = false;

            await _skuRepository.UpdateAsync(sku);

            await _allocationRepository.UpdateAsync(allocation);
        }
    }
}