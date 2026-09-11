using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;
using WHMAllocation.Core.Results;

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

    public async Task<AllocationRunResult> AllocateReleasedOrdersAsync()
    {
        var orders = await _orderRepository.GetReleasedOrdersAsync();

        var results = new List<OrderAllocationResult>(orders.Count);

        foreach (var order in orders)
        {
            results.Add(await AllocateOrderAsync(order));
        }

        return new AllocationRunResult(results);
    }

    private async Task<OrderAllocationResult> AllocateOrderAsync(Order order)
    {
        // A cancelled line keeps its row but must never receive stock again.
        List<OrderLine> allocatableLines = order.OrderLines
            .Where(x => !x.IsCancelled)
            .ToList();

        int totalRequested = allocatableLines.Sum(x => x.RequestedQuantity);

        if (allocatableLines.Count == 0)
            return Skipped(order, OrderAllocationOutcome.SkippedNoLines, totalRequested);

        if (order.CompleteDeliveryRequired)
        {
            var canAllocate = await CanFullyAllocateAsync(allocatableLines);

            if (!canAllocate)
                return Skipped(order, OrderAllocationOutcome.SkippedCompleteDeliveryNotPossible, totalRequested);
        }

        int totalAllocated = 0;

        foreach(var orderLine in allocatableLines)
        {
            totalAllocated += await AllocateLineAsync(orderLine);
        }

        // Nothing reserved means the order is untouched, so it stays Released and
        // will be retried on the next run once stock arrives.
        if (totalAllocated == 0)
            return Skipped(order, OrderAllocationOutcome.SkippedNoStock, totalRequested);

        order.Status = totalAllocated >= totalRequested ? OrderStatus.Allocated : OrderStatus.PartiallyAllocated;

        await _orderRepository.UpdateAsync(order);

        await _unitOfWork.SaveChangesAsync();

        var outcome = order.Status == OrderStatus.Allocated
            ? OrderAllocationOutcome.FullyAllocated
            : OrderAllocationOutcome.PartiallyAllocated;

        return new OrderAllocationResult(order.Id, order.OrderNumber, outcome, totalRequested, totalAllocated);
    }

    private static OrderAllocationResult Skipped(Order order, OrderAllocationOutcome outcome, int totalRequested) =>
        new(order.Id, order.OrderNumber, outcome, totalRequested, AllocatedQuantity: 0);

    /// <summary>
    /// Lines are grouped by product first: two lines asking for the same product compete
    /// for the same stock, so checking each line against the full availability on its own
    /// would let an order through that cannot actually be delivered complete.
    /// </summary>
    private async Task<bool> CanFullyAllocateAsync(IReadOnlyCollection<OrderLine> orderLines)
    {
        var requestedByProduct = orderLines
            .GroupBy(x => x.ProductId)
            .Select(x => new
            {
                ProductId = x.Key,
                RequestedQuantity = x.Sum(line => line.RequestedQuantity)
            });

        foreach(var requested in requestedByProduct)
        {
            List<Sku> availableSkus = await _skuRepository.GetAvailableSkusByProductAsync(requested.ProductId);

            int totalAvailableQuantity = availableSkus.Sum(x => x.Quantity);

            if (totalAvailableQuantity < requested.RequestedQuantity)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns the quantity actually allocated. The caller sums these rather than reading
    /// back <see cref="OrderLine.Allocations"/>, which may still hold deactivated
    /// allocations from an earlier run.
    /// </summary>
    private async Task<int> AllocateLineAsync(OrderLine orderLine)
    {
        List<Sku> availableSkus = await _skuRepository.GetAvailableSkusByProductAsync(orderLine.ProductId);

        int remainingQuantity = orderLine.RequestedQuantity;

        int allocatedQuantity = 0;

        foreach(var sku in availableSkus)
        {
            if(remainingQuantity <= 0)
                break;

            int quantityToAllocate = Math.Min(remainingQuantity, sku.Quantity);

            if (quantityToAllocate <= 0)
                continue;

            await CreateAllocationAsync(orderLine, sku, quantityToAllocate);

            remainingQuantity -= quantityToAllocate;

            allocatedQuantity += quantityToAllocate;
        }

        return allocatedQuantity;
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
