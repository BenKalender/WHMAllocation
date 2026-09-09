using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;

namespace WHMAllocation.Core.Services;

public class InventoryCorrectionService : IInventoryCorrectionService
{
    private readonly ISkuRepository _skuRepository;
    private readonly IAllocationRepository _allocationRepository;

    public InventoryCorrectionService(
        ISkuRepository skuRepository,
        IAllocationRepository allocationRepository)
    {
        _skuRepository = skuRepository;
        _allocationRepository = allocationRepository;
    }

    public async Task CorrectSkuQuantityAsync(
        Guid skuId,
        int newQuantity)
    {
        var sku = await _skuRepository.GetByIdAsync(skuId);

        if (sku is null)
        {
            return;
        }

        var allocations =
            await _allocationRepository.GetBySkuIdAsync(skuId);

        sku.Quantity = newQuantity;

        await _skuRepository.UpdateAsync(sku);

        foreach (var allocation in allocations)
        {
            if (!allocation.IsActive)
            {
                continue;
            }

            var totalAvailable =
                sku.Quantity;

            if (totalAvailable >= allocation.Quantity)
            {
                continue;
            }

            var missingQuantity =
                allocation.Quantity - totalAvailable;

            await TryFindSubstituteSkuAsync(
                sku,
                allocation,
                missingQuantity);
        }
    }

    private async Task TryFindSubstituteSkuAsync(
        Sku originalSku,
        Allocation allocation,
        int missingQuantity)
    {
        var substituteSkus =
            await _skuRepository
                .GetAvailableSkusByProductAsync(
                    originalSku.ProductId);

        foreach (var substituteSku in substituteSkus)
        {
            if (substituteSku.Id == originalSku.Id)
            {
                continue;
            }

            if (missingQuantity <= 0)
            {
                break;
            }

            var quantityToAllocate =
                Math.Min(
                    substituteSku.Quantity,
                    missingQuantity);

            substituteSku.Quantity -= quantityToAllocate;

            await _skuRepository.UpdateAsync(
                substituteSku);

            missingQuantity -= quantityToAllocate;
        }

        if (missingQuantity > 0)
        {
            // TODO:
            // CompleteDeliveryRequired kontrolü
            // ve order deallocation işlemi
        }
    }
}