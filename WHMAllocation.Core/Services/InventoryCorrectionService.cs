using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;

namespace WHMAllocation.Core.Services;

public class InventoryCorrectionService : IInventoryCorrectionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISkuRepository _skuRepository;
    private readonly IAllocationRepository _allocationRepository;
    private readonly IOrderRepository _orderRepository;


    public InventoryCorrectionService(IUnitOfWork unitOfWork, IOrderRepository orderRepository, ISkuRepository skuRepository, IAllocationRepository allocationRepository)
    {
        _unitOfWork = unitOfWork;
        _orderRepository = orderRepository;
        _skuRepository = skuRepository;
        _allocationRepository = allocationRepository;
    }

    public async Task CorrectSkuQuantityAsync(Guid skuId, int newQuantity)
    {
        var sku = await _skuRepository.GetByIdAsync(skuId);

        if (sku is null)
            return;

        var allocations = await _allocationRepository.GetBySkuIdAsync(skuId);

        sku.Quantity = newQuantity;

        await _skuRepository.UpdateAsync(sku);

        foreach (var allocation in allocations)
        {
            if (!allocation.IsActive)
                continue;

            var totalAvailable = sku.Quantity;

            if (totalAvailable >= allocation.Quantity)
                continue;

            var missingQuantity = allocation.Quantity - totalAvailable;

            await TryFindSubstituteSkuAsync(sku, allocation, missingQuantity);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task TryFindSubstituteSkuAsync(Sku originalSku, Allocation allocation, int missingQuantity)
    {
        var substituteSkus = await _skuRepository.GetAvailableSkusByProductAsync(originalSku.ProductId);

        foreach (var substituteSku in substituteSkus)
        {
            if (substituteSku.Id == originalSku.Id)
                continue;

            if (missingQuantity <= 0)
                break;

            var quantityToAllocate = Math.Min(substituteSku.Quantity, missingQuantity);

            substituteSku.Quantity -= quantityToAllocate;

            var substitudeAllocation = new Allocation
            {
                OrderLineId = allocation.OrderLineId,
                SkuId = substituteSku.Id,
                Quantity = quantityToAllocate,
                IsActive = true
            };

            await _allocationRepository.AddAsync(substitudeAllocation);

            await _skuRepository.UpdateAsync(substituteSku);

            missingQuantity -= quantityToAllocate;
        }

        if (missingQuantity > 0)
        {
            var order = allocation.OrderLine.Order;

            if (order.CompleteDeliveryRequired)
                await DeallocateOrderAsync(order);            
        }
    }

    private async Task DeallocateOrderAsync(Order order)
    {
        foreach (var line in order.OrderLines)
        {
            foreach (var allocation in line.Allocations)
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

        order.Status = OrderStatus.Released;
        await _orderRepository.UpdateAsync(order);
    }
}