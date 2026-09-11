using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;
using WHMAllocation.Infrastructure.Persistence.Seed;

namespace WHMAllocation.Tests.Seed;

/// <summary>
/// The demo dataset exists to exercise specific behaviours. These tests pin the arithmetic
/// those demonstrations depend on, so a later edit to a quantity cannot quietly destroy the
/// scenario it was chosen to show.
/// </summary>
[TestClass]
public class SeedDataTests
{
    private static SeedGraph Graph => SeedData.Build();

    private static Sku SkuFor(SeedGraph graph, string productNumber, string locationCode)
    {
        var product = graph.Products.Single(x => x.ProductNumber == productNumber);
        var location = graph.Locations.Single(x => x.Code == locationCode);

        return graph.Skus.Single(x => x.ProductId == product.Id && x.WarehouseLocationId == location.Id);
    }

    private static int UnlockedStockFor(SeedGraph graph, string productNumber)
    {
        var product = graph.Products.Single(x => x.ProductNumber == productNumber);

        var unlockedLocationIds = graph.Locations
            .Where(x => !x.IsLocked)
            .Select(x => x.Id)
            .ToHashSet();

        return graph.Skus
            .Where(x => x.ProductId == product.Id && unlockedLocationIds.Contains(x.WarehouseLocationId))
            .Sum(x => x.Quantity);
    }

    private static Order OrderFor(SeedGraph graph, string orderNumber) =>
        graph.Orders.Single(x => x.OrderNumber == orderNumber);

    [TestMethod]
    public void Seed_Should_Be_Deterministic()
    {
        var first = SeedData.Build();
        var second = SeedData.Build();

        second.Orders.Select(x => x.Id).Should().Equal(first.Orders.Select(x => x.Id));
        second.Orders.Select(x => x.CreatedAt).Should().Equal(first.Orders.Select(x => x.CreatedAt));
        second.Skus.Select(x => x.Id).Should().Equal(first.Skus.Select(x => x.Id));
    }

