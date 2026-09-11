using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;
using WHMAllocation.Core.Results;

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

    public async Task<CancellationResult> CancelOrderAsync(Guid orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);

        if (order is null)
            return CancellationResult.NoChange(CancellationOutcome.OrderNotFound);

        if (order.Status == OrderStatus.Cancelled)
            return CancellationResult.NoChange(CancellationOutcome.OrderAlreadyCancelled, order.Status);

        int releasedQuantity = 0;
        int affectedLines = 0;

        foreach (var orderLine in order.OrderLines)
        {
            if (orderLine.IsCancelled)
                continue;

            releasedQuantity += await ReleaseAllocationsForLineAsync(orderLine.Id);

            orderLine.IsCancelled = true;

            affectedLines++;
        }

        order.Status = OrderStatus.Cancelled;

        await _orderRepository.UpdateAsync(order);

        await _unitOfWork.SaveChangesAsync();

        return new CancellationResult(
            CancellationOutcome.Cancelled,
            releasedQuantity,
            affectedLines,
            order.Status);
    }

    public async Task<CancellationResult> CancelOrderLineAsync(Guid orderLineId)
    {
        var order = await _orderRepository.GetByOrderLineIdAsync(orderLineId);

        var orderLine = order?.OrderLines
            .FirstOrDefault(x => x.Id == orderLineId);

        if (order is null || orderLine is null)
            return CancellationResult.NoChange(CancellationOutcome.OrderLineNotFound);

        if (order.Status == OrderStatus.Cancelled)
            return CancellationResult.NoChange(CancellationOutcome.OrderAlreadyCancelled, order.Status);

        if (orderLine.IsCancelled)
            return CancellationResult.NoChange(CancellationOutcome.OrderLineAlreadyCancelled, order.Status);

        int releasedQuantity = await ReleaseAllocationsForLineAsync(orderLineId);

        orderLine.IsCancelled = true;

        RecalculateOrderStatus(order);

        await _orderRepository.UpdateAsync(order);

        await _unitOfWork.SaveChangesAsync();

        return new CancellationResult(
            CancellationOutcome.Cancelled,
            releasedQuantity,
            AffectedLines: 1,
            order.Status);
    }

    /// <summary>
    /// Returns the allocated quantity of every active allocation on the line back to its SKU
    /// and deactivates the allocation. Does not save — the public entry points own the
    /// transaction boundary so that cancelling a whole order is a single write.
    /// </summary>
    /// <returns>The number of units actually returned to stock.</returns>
    private async Task<int> ReleaseAllocationsForLineAsync(Guid orderLineId)
    {
        var allocations = await _allocationRepository.GetByOrderLineIdAsync(orderLineId);

        int releasedQuantity = 0;

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

            releasedQuantity += allocation.Quantity;
        }

        return releasedQuantity;
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
