using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Services;

namespace WHMAllocation.Tests.Services;

[TestClass]
public class OrderCancellationServiceTests
{
    [TestMethod]
    public async Task CancelOrderLine_Should_Release_Allocated_Stock()
    {
        // Arrange
        var skuId = Guid.NewGuid();

        var sku = new Sku
        {
            Id = skuId,
            Quantity = 0
        };

        var allocation = new Allocation
        {
            Id = Guid.NewGuid(),
            SkuId = skuId,
            Quantity = 20,
            IsActive = true
        };

        var orderRepository = new Mock<IOrderRepository>();

        var allocationRepository = new Mock<IAllocationRepository>();

        var skuRepository = new Mock<ISkuRepository>();

        allocationRepository.Setup(x => x.GetByOrderLineIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync([allocation]);

        skuRepository.Setup(x => x.GetByIdAsync(skuId))
            .ReturnsAsync(sku);

        var service = new OrderCancellationService(
                orderRepository.Object,
                allocationRepository.Object,
                skuRepository.Object);

        // Act
        await service.CancelOrderLineAsync(Guid.NewGuid());

        // Assert
        sku.Quantity.Should().Be(20);
        allocation.IsActive.Should().BeFalse();
    }
}