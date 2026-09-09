using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;

namespace WHMAllocation.Core.Services;

public class AllocationService : IAllocationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orderRepository;
    private readonly ISkuRepository _skuRepository;
    private readonly IAllocationRepository _allocationRepository;

    public AllocationService(
        IUnitOfWork unitOfWork,
        IOrderRepository orderRepository,
        ISkuRepository skuRepository,
        IAllocationRepository allocationRepository)
    {
        _unitOfWork = unitOfWork;
        _orderRepository = orderRepository;
        _skuRepository = skuRepository;
        _allocationRepository = allocationRepository;
    }

    public async Task AllocateReleasedOrdersAsync()
    {
        var orders = await _orderRepository.GetReleasedOrdersAsync();

        foreach (var order in orders)
        {
            await AllocateOrderAsync(order);
        }
    }

    private async Task AllocateOrderAsync(Order order)
    {
        if (order.CompleteDeliveryRequired)
        {
            var canAllocate = await CanFullyAllocateAsync(order);

            if (!canAllocate)
                return;                
        }

        int totalRequested = order.OrderLines.Sum(x => x.RequestedQuantity);

        foreach(var orderLine in order.OrderLines)
        {
            await AllocateLineAsync(orderLine);
        }

        int totalAllocated = order.OrderLines
            .SelectMany(x => x.Allocations)
            .Sum(x => x.Quantity);

        order.Status = totalAllocated >= totalRequested ? OrderStatus.Allocated : OrderStatus.PartiallyAllocated;

        await _orderRepository.UpdateAsync(order);

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task<bool> CanFullyAllocateAsync(Order order)
    {
        foreach(var orderLine in order.OrderLines)
        {
            List<Sku> availableSkus = await _skuRepository.GetAvailableSkusByProductAsync(orderLine.ProductId);

            int totalAvailableQuantity = availableSkus.Sum(x => x.Quantity);

            if (totalAvailableQuantity < orderLine.RequestedQuantity)
                return false;
        }
        return true;
    }

    private async Task AllocateLineAsync(OrderLine orderLine)
    {
        List<Sku> availableSkus = await _skuRepository.GetAvailableSkusByProductAsync(orderLine.ProductId);

        int remainingQuantity = orderLine.RequestedQuantity;

        foreach(var sku in availableSkus)
        {
            if(remainingQuantity <= 0)
                break;

            int quantityToAllocate = Math.Min(remainingQuantity, sku.Quantity);
            
            await CreateAllocationAsync(orderLine, sku, quantityToAllocate);

            remainingQuantity -= quantityToAllocate;
        }
    }

    private async Task CreateAllocationAsync(OrderLine orderLine, Sku sku, int quantity)
    {
        var allocation = new Allocation
        {
            OrderLineId = orderLine.Id,
            SkuId = sku.Id,
            Quantity = quantity,
            IsActive = true
        };
        
        sku.Quantity -= quantity;
        await _allocationRepository.AddAsync(allocation);
        await _skuRepository.UpdateAsync(sku);
        orderLine.Allocations.Add(allocation);
    }
}
