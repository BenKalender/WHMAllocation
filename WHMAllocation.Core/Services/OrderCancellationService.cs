using WHMAllocation.Core.Entities;
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

        if (order.Status == OrderStatus.Cancelled)
            return;

        foreach (var orderLine in order.OrderLines)
        {
            if (orderLine.IsCancelled)
                continue;

            await ReleaseAllocationsForLineAsync(orderLine.Id);

            orderLine.IsCancelled = true;
        }

        order.Status = OrderStatus.Cancelled;

        await _orderRepository.UpdateAsync(order);

        await _unitOfWork.SaveChangesAsync();
    }

    public async Task CancelOrderLineAsync(Guid orderLineId)
    {
        var order = await _orderRepository.GetByOrderLineIdAsync(orderLineId);

        var orderLine = order?.OrderLines
            .FirstOrDefault(x => x.Id == orderLineId);

        if (order is null || orderLine is null)
            return;

        if (order.Status == OrderStatus.Cancelled || orderLine.IsCancelled)
            return;

        await ReleaseAllocationsForLineAsync(orderLineId);

        orderLine.IsCancelled = true;

        RecalculateOrderStatus(order);

        await _orderRepository.UpdateAsync(order);

        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Returns the allocated quantity of every active allocation on the line back to its SKU
    /// and deactivates the allocation. Does not save — the public entry points own the
    /// transaction boundary so that cancelling a whole order is a single write.
    /// </summary>
    private async Task ReleaseAllocationsForLineAsync(Guid orderLineId)
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

    /// <summary>
    /// Re-derives the order status from the lines that survive, so an order whose only
    /// unallocated line was cancelled is no longer left looking partially allocated.
    /// </summary>
    private static void RecalculateOrderStatus(Order order)
    {
        var remainingLines = order.OrderLines
            .Where(x => !x.IsCancelled)
            .ToList();

        if (remainingLines.Count == 0)
        {
            order.Status = OrderStatus.Cancelled;
            return;
        }

        int totalRequested = remainingLines.Sum(x => x.RequestedQuantity);

        int totalAllocated = remainingLines
            .SelectMany(x => x.Allocations)
            .Where(x => x.IsActive)
            .Sum(x => x.Quantity);

        order.Status = totalAllocated switch
        {
            0 => OrderStatus.Released,
            var allocated when allocated >= totalRequested => OrderStatus.Allocated,
            _ => OrderStatus.PartiallyAllocated
        };
    }
}
