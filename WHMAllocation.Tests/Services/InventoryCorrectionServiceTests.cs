using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Services;

namespace WHMAllocation.Tests.Services;

[TestClass]
public class InventoryCorrectionServiceTests
{
    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IOrderRepository> _orderRepository = null!;
    private Mock<ISkuRepository> _skuRepository = null!;
    private Mock<IAllocationRepository> _allocationRepository = null!;

    private readonly List<Allocation> _addedAllocations = [];

    [TestInitialize]
    public void Setup()
    {
        _unitOfWork = new Mock<IUnitOfWork>();
        _orderRepository = new Mock<IOrderRepository>();
        _skuRepository = new Mock<ISkuRepository>();
        _allocationRepository = new Mock<IAllocationRepository>();

        _addedAllocations.Clear();

        _allocationRepository
            .Setup(x => x.AddAsync(It.IsAny<Allocation>()))
            .Callback<Allocation>(_addedAllocations.Add)
            .Returns(Task.CompletedTask);
    }

    private InventoryCorrectionService CreateService() =>
        new(_unitOfWork.Object, _orderRepository.Object, _skuRepository.Object, _allocationRepository.Object);

    private void SetupSkus(params Sku[] skus)
    {
        foreach (var sku in skus)
        {
            var captured = sku;
            _skuRepository.Setup(x => x.GetByIdAsync(captured.Id)).ReturnsAsync(captured);
        }
    }

    private void SetupAvailableSkus(Guid productId, params Sku[] skus)
    {
        _skuRepository
            .Setup(x => x.GetAvailableSkusByProductAsync(productId))
            .ReturnsAsync(() => skus.Where(x => x.Quantity > 0).ToList());
    }

    /// <summary>
    /// Builds an order line wired to its order in both directions, matching the graph the
    /// repository eager-loads.
    /// </summary>
    private static OrderLine BuildLine(Order order, Guid productId, int requestedQuantity)
    {
        var line = new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = productId,
            RequestedQuantity = requestedQuantity,
            Order = order
        };

        order.OrderLines.Add(line);

