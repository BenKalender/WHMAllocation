using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;
using WHMAllocation.Core.Results;

namespace WHMAllocation.Core.Services;

/// <summary>
/// Applies a manual stock count to a SKU and repairs any allocation it invalidates.
/// </summary>
/// <remarks>
/// <see cref="Sku.Quantity"/> holds the <em>available</em> (unreserved) quantity: allocating
/// decrements it, cancelling increments it. The corrected value supplied by the operator is a
/// <em>physical</em> count, so the new available quantity is derived as
/// <c>newQuantity - SUM(active allocations on that SKU)</c>. A negative result is the shortfall
/// that has to be covered by substitute SKUs, or else surrendered.
/// </remarks>
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

    public async Task<CorrectionResult> CorrectSkuQuantityAsync(Guid skuId, int newQuantity)
    {
        if (newQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(newQuantity), "A corrected SKU quantity cannot be negative.");

        var sku = await _skuRepository.GetByIdAsync(skuId);

        if (sku is null)
            return CorrectionResult.SkuNotFound;

        var activeAllocations = await _allocationRepository.GetActiveAllocationsBySkuIdAsync(skuId);

        int totalAllocated = activeAllocations.Sum(x => x.Quantity);

        // The shortfall is measured once against the whole reserved total. Comparing the
        // corrected quantity against each allocation separately would let the same units
        // count as covering every allocation at the same time.
        int newAvailable = newQuantity - totalAllocated;

        sku.Quantity = Math.Max(newAvailable, 0);

        await _skuRepository.UpdateAsync(sku);

        if (newAvailable >= 0)
        {
            // Enough stock remains to honour every reservation. An increase also lands here
            // and simply frees more availability for the next allocation run.
            await _unitOfWork.SaveChangesAsync();

            return CorrectionResult.NoShortfall(sku.Quantity);
        }

        int shortfall = -newAvailable;

        var impact = await CoverShortfallAsync(sku, activeAllocations, shortfall);

        await _unitOfWork.SaveChangesAsync();

        // Reported by escalating severity: a released order is the most disruptive thing that
        // can happen here, so it wins over a mere downgrade.
        var outcome = impact.DeallocatedOrderIds.Count > 0
            ? CorrectionOutcome.OrdersDeallocated
            : impact.DowngradedOrderIds.Count > 0
                ? CorrectionOutcome.ShortfallPartiallyCovered
                : CorrectionOutcome.ShortfallCovered;

        return new CorrectionResult(
            outcome,
            sku.Quantity,
            shortfall,
            impact.CoveredBySubstitutes,
            impact.DeallocatedOrderIds,
            impact.DowngradedOrderIds);
    }

    /// <summary>What a shortfall cost: how much was re-sourced, and which orders suffered.</summary>
    private sealed class ShortfallImpact
    {
        public int CoveredBySubstitutes { get; set; }

        public List<Guid> DeallocatedOrderIds { get; } = [];

        public List<Guid> DowngradedOrderIds { get; } = [];
    }

    private async Task<ShortfallImpact> CoverShortfallAsync(Sku sku, List<Allocation> activeAllocations, int shortfall)
    {
        var impact = new ShortfallImpact();

        // Surrender stock from the least important reservations first: lowest priority, and
        // within a priority the most recent order, so established high-priority orders keep
        // what they already hold.
        var surrenderOrder = activeAllocations
            .OrderBy(x => x.OrderLine.Order.Priority)
            .ThenByDescending(x => x.OrderLine.Order.CreatedAt)
            .ToList();

        // Substitutes created here are not yet part of any loaded order graph, so they are
        // tracked separately in case the order later has to be fully deallocated.
        var createdSubstitutes = new List<Allocation>();

        foreach (var allocation in surrenderOrder)
        {
            if (shortfall <= 0)
                break;

            int reduction = Math.Min(shortfall, allocation.Quantity);

            // Reducing the original is what keeps the line's total honest once a substitute
            // is added; without it the line would hold the original quantity plus the
            // substitute quantity.
            allocation.Quantity -= reduction;

            if (allocation.Quantity == 0)
                allocation.IsActive = false;

            await _allocationRepository.UpdateAsync(allocation);

            shortfall -= reduction;

            int stillMissing = await TryCoverWithSubstitutesAsync(sku, allocation, reduction, createdSubstitutes);

            impact.CoveredBySubstitutes += reduction - stillMissing;

            if (stillMissing > 0)
                await HandleUncoveredShortfallAsync(allocation, createdSubstitutes, impact);
        }

        return impact;
    }

    /// <summary>
    /// Attempts to cover <paramref name="missingQuantity"/> from other SKUs of the same product.
    /// Returns whatever could not be covered.
    /// </summary>
    private async Task<int> TryCoverWithSubstitutesAsync(
        Sku originalSku,
        Allocation allocation,
        int missingQuantity,
        List<Allocation> createdSubstitutes)
    {
        var substituteSkus = await _skuRepository.GetAvailableSkusByProductAsync(originalSku.ProductId);

        foreach (var substituteSku in substituteSkus)
        {
            if (missingQuantity <= 0)
                break;

            if (substituteSku.Id == originalSku.Id)
                continue;

            int quantityToAllocate = Math.Min(substituteSku.Quantity, missingQuantity);

            if (quantityToAllocate <= 0)
                continue;

            substituteSku.Quantity -= quantityToAllocate;

            var substituteAllocation = new Allocation
            {
                OrderLineId = allocation.OrderLineId,
                SkuId = substituteSku.Id,
                Quantity = quantityToAllocate,
                IsActive = true
            };

            await _allocationRepository.AddAsync(substituteAllocation);

            await _skuRepository.UpdateAsync(substituteSku);

            createdSubstitutes.Add(substituteAllocation);

            missingQuantity -= quantityToAllocate;
        }

        return missingQuantity;
    }

    private async Task HandleUncoveredShortfallAsync(
        Allocation allocation,
        List<Allocation> createdSubstitutes,
        ShortfallImpact impact)
    {
        var order = allocation.OrderLine.Order;

        if (!order.CompleteDeliveryRequired)
        {
            // Partial delivery is acceptable, so the order simply holds less than it asked for.
            if (order.Status == OrderStatus.Allocated)
            {
                order.Status = OrderStatus.PartiallyAllocated;

                await _orderRepository.UpdateAsync(order);

                impact.DowngradedOrderIds.Add(order.Id);
            }

            return;
        }

        if (impact.DeallocatedOrderIds.Contains(order.Id))
            return;

        impact.DeallocatedOrderIds.Add(order.Id);

        // The allocation graph reached from this SKU only carries the lines that happen to be
        // tied to it, so the order is reloaded in full: allocations this order holds on other
        // SKUs must be released too, otherwise they stay reserved for an order that can no
        // longer ship.
        var fullOrder = await _orderRepository.GetByIdAsync(order.Id);

        if (fullOrder is null)
            return;

        await DeallocateOrderAsync(fullOrder, createdSubstitutes);
    }

    private async Task DeallocateOrderAsync(Order order, List<Allocation> createdSubstitutes)
    {
        var orderLineIds = order.OrderLines
            .Select(x => x.Id)
            .ToHashSet();

        var allocations = order.OrderLines
            .SelectMany(x => x.Allocations)
            .Concat(createdSubstitutes.Where(x => orderLineIds.Contains(x.OrderLineId)))
            .Distinct()
            .ToList();

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

        // Back to Released so the next allocation run can try again from scratch.
        order.Status = OrderStatus.Released;

        await _orderRepository.UpdateAsync(order);
    }
}
