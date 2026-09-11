using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;

namespace WHMAllocation.Infrastructure.Persistence.Seed;

/// <summary>
/// The demo dataset, built as a pure object graph with no persistence concern, so the
/// scenarios it is designed to exercise can be asserted without a database.
/// </summary>
/// <remarks>
/// Identifiers and timestamps are fixed rather than generated: allocation order depends on
/// <see cref="Order.CreatedAt"/>, so a non-deterministic seed would produce a different demo
/// on every run.
///
/// Every order is seeded as <see cref="OrderStatus.Released"/>. Nothing is allocated up front —
/// running allocation is what the demo is for.
/// </remarks>
public static class SeedData
{
    /// <summary>Fixed clock for the seed, so FIFO ordering is reproducible.</summary>
    public static readonly DateTime BaseDate = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);

    // Stable ids, prefixed by entity type so they are recognisable in the database and in logs.
    private static Guid LocationId(int n) => new($"10000000-0000-0000-0000-{n:D12}");
    private static Guid ProductId(int n) => new($"20000000-0000-0000-0000-{n:D12}");
    private static Guid SkuId(int n) => new($"30000000-0000-0000-0000-{n:D12}");
    private static Guid OrderId(int n) => new($"40000000-0000-0000-0000-{n:D12}");
    private static Guid OrderLineId(int n) => new($"50000000-0000-0000-0000-{n:D12}");

    public static SeedGraph Build()
    {
        // --- Warehouse locations -------------------------------------------------------
        var pickFaceA1 = NewLocation(1, "A-01-01");
        var pickFaceA2 = NewLocation(2, "A-01-02");
        var bulkB2 = NewLocation(3, "B-02-01");

        // Locked: the SKU sitting here is deliberately large, so any allocation drawing on
        // it is immediately visible as a bug.
        var quarantine = NewLocation(4, "Q-99-01", isLocked: true);

        // --- Products ------------------------------------------------------------------
        var orangeJuice = NewProduct(1, "FRJ-001", "Orange Juice 1L");
        var appleJuice = NewProduct(2, "FRJ-002", "Apple Juice 1L");
        var crackers = NewProduct(3, "SNK-010", "Salted Crackers 200g");
        var sparklingWater = NewProduct(4, "BEV-020", "Sparkling Water 500ml");

        // --- SKUs ----------------------------------------------------------------------
        // Orange juice is spread over two usable locations so a single line has to split.
        var ojPickA1 = NewSku(1, orangeJuice, pickFaceA1, quantity: 20);
        var ojPickA2 = NewSku(2, orangeJuice, pickFaceA2, quantity: 12);
        var ojQuarantined = NewSku(3, orangeJuice, quarantine, quantity: 500);

        // Apple juice keeps a second SKU in reserve to act as a substitute during a correction.
        var ajPickA1 = NewSku(4, appleJuice, pickFaceA1, quantity: 30);
        var ajBulkB2 = NewSku(5, appleJuice, bulkB2, quantity: 10);

        // Deliberately scarce: 8 units against 10 demanded by ORD-1004.
        var crackersBulk = NewSku(6, crackers, bulkB2, quantity: 8);

        var waterPickA2 = NewSku(7, sparklingWater, pickFaceA2, quantity: 100);

        // --- Orders, all Released ------------------------------------------------------
        // Timestamps ascend with the order number, so FIFO within a priority is predictable.

        // Wants 25 of 32 available orange juice: must split across two SKUs.
        var ord1001 = NewOrder(1, "ORD-1001", Priority.High, completeDelivery: false, createdOffsetHours: 0);
        AddLine(ord1001, 1, orangeJuice, 25);

        // Same priority, later timestamp: only the 7 left after ORD-1001 remain,
        // so this lands on PartiallyAllocated and FIFO is visible.
        var ord1002 = NewOrder(2, "ORD-1002", Priority.High, completeDelivery: false, createdOffsetHours: 1);
        AddLine(ord1002, 2, orangeJuice, 10);

        // Complete delivery that succeeds: 20 of 40 available apple juice.
        var ord1003 = NewOrder(3, "ORD-1003", Priority.Normal, completeDelivery: true, createdOffsetHours: 2);
        AddLine(ord1003, 3, appleJuice, 20);

        // Complete delivery that must be refused. Two lines share one product: 5 + 5 = 10
        // against 8 in stock. Checking either line alone would wrongly let this through.
        var ord1004 = NewOrder(4, "ORD-1004", Priority.Normal, completeDelivery: true, createdOffsetHours: 3);
        AddLine(ord1004, 4, crackers, 5);
        AddLine(ord1004, 5, crackers, 5);

        // Plain order, the natural candidate for demonstrating cancellation.
        var ord1005 = NewOrder(5, "ORD-1005", Priority.Normal, completeDelivery: false, createdOffsetHours: 4);
        AddLine(ord1005, 6, sparklingWater, 15);

        // Low priority, yet still served: ORD-1004 refused the crackers, so all 8 remain.
        var ord1006 = NewOrder(6, "ORD-1006", Priority.Low, completeDelivery: false, createdOffsetHours: 5);
        AddLine(ord1006, 7, crackers, 8);

        return new SeedGraph(
            [pickFaceA1, pickFaceA2, bulkB2, quarantine],
            [orangeJuice, appleJuice, crackers, sparklingWater],
            [ojPickA1, ojPickA2, ojQuarantined, ajPickA1, ajBulkB2, crackersBulk, waterPickA2],
            [ord1001, ord1002, ord1003, ord1004, ord1005, ord1006]);
    }

    private static WarehouseLocation NewLocation(int n, string code, bool isLocked = false) => new()
    {
        Id = LocationId(n),
        Code = code,
        IsLocked = isLocked,
        CreatedAt = BaseDate
    };

    private static Product NewProduct(int n, string productNumber, string name) => new()
    {
        Id = ProductId(n),
        ProductNumber = productNumber,
        Name = name,
        CreatedAt = BaseDate
    };

    private static Sku NewSku(int n, Product product, WarehouseLocation location, int quantity) => new()
    {
        Id = SkuId(n),
        ProductId = product.Id,
        WarehouseLocationId = location.Id,
        Quantity = quantity,
        CreatedAt = BaseDate
    };

    private static Order NewOrder(int n, string orderNumber, Priority priority, bool completeDelivery, int createdOffsetHours) => new()
    {
        Id = OrderId(n),
        OrderNumber = orderNumber,
        Priority = priority,
        CompleteDeliveryRequired = completeDelivery,
        Status = OrderStatus.Released,
        CreatedAt = BaseDate.AddHours(createdOffsetHours)
    };

    private static void AddLine(Order order, int n, Product product, int requestedQuantity)
    {
        order.OrderLines.Add(new OrderLine
        {
            Id = OrderLineId(n),
            OrderId = order.Id,
            ProductId = product.Id,
            RequestedQuantity = requestedQuantity,
            CreatedAt = order.CreatedAt
        });
    }
}

/// <summary>Demo data in dependency order: locations and products, then SKUs, then orders.</summary>
public sealed record SeedGraph(
    IReadOnlyList<WarehouseLocation> Locations,
    IReadOnlyList<Product> Products,
    IReadOnlyList<Sku> Skus,
    IReadOnlyList<Order> Orders);
