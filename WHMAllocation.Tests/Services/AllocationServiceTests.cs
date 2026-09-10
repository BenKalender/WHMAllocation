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
public class AllocationServiceTests
{
    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IOrderRepository> _orderRepository = null!;
    private Mock<ISkuRepository> _skuRepository = null!;
    private Mock<IAllocationRepository> _allocationRepository = null!;

    [TestInitialize]
    public void Setup()
    {
        _unitOfWork = new Mock<IUnitOfWork>();
        _orderRepository = new Mock<IOrderRepository>();
        _skuRepository = new Mock<ISkuRepository>();
        _allocationRepository = new Mock<IAllocationRepository>();
    }

    private AllocationService CreateService() =>
        new(_unitOfWork.Object, _orderRepository.Object, _skuRepository.Object, _allocationRepository.Object);

    /// <summary>
    /// Mirrors the real repository: allocating decrements the SKU, so every re-query has to
    /// return the same instances and hide the ones that have run dry.
    /// </summary>
    private void SetupAvailableSkus(Guid productId, params Sku[] skus)
    {
        _skuRepository
            .Setup(x => x.GetAvailableSkusByProductAsync(productId))
            .ReturnsAsync(() => skus.Where(x => x.Quantity > 0).ToList());
    }

    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Process_HighPriority_First()
    {
        var productId = Guid.NewGuid();

        var highPriorityOrder = new Order
        {
            Id = Guid.NewGuid(),
            Priority = Priority.High,
            Status = OrderStatus.Released,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 10 }]
        };

        var lowPriorityOrder = new Order
        {
            Id = Guid.NewGuid(),
            Priority = Priority.Low,
            Status = OrderStatus.Released,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 10 }]
        };

        // Only enough for one of the two orders.
        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 10 };

        // The repository is what applies the priority ordering.
        _orderRepository
            .Setup(x => x.GetReleasedOrdersAsync())
            .ReturnsAsync([highPriorityOrder, lowPriorityOrder]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        highPriorityOrder.Status.Should().Be(OrderStatus.Allocated);
        lowPriorityOrder.Status.Should().Be(OrderStatus.Released, "the high priority order consumed the stock first");
    }

    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Respect_Fifo_Within_Same_Priority()
    {
        var productId = Guid.NewGuid();

        var olderOrder = new Order
        {
            Id = Guid.NewGuid(),
            Priority = Priority.Normal,
            Status = OrderStatus.Released,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 10 }]
        };

        var newerOrder = new Order
        {
            Id = Guid.NewGuid(),
            Priority = Priority.Normal,
            Status = OrderStatus.Released,
            CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 10 }]
        };

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 10 };

        _orderRepository
            .Setup(x => x.GetReleasedOrdersAsync())
            .ReturnsAsync([olderOrder, newerOrder]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        olderOrder.Status.Should().Be(OrderStatus.Allocated);
        newerOrder.Status.Should().Be(OrderStatus.Released);
    }

    [TestMethod]
    public async Task CompleteDeliveryRequired_Should_NotAllocate_When_Stock_Is_Insufficient()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = true,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 100 }]
        };

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 50 };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        order.Status.Should().Be(OrderStatus.Released);

        _allocationRepository.Verify(x => x.AddAsync(It.IsAny<Allocation>()), Times.Never);

        sku.Quantity.Should().Be(50, "no stock may be touched for a complete-delivery order that cannot be filled");
    }

    /// <summary>
    /// Two lines asking for the same product compete for the same stock. Checking each line
    /// against the full availability separately would wrongly let this order through.
    /// </summary>
    [TestMethod]
    public async Task CompleteDeliveryRequired_Should_NotAllocate_When_Two_Lines_Share_A_Product()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = true,
            OrderLines =
            [
                new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 30 },
                new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 30 }
            ]
        };

        // Enough for either line alone, but not for both together.
        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 40 };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        order.Status.Should().Be(OrderStatus.Released);

        _allocationRepository.Verify(x => x.AddAsync(It.IsAny<Allocation>()), Times.Never);

        sku.Quantity.Should().Be(40);
    }

    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Mark_Order_As_Allocated()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = false,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 50 }]
        };

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 100 };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        order.Status.Should().Be(OrderStatus.Allocated);

        sku.Quantity.Should().Be(50);
    }

    [TestMethod]
    public async Task AllocateOrder_Should_Set_Status_To_PartiallyAllocated_When_Stock_Is_Insufficient()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = false,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 100 }]
        };

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 50 };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        order.Status.Should().Be(OrderStatus.PartiallyAllocated);

        sku.Quantity.Should().Be(0);
    }

    /// <summary>
    /// Nothing was reserved, so the order must stay Released and be retried later rather
    /// than being labelled partially allocated.
    /// </summary>
    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Leave_Order_Released_When_No_Stock_Exists()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = false,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 100 }]
        };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        SetupAvailableSkus(productId);

        await CreateService().AllocateReleasedOrdersAsync();

        order.Status.Should().Be(OrderStatus.Released);

        _allocationRepository.Verify(x => x.AddAsync(It.IsAny<Allocation>()), Times.Never);
    }

    /// <summary>
    /// An order returned to Released by an inventory correction still carries its deactivated
    /// allocations. Those must not count towards the newly allocated total.
    /// </summary>
    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Ignore_Deactivated_Allocations_From_Previous_Run()
    {
        var productId = Guid.NewGuid();

        var orderLine = new OrderLine
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            RequestedQuantity = 100
        };

        // Left over from a previous allocation that was reversed.
        orderLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = orderLine.Id,
            Quantity = 100,
            IsActive = false
        });

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = false,
            OrderLines = [orderLine]
        };

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 40 };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        order.Status.Should().Be(OrderStatus.PartiallyAllocated,
            "only the 40 newly allocated units count, not the 100 from the reversed allocation");
    }

    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Skip_Cancelled_Lines()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            OrderLines =
            [
                new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 10 },
                new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 10, IsCancelled = true }
            ]
        };

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 100 };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        sku.Quantity.Should().Be(90, "only the live line may consume stock");

        order.Status.Should().Be(OrderStatus.Allocated);

        _allocationRepository.Verify(x => x.AddAsync(It.IsAny<Allocation>()), Times.Once);
    }

    [TestMethod]
    public async Task Allocation_Should_Split_Across_Multiple_Skus()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 25 }]
        };

        var sku1 = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 20 };
        var sku2 = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 15 };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        SetupAvailableSkus(productId, sku1, sku2);

        await CreateService().AllocateReleasedOrdersAsync();

        _allocationRepository.Verify(x => x.AddAsync(It.IsAny<Allocation>()), Times.Exactly(2));

        sku1.Quantity.Should().Be(0);
        sku2.Quantity.Should().Be(10);
        order.Status.Should().Be(OrderStatus.Allocated);
    }

    /// <summary>
    /// One SKU serving several orders is explicitly allowed as long as the total is sufficient.
    /// </summary>
    [TestMethod]
    public async Task Single_Sku_Should_Serve_Multiple_Orders()
    {
        var productId = Guid.NewGuid();

        var firstOrder = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 5 }]
        };

        var secondOrder = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            OrderLines = [new OrderLine { Id = Guid.NewGuid(), ProductId = productId, RequestedQuantity = 7 }]
        };

        var sku = new Sku { Id = Guid.NewGuid(), ProductId = productId, Quantity = 15 };

        _orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([firstOrder, secondOrder]);

        SetupAvailableSkus(productId, sku);

        await CreateService().AllocateReleasedOrdersAsync();

        firstOrder.Status.Should().Be(OrderStatus.Allocated);
        secondOrder.Status.Should().Be(OrderStatus.Allocated);
        sku.Quantity.Should().Be(3);
    }
}
