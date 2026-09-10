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
public class OrderCancellationServiceTests
{
    private Mock<IUnitOfWork> _unitOfWork = null!;
    private Mock<IOrderRepository> _orderRepository = null!;
    private Mock<IAllocationRepository> _allocationRepository = null!;
    private Mock<ISkuRepository> _skuRepository = null!;

    [TestInitialize]
    public void Setup()
    {
        _unitOfWork = new Mock<IUnitOfWork>();
        _orderRepository = new Mock<IOrderRepository>();
        _allocationRepository = new Mock<IAllocationRepository>();
        _skuRepository = new Mock<ISkuRepository>();
    }

    private OrderCancellationService CreateService() =>
        new(_unitOfWork.Object, _orderRepository.Object, _allocationRepository.Object, _skuRepository.Object);

    private void SetupSkus(params Sku[] skus)
    {
        foreach (var sku in skus)
        {
            var captured = sku;
            _skuRepository.Setup(x => x.GetByIdAsync(captured.Id)).ReturnsAsync(captured);
        }
    }

    private void SetupAllocationsByLine(params OrderLine[] orderLines)
    {
        foreach (var orderLine in orderLines)
        {
            var captured = orderLine;
            _allocationRepository
                .Setup(x => x.GetByOrderLineIdAsync(captured.Id))
                .ReturnsAsync(() => captured.Allocations.ToList());
        }
    }

    [TestMethod]
    public async Task CancelOrderLine_Should_Release_Allocated_Stock()
    {
        var sku = new Sku { Id = Guid.NewGuid(), Quantity = 0 };

        var orderLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 20 };