        return line;
    }

    private static Allocation BuildAllocation(OrderLine line, Sku sku, int quantity)
    {
        var allocation = new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = line.Id,
            SkuId = sku.Id,
            Quantity = quantity,
            IsActive = true,
            OrderLine = line
        };

        line.Allocations.Add(allocation);

        return allocation;
    }

    [TestMethod]
    public async Task Correction_Should_Reject_Negative_Quantity()
    {
        var sku = new Sku { Id = Guid.NewGuid(), Quantity = 10 };

        SetupSkus(sku);

        var act = async () => await CreateService().CorrectSkuQuantityAsync(sku.Id, -1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// An increase simply frees more availability. With 8 of 10 reserved, a correction to 20
    /// leaves 12 available.
    /// </summary>
    [TestMethod]
    public async Task Correction_Should_Increase_Available_Quantity()
    {
        var productId = Guid.NewGuid();

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 2 };

        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Allocated };
        var line = BuildLine(order, productId, 8);
        var allocation = BuildAllocation(line, sku, 8);

        SetupSkus(sku);

        _allocationRepository
            .Setup(x => x.GetActiveAllocationsBySkuIdAsync(sku.Id))
            .ReturnsAsync([allocation]);

        await CreateService().CorrectSkuQuantityAsync(sku.Id, 20);

        sku.Quantity.Should().Be(12);
        allocation.Quantity.Should().Be(8, "an increase disturbs no reservation");
        allocation.IsActive.Should().BeTrue();
        _unitOfWork.Verify(x => x.SaveChangesAsync(), Times.Once);
    }

    /// <summary>
    /// A decrease that still covers every reservation only reduces availability.
    /// </summary>
    [TestMethod]
    public async Task Correction_Should_Not_Touch_Allocations_When_Stock_Still_Covers_Them()
    {
        var productId = Guid.NewGuid();

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 5 };

        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Allocated };
        var line = BuildLine(order, productId, 10);
        var allocation = BuildAllocation(line, sku, 10);

        SetupSkus(sku);

        _allocationRepository
            .Setup(x => x.GetActiveAllocationsBySkuIdAsync(sku.Id))
            .ReturnsAsync([allocation]);

        await CreateService().CorrectSkuQuantityAsync(sku.Id, 12);

        sku.Quantity.Should().Be(2);
        allocation.Quantity.Should().Be(10);
        _addedAllocations.Should().BeEmpty();
    }

    /// <summary>
    /// The substitute must be paired with a matching reduction of the original, otherwise the
    /// line ends up holding both quantities.
    /// </summary>
    [TestMethod]
    public async Task Correction_Should_Use_Substitute_Sku_And_Reduce_The_Original()
    {
        var productId = Guid.NewGuid();

        var correctedSku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 0 };
        var substituteSku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 10 };

        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Allocated };
        var line = BuildLine(order, productId, 20);
        var allocation = BuildAllocation(line, correctedSku, 20);

        SetupSkus(correctedSku, substituteSku);
        SetupAvailableSkus(productId, correctedSku, substituteSku);

        _allocationRepository
            .Setup(x => x.GetActiveAllocationsBySkuIdAsync(correctedSku.Id))
            .ReturnsAsync([allocation]);

        // Physical count drops from 20 to 15: a shortfall of 5.
        await CreateService().CorrectSkuQuantityAsync(correctedSku.Id, 15);

        allocation.Quantity.Should().Be(15, "the original must shrink by the shortfall");
        allocation.IsActive.Should().BeTrue();

        _addedAllocations.Should().HaveCount(1);
        _addedAllocations[0].SkuId.Should().Be(substituteSku.Id);
        _addedAllocations[0].Quantity.Should().Be(5);
        _addedAllocations[0].OrderLineId.Should().Be(line.Id);

        substituteSku.Quantity.Should().Be(5, "the substitute gave up 5 units");

        // The line still holds exactly what it asked for: 15 original + 5 substitute.
        (allocation.Quantity + _addedAllocations[0].Quantity).Should().Be(20);

        order.Status.Should().Be(OrderStatus.Allocated, "the shortfall was fully covered");
    }

    /// <summary>
    /// Two reservations on one SKU: the shortfall is measured once against their combined
    /// total, not separately against each one.
    /// </summary>
    [TestMethod]
    public async Task Correction_Should_Count_Shortfall_Once_Across_Multiple_Allocations()
    {
        var productId = Guid.NewGuid();

        var correctedSku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 0 };

        var lowPriorityOrder = new Order
        {
            Id = Guid.NewGuid(),
            Priority = Priority.Low,
            Status = OrderStatus.Allocated
        };
        var lowPriorityLine = BuildLine(lowPriorityOrder, productId, 10);
        var lowPriorityAllocation = BuildAllocation(lowPriorityLine, correctedSku, 10);

        var highPriorityOrder = new Order
        {
            Id = Guid.NewGuid(),
            Priority = Priority.High,
            Status = OrderStatus.Allocated
        };
        var highPriorityLine = BuildLine(highPriorityOrder, productId, 10);
        var highPriorityAllocation = BuildAllocation(highPriorityLine, correctedSku, 10);

        SetupSkus(correctedSku);
        SetupAvailableSkus(productId, correctedSku);

        _allocationRepository
            .Setup(x => x.GetActiveAllocationsBySkuIdAsync(correctedSku.Id))
            .ReturnsAsync([lowPriorityAllocation, highPriorityAllocation]);

        _orderRepository.Setup(x => x.GetByIdAsync(lowPriorityOrder.Id)).ReturnsAsync(lowPriorityOrder);

        // 20 reserved, corrected to 16, so exactly 4 units must be surrendered in total.
        await CreateService().CorrectSkuQuantityAsync(correctedSku.Id, 16);

        int remaining = lowPriorityAllocation.Quantity + highPriorityAllocation.Quantity;

        remaining.Should().Be(16, "only the 4 missing units may be taken away");

        lowPriorityAllocation.Quantity.Should().Be(6, "the low priority order surrenders stock first");
        highPriorityAllocation.Quantity.Should().Be(10, "the high priority order keeps what it holds");

        correctedSku.Quantity.Should().Be(0);
    }

    /// <summary>
    /// Scenario 3.3: with complete delivery required and no substitute available, every
    /// allocation on the order is released - including those held on other SKUs.
    /// </summary>
    [TestMethod]
    public async Task Correction_Should_Deallocate_Whole_Order_Including_Other_Skus_When_Complete_Delivery_Required()
    {
        var productId = Guid.NewGuid();
        var otherProductId = Guid.NewGuid();

        var correctedSku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 0 };
        var unrelatedSku = new Sku { Id = Guid.NewGuid(), ProductId = otherProductId, Quantity = 0 };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Allocated,
            CompleteDeliveryRequired = true
        };

        var correctedLine = BuildLine(order, productId, 10);
        var correctedAllocation = BuildAllocation(correctedLine, correctedSku, 10);

        // A second line of the same order, allocated from a completely different SKU.
        var otherLine = BuildLine(order, otherProductId, 4);
        var otherAllocation = BuildAllocation(otherLine, unrelatedSku, 4);

        SetupSkus(correctedSku, unrelatedSku);

        // No substitute exists for the corrected product.
        SetupAvailableSkus(productId, correctedSku);

        _allocationRepository
            .Setup(x => x.GetActiveAllocationsBySkuIdAsync(correctedSku.Id))
            .ReturnsAsync([correctedAllocation]);

        _orderRepository.Setup(x => x.GetByIdAsync(order.Id)).ReturnsAsync(order);

        // Physical count collapses to 2 against 10 reserved.
        await CreateService().CorrectSkuQuantityAsync(correctedSku.Id, 2);

        order.Status.Should().Be(OrderStatus.Released);

        correctedAllocation.IsActive.Should().BeFalse();
        otherAllocation.IsActive.Should().BeFalse("allocations on other SKUs of the order must be released too");

        unrelatedSku.Quantity.Should().Be(4, "the unrelated SKU gets its stock back");
    }

    /// <summary>
    /// Without the complete delivery flag a partial hold is acceptable.
    /// </summary>
    [TestMethod]
    public async Task Correction_Should_Downgrade_To_PartiallyAllocated_When_Partial_Delivery_Allowed()
    {
        var productId = Guid.NewGuid();

        var correctedSku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 0 };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Allocated,
            CompleteDeliveryRequired = false
        };

        var line = BuildLine(order, productId, 10);
        var allocation = BuildAllocation(line, correctedSku, 10);

        SetupSkus(correctedSku);
        SetupAvailableSkus(productId, correctedSku);

        _allocationRepository
            .Setup(x => x.GetActiveAllocationsBySkuIdAsync(correctedSku.Id))
            .ReturnsAsync([allocation]);

        await CreateService().CorrectSkuQuantityAsync(correctedSku.Id, 6);

        order.Status.Should().Be(OrderStatus.PartiallyAllocated);

        allocation.Quantity.Should().Be(6);
        allocation.IsActive.Should().BeTrue();

        _addedAllocations.Should().BeEmpty("there was no substitute stock to draw on");
    }

    /// <summary>
    /// A correction to zero must retire the allocation rather than leave a zero-quantity
    /// reservation behind.
    /// </summary>
    [TestMethod]
    public async Task Correction_To_Zero_Should_Deactivate_The_Allocation()
    {
        var productId = Guid.NewGuid();

        var correctedSku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 0 };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Allocated,
            CompleteDeliveryRequired = false
        };

        var line = BuildLine(order, productId, 10);
        var allocation = BuildAllocation(line, correctedSku, 10);

        SetupSkus(correctedSku);
        SetupAvailableSkus(productId, correctedSku);

        _allocationRepository
            .Setup(x => x.GetActiveAllocationsBySkuIdAsync(correctedSku.Id))
            .ReturnsAsync([allocation]);

        await CreateService().CorrectSkuQuantityAsync(correctedSku.Id, 0);

        allocation.Quantity.Should().Be(0);
        allocation.IsActive.Should().BeFalse();
        correctedSku.Quantity.Should().Be(0);
        order.Status.Should().Be(OrderStatus.PartiallyAllocated);
    }

    [TestMethod]
    public async Task Correction_Should_Do_Nothing_When_Sku_Not_Found()
    {
        _skuRepository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Sku?)null);

        await CreateService().CorrectSkuQuantityAsync(Guid.NewGuid(), 10);

        _unitOfWork.Verify(x => x.SaveChangesAsync(), Times.Never);
    }
}