    [TestMethod]
    public void Seed_Should_Use_Unique_Business_Keys()
    {
        var graph = Graph;

        graph.Orders.Select(x => x.OrderNumber).Should().OnlyHaveUniqueItems();
        graph.Products.Select(x => x.ProductNumber).Should().OnlyHaveUniqueItems();
        graph.Locations.Select(x => x.Code).Should().OnlyHaveUniqueItems();

        graph.Skus.Select(x => x.Id).Should().OnlyHaveUniqueItems();
        graph.Orders.SelectMany(x => x.OrderLines).Select(x => x.Id).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void Every_Order_Should_Start_Released_And_Unallocated()
    {
        var graph = Graph;

        graph.Orders.Should().OnlyContain(x => x.Status == OrderStatus.Released);

        graph.Orders.SelectMany(x => x.OrderLines)
            .Should().OnlyContain(x => !x.IsCancelled && x.Allocations.Count == 0);
    }

    [TestMethod]
    public void Seed_Should_Reference_Only_Known_Products_And_Locations()
    {
        var graph = Graph;

        var productIds = graph.Products.Select(x => x.Id).ToHashSet();
        var locationIds = graph.Locations.Select(x => x.Id).ToHashSet();

        graph.Skus.Should().OnlyContain(x => productIds.Contains(x.ProductId));
        graph.Skus.Should().OnlyContain(x => locationIds.Contains(x.WarehouseLocationId));

        graph.Orders.SelectMany(x => x.OrderLines)
            .Should().OnlyContain(x => productIds.Contains(x.ProductId));
    }

    /// <summary>
    /// A locked location holding a large quantity makes any allocation that wrongly draws on
    /// it obvious at a glance.
    /// </summary>
    [TestMethod]
    public void Locked_Location_Should_Hold_Stock_That_Must_Never_Be_Allocated()
    {
        var graph = Graph;

        var quarantine = graph.Locations.Single(x => x.Code == "Q-99-01");

        quarantine.IsLocked.Should().BeTrue();

        var quarantinedStock = graph.Skus.Where(x => x.WarehouseLocationId == quarantine.Id).ToList();

        quarantinedStock.Should().NotBeEmpty();
        quarantinedStock.Should().OnlyContain(x => x.Quantity > 0);

        graph.Locations.Where(x => x.Code != "Q-99-01").Should().OnlyContain(x => !x.IsLocked);
    }

    [TestMethod]
    public void Ord1001_Should_Need_More_Than_A_Single_Sku_Can_Supply()
    {
        var graph = Graph;

        var requested = OrderFor(graph, "ORD-1001").OrderLines.Single().RequestedQuantity;

        var largestSingleSku = SkuFor(graph, "FRJ-001", "A-01-01").Quantity;

        requested.Should().BeGreaterThan(largestSingleSku,
            "the line has to split across more than one SKU");

        requested.Should().BeLessThanOrEqualTo(UnlockedStockFor(graph, "FRJ-001"),
            "but must still be fully satisfiable from unlocked stock");
    }

    /// <summary>
    /// ORD-1001 and ORD-1002 are both High priority, so the earlier timestamp decides, and
    /// what is left over must be a genuine partial.
    /// </summary>
    [TestMethod]
    public void Ord1002_Should_Demonstrate_Fifo_And_Partial_Allocation()
    {
        var graph = Graph;

        var first = OrderFor(graph, "ORD-1001");
        var second = OrderFor(graph, "ORD-1002");

        first.Priority.Should().Be(Priority.High);
        second.Priority.Should().Be(Priority.High);
        first.CreatedAt.Should().BeBefore(second.CreatedAt);

        int available = UnlockedStockFor(graph, "FRJ-001");
        int takenFirst = first.OrderLines.Single().RequestedQuantity;
        int requestedSecond = second.OrderLines.Single().RequestedQuantity;

        int remainder = available - takenFirst;

        remainder.Should().BeGreaterThan(0, "something must be left to allocate");
        remainder.Should().BeLessThan(requestedSecond, "but not enough, so the order lands on PartiallyAllocated");
    }

    [TestMethod]
    public void Ord1003_Should_Be_A_Complete_Delivery_Order_That_Can_Be_Filled()
    {
        var graph = Graph;

        var order = OrderFor(graph, "ORD-1003");

        order.CompleteDeliveryRequired.Should().BeTrue();

        order.OrderLines.Single().RequestedQuantity
            .Should().BeLessThanOrEqualTo(UnlockedStockFor(graph, "FRJ-002"));
    }

    /// <summary>
    /// The regression case: two lines for one product. Each line alone fits inside available
    /// stock, so only checking demand per product catches that the order cannot be delivered.
    /// </summary>
    [TestMethod]
    public void Ord1004_Should_Only_Be_Unfillable_When_Lines_Are_Summed_Per_Product()
    {
        var graph = Graph;

        var order = OrderFor(graph, "ORD-1004");

        order.CompleteDeliveryRequired.Should().BeTrue();
        order.OrderLines.Should().HaveCount(2);
        order.OrderLines.Select(x => x.ProductId).Distinct().Should().HaveCount(1,
            "both lines must ask for the same product");

        int available = UnlockedStockFor(graph, "SNK-010");

        order.OrderLines.Should().OnlyContain(x => x.RequestedQuantity <= available,
            "each line on its own looks satisfiable");

        order.OrderLines.Sum(x => x.RequestedQuantity).Should().BeGreaterThan(available,
            "but together they exceed stock, so the order must receive nothing");
    }

    /// <summary>
    /// ORD-1004 is refused, so its crackers stay on the shelf and the Low priority order
    /// behind it is still served in full.
    /// </summary>
    [TestMethod]
    public void Ord1006_Should_Be_Low_Priority_Yet_Still_Fillable()
    {
        var graph = Graph;

        var order = OrderFor(graph, "ORD-1006");

        order.Priority.Should().Be(Priority.Low);

        order.OrderLines.Single().RequestedQuantity
            .Should().BeLessThanOrEqualTo(UnlockedStockFor(graph, "SNK-010"));
    }

    /// <summary>
    /// Correction needs a second SKU of the same product to substitute from, and that SKU
    /// must survive the initial allocation run untouched.
    /// </summary>
    [TestMethod]
    public void Apple_Juice_Should_Retain_A_Substitute_Sku_After_Allocation()
    {
        var graph = Graph;

        var primary = SkuFor(graph, "FRJ-002", "A-01-01");
        var substitute = SkuFor(graph, "FRJ-002", "B-02-01");

        substitute.Quantity.Should().BeGreaterThan(0);

        int demand = OrderFor(graph, "ORD-1003").OrderLines.Single().RequestedQuantity;

        demand.Should().BeLessThanOrEqualTo(primary.Quantity,
            "the primary SKU alone covers the order, leaving the substitute intact for a correction demo");
    }

    [TestMethod]
    public void Seed_Should_Cover_Every_Priority_And_Both_Delivery_Modes()
    {
        var graph = Graph;

        graph.Orders.Select(x => x.Priority).Distinct()
            .Should().BeEquivalentTo(new[] { Priority.Low, Priority.Normal, Priority.High });

        graph.Orders.Should().Contain(x => x.CompleteDeliveryRequired);
        graph.Orders.Should().Contain(x => !x.CompleteDeliveryRequired);
    }
}