        var allocation = new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = orderLine.Id,
            SkuId = sku.Id,
            Quantity = 20,
            IsActive = true
        };

        orderLine.Allocations.Add(allocation);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Allocated,
            OrderLines = [orderLine]
        };

        _orderRepository.Setup(x => x.GetByOrderLineIdAsync(orderLine.Id)).ReturnsAsync(order);

        SetupAllocationsByLine(orderLine);
        SetupSkus(sku);

        await CreateService().CancelOrderLineAsync(orderLine.Id);

        sku.Quantity.Should().Be(20);
        allocation.IsActive.Should().BeFalse();
        orderLine.IsCancelled.Should().BeTrue();
    }

    /// <summary>
    /// A line cancellation is a standalone operation, so it has to commit on its own.
    /// </summary>
    [TestMethod]
    public async Task CancelOrderLine_Should_Persist_Changes()
    {
        var sku = new Sku { Id = Guid.NewGuid(), Quantity = 0 };

        var orderLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 20 };

        orderLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = orderLine.Id,
            SkuId = sku.Id,
            Quantity = 20,
            IsActive = true
        });

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Allocated,
            OrderLines = [orderLine]
        };

        _orderRepository.Setup(x => x.GetByOrderLineIdAsync(orderLine.Id)).ReturnsAsync(order);

        SetupAllocationsByLine(orderLine);
        SetupSkus(sku);

        await CreateService().CancelOrderLineAsync(orderLine.Id);

        _unitOfWork.Verify(x => x.SaveChangesAsync(), Times.Once);
    }

    [TestMethod]
    public async Task CancelOrderLine_Should_Downgrade_Order_Status_To_PartiallyAllocated()
    {
        var skuOne = new Sku { Id = Guid.NewGuid(), Quantity = 0 };
        var skuTwo = new Sku { Id = Guid.NewGuid(), Quantity = 0 };

        var cancelledLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 10 };
        cancelledLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = cancelledLine.Id,
            SkuId = skuOne.Id,
            Quantity = 10,
            IsActive = true
        });

        // Survives the cancellation but is only half covered.
        var remainingLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 10 };
        remainingLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = remainingLine.Id,
            SkuId = skuTwo.Id,
            Quantity = 5,
            IsActive = true
        });

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Allocated,
            OrderLines = [cancelledLine, remainingLine]
        };

        _orderRepository.Setup(x => x.GetByOrderLineIdAsync(cancelledLine.Id)).ReturnsAsync(order);

        SetupAllocationsByLine(cancelledLine, remainingLine);
        SetupSkus(skuOne, skuTwo);

        await CreateService().CancelOrderLineAsync(cancelledLine.Id);

        skuOne.Quantity.Should().Be(10);
        order.Status.Should().Be(OrderStatus.PartiallyAllocated);
    }

    [TestMethod]
    public async Task CancelOrderLine_Should_Cancel_Order_When_Last_Line_Is_Cancelled()
    {
        var sku = new Sku { Id = Guid.NewGuid(), Quantity = 0 };

        var orderLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 10 };
        orderLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = orderLine.Id,
            SkuId = sku.Id,
            Quantity = 10,
            IsActive = true
        });

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Allocated,
            OrderLines = [orderLine]
        };

        _orderRepository.Setup(x => x.GetByOrderLineIdAsync(orderLine.Id)).ReturnsAsync(order);

        SetupAllocationsByLine(orderLine);
        SetupSkus(sku);

        await CreateService().CancelOrderLineAsync(orderLine.Id);

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [TestMethod]
    public async Task CancelOrderLine_Should_Be_NoOp_When_Line_Already_Cancelled()
    {
        var orderLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 10, IsCancelled = true };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            OrderLines = [orderLine]
        };

        _orderRepository.Setup(x => x.GetByOrderLineIdAsync(orderLine.Id)).ReturnsAsync(order);

        await CreateService().CancelOrderLineAsync(orderLine.Id);

        _unitOfWork.Verify(x => x.SaveChangesAsync(), Times.Never);
    }

    [TestMethod]
    public async Task CancelOrder_Should_Release_Allocated_Stock_On_Every_Line()
    {
        var skuOne = new Sku { Id = Guid.NewGuid(), Quantity = 0 };
        var skuTwo = new Sku { Id = Guid.NewGuid(), Quantity = 3 };

        var firstLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 20 };
        firstLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = firstLine.Id,
            SkuId = skuOne.Id,
            Quantity = 20,
            IsActive = true
        });

        var secondLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 7 };
        secondLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = secondLine.Id,
            SkuId = skuTwo.Id,
            Quantity = 7,
            IsActive = true
        });

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Allocated,
            OrderLines = [firstLine, secondLine]
        };

        _orderRepository.Setup(x => x.GetByIdAsync(order.Id)).ReturnsAsync(order);

        SetupAllocationsByLine(firstLine, secondLine);
        SetupSkus(skuOne, skuTwo);

        await CreateService().CancelOrderAsync(order.Id);

        skuOne.Quantity.Should().Be(20);
        skuTwo.Quantity.Should().Be(10);

        order.Status.Should().Be(OrderStatus.Cancelled);
        firstLine.IsCancelled.Should().BeTrue();
        secondLine.IsCancelled.Should().BeTrue();

        _unitOfWork.Verify(x => x.SaveChangesAsync(), Times.Once);
    }

    /// <summary>
    /// Cancelling twice must not credit the stock a second time.
    /// </summary>
    [TestMethod]
    public async Task CancelOrder_Should_Be_NoOp_When_Order_Already_Cancelled()
    {
        var sku = new Sku { Id = Guid.NewGuid(), Quantity = 20 };

        var orderLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 20, IsCancelled = true };
        orderLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = orderLine.Id,
            SkuId = sku.Id,
            Quantity = 20,
            IsActive = false
        });

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Cancelled,
            OrderLines = [orderLine]
        };

        _orderRepository.Setup(x => x.GetByIdAsync(order.Id)).ReturnsAsync(order);

        SetupAllocationsByLine(orderLine);
        SetupSkus(sku);

        await CreateService().CancelOrderAsync(order.Id);

        sku.Quantity.Should().Be(20);
        _unitOfWork.Verify(x => x.SaveChangesAsync(), Times.Never);
    }

    [TestMethod]
    public async Task CancelOrder_Should_Ignore_Inactive_Allocations()
    {
        var sku = new Sku { Id = Guid.NewGuid(), Quantity = 5 };

        var orderLine = new OrderLine { Id = Guid.NewGuid(), RequestedQuantity = 20 };
        orderLine.Allocations.Add(new Allocation
        {
            Id = Guid.NewGuid(),
            OrderLineId = orderLine.Id,
            SkuId = sku.Id,
            Quantity = 20,
            IsActive = false
        });

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Released,
            OrderLines = [orderLine]
        };

        _orderRepository.Setup(x => x.GetByIdAsync(order.Id)).ReturnsAsync(order);

        SetupAllocationsByLine(orderLine);
        SetupSkus(sku);

        await CreateService().CancelOrderAsync(order.Id);

        sku.Quantity.Should().Be(5, "an already released allocation must not be credited twice");
        order.Status.Should().Be(OrderStatus.Cancelled);
    }
}
