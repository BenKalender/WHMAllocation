using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Services;

[TestClass]
public class AllocationServiceTests
{
    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Process_HighPriority_First()
    {
        // Arrange

        // Act

        // Assert
    }

    [TestMethod]
    public async Task CompleteDeliveryRequired_Should_NotAllocate_When_Stock_Is_Insufficient()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = true,
            OrderLines =
            [
                new OrderLine
                {
                    ProductId = productId,
                    RequestedQuantity = 100
                }
            ]
        };

        var sku = new Sku
        {
            ProductId = productId,
            Quantity = 50
        };
        var unitOfWork = new Mock<IUnitOfWork>();
        var orderRepository = new Mock<IOrderRepository>();
        var skuRepository = new Mock<ISkuRepository>();
        var allocationRepository = new Mock<IAllocationRepository>();

        orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        skuRepository.Setup(x => x.GetAvailableSkusByProductAsync(productId)).ReturnsAsync([sku]);

        var service = new AllocationService(unitOfWork.Object, orderRepository.Object, skuRepository.Object, allocationRepository.Object);

        await service.AllocateReleasedOrdersAsync();

        order.Status.Should().Be(OrderStatus.Released);

        allocationRepository.Verify(x => x.AddAsync(It.IsAny<Allocation>()), Times.Never);
    }

    [TestMethod]
    public async Task AllocateOrder_Should_Set_Status_To_PartiallyAllocated_When_Stock_Is_Insufficient()
    {
        // Arrange

        // Act

        // Assert

        // Need = 100

        // Stock = 50

        // CompleteDeliveryRequired = false

        // Expected as OrderStatus.PartiallyAllocated
    }

    [TestMethod]
    public async Task CancelOrder_Should_Release_Allocated_Stock()
    {
        // Arrange

        var sku = new Sku
        {
            Quantity = 0
        };

        var allocation = new Allocation
        {
            Quantity = 20,
            IsActive = true
        };

        // Act

        // CancelOrderAsync()

        // Assert

        // sku.Quantity.Should().Be(20);

        // allocation.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task InventoryCorrection_Should_Use_Substitute_Sku_When_Available()
    {
        // Arrange

        // SKU1 correction
        // missing quantity = 5

        // SKU2 available = 10

        // Act

        // Assert
    }

    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Mark_Order_As_Allocated()   
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = false,
            OrderLines =
            [
                new OrderLine
                {
                    ProductId = productId,
                    RequestedQuantity = 50
                }
            ]
        };

        var sku = new Sku
        {
            ProductId = productId,
            Quantity = 100
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        var orderRepository = new Mock<IOrderRepository>();
        var skuRepository = new Mock<ISkuRepository>();
        var allocationRepository = new Mock<IAllocationRepository>();

        orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        skuRepository.Setup(x => x.GetAvailableSkusByProductAsync(productId)).ReturnsAsync([sku]);

        var service = new AllocationService(unitOfWork.Object, orderRepository.Object, skuRepository.Object, allocationRepository.Object);

        await service.AllocateReleasedOrdersAsync();

        order.Status.Should().Be(OrderStatus.Allocated);
    }

    [TestMethod]
    public async Task AllocateReleasedOrders_Should_Mark_Order_As_PartiallyAllocated()
    {
        // Arrange
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Status = OrderStatus.Released,
            CompleteDeliveryRequired = false,
            OrderLines =
            [
                new OrderLine
                {
                    ProductId = productId,
                    RequestedQuantity = 100    
                }
            ]
        };

        var sku = new Sku
        {
            ProductId = productId,
            Quantity = 50  
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        var orderRepository = new Mock<IOrderRepository>();
        var skuRepository = new Mock<ISkuRepository>();
        var allocationRepository = new Mock<IAllocationRepository>();
        

        orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);
        skuRepository.Setup(x => x.GetAvailableSkusByProductAsync(productId)).ReturnsAsync([sku]);

        var service = new AllocationService(unitOfWork.Object, orderRepository.Object, skuRepository.Object, allocationRepository.Object);

        // Act
        await service.AllocateReleasedOrdersAsync();

        // Assert
        order.Status.Should().Be(OrderStatus.PartiallyAllocated);

    }

    [TestMethod]
    public async Task Allocation_Should_Split_Across_Multiple_Skus()
    {
        var productId = Guid.NewGuid();

        var order = new Order
        {
            Status = OrderStatus.Released,
            OrderLines =
            [
                new OrderLine
                {
                    ProductId = productId,
                    RequestedQuantity = 25
                }
            ]
        };

        var sku1 = new Sku
        {
            ProductId = productId,
            Quantity = 20
        };

        var sku2 = new Sku
        {
            ProductId = productId,
            Quantity = 15
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        var orderRepository = new Mock<IOrderRepository>();
        var skuRepository = new Mock<ISkuRepository>();
        var allocationRepository = new Mock<IAllocationRepository>();

        orderRepository.Setup(x => x.GetReleasedOrdersAsync()).ReturnsAsync([order]);

        skuRepository.Setup(x => x.GetAvailableSkusByProductAsync(productId)).ReturnsAsync([sku1, sku2]);

        var service = new AllocationService(unitOfWork.Object, orderRepository.Object, skuRepository.Object, allocationRepository.Object);

        await service.AllocateReleasedOrdersAsync();

        allocationRepository.Verify(x => x.AddAsync(It.IsAny<Allocation>()), Times.Exactly(2));
    }
}