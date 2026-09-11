using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WHMAllocation.App.Display;
using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;

namespace WHMAllocation.Tests.Display;

/// <summary>
/// The filter parser is small but full of edge cases, and it is pure logic, so it is covered
/// directly rather than through the page.
/// </summary>
[TestClass]
public class OrderQueryTests
{
    private static Order BuildOrder(
        string orderNumber = "ORD-1001",
        Priority priority = Priority.Normal,
        OrderStatus status = OrderStatus.Released,
        bool completeDelivery = false,
        string productName = "Orange Juice 1L",
        string productNumber = "FRJ-001",
        int requested = 10,
        int allocated = 0,
        bool lineCancelled = false)
    {
        var line = new OrderLine
        {
            Id = Guid.NewGuid(),
            RequestedQuantity = requested,
            IsCancelled = lineCancelled,
            Product = new Product { Name = productName, ProductNumber = productNumber }
        };

        if (allocated > 0)
        {
            line.Allocations.Add(new Allocation { Quantity = allocated, IsActive = true });
        }

        return new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Priority = priority,
            Status = status,
            CompleteDeliveryRequired = completeDelivery,
            OrderLines = [line]
        };
    }

    private static bool Match(Order order, string filter) =>
        OrderQuery.Matches(order, OrderQuery.ParseFilter(filter));

    // ---- Parsing individual keys ----------------------------------------------------

    [TestMethod]
    public void Empty_Filter_Should_Match_Everything()
    {
        var filter = OrderQuery.ParseFilter("   ");

        filter.IsEmpty.Should().BeTrue();
        OrderQuery.Matches(BuildOrder(), filter).Should().BeTrue();
    }

    [TestMethod]
    public void Priority_Filter_Should_Match_Only_That_Priority()
    {
        Match(BuildOrder(priority: Priority.High), "priority:high").Should().BeTrue();
        Match(BuildOrder(priority: Priority.Low), "priority:high").Should().BeFalse();
    }

    [TestMethod]
    public void Status_Filter_Should_Accept_Partial_As_An_Alias()
    {
        var order = BuildOrder(status: OrderStatus.PartiallyAllocated);

        Match(order, "status:partial").Should().BeTrue();
        Match(order, "status:partiallyallocated").Should().BeTrue();
        Match(order, "status:released").Should().BeFalse();
    }

    [TestMethod]
    public void Complete_Filter_Should_Accept_Yes_No_And_Boolean_Words()
    {
        var required = BuildOrder(completeDelivery: true);
        var notRequired = BuildOrder(completeDelivery: false);

        Match(required, "complete:yes").Should().BeTrue();
        Match(required, "complete:true").Should().BeTrue();
        Match(notRequired, "complete:no").Should().BeTrue();
        Match(notRequired, "complete:yes").Should().BeFalse();
    }

    [TestMethod]
    public void Filters_Should_Be_Case_Insensitive()
    {
        var order = BuildOrder(priority: Priority.High);

        Match(order, "PRIORITY:HIGH").Should().BeTrue();
        Match(order, "Priority:High").Should().BeTrue();
    }

    // ---- Allocation state -----------------------------------------------------------

    [TestMethod]
    public void Allocated_Filter_Should_Separate_None_Partial_And_Full()
    {
        var none = BuildOrder(requested: 10, allocated: 0);
        var partial = BuildOrder(requested: 10, allocated: 4);
        var full = BuildOrder(requested: 10, allocated: 10);

        OrderQuery.StateOf(none).Should().Be(AllocationState.None);
        OrderQuery.StateOf(partial).Should().Be(AllocationState.Partial);
        OrderQuery.StateOf(full).Should().Be(AllocationState.Full);

        Match(none, "allocated:none").Should().BeTrue();
        Match(partial, "allocated:partial").Should().BeTrue();
        Match(full, "allocated:full").Should().BeTrue();
        Match(full, "allocated:none").Should().BeFalse();
    }

    /// <summary>
    /// An order whose lines are all cancelled requests nothing. Without a guard the
    /// "allocated >= requested" test reads 0 >= 0 as fully allocated.
    /// </summary>
    [TestMethod]
    public void Order_With_Only_Cancelled_Lines_Should_Be_None_Not_Full()
    {
        var order = BuildOrder(requested: 10, allocated: 0, lineCancelled: true);

        OrderQuery.RequestedQuantity(order).Should().Be(0);
        OrderQuery.StateOf(order).Should().Be(AllocationState.None);
        Match(order, "allocated:full").Should().BeFalse();
    }

    [TestMethod]
    public void Inactive_Allocations_Should_Not_Count_As_Allocated()
    {
        var order = BuildOrder(requested: 10);
        order.OrderLines.Single().Allocations.Add(new Allocation { Quantity = 10, IsActive = false });

        OrderQuery.AllocatedQuantity(order).Should().Be(0);
        OrderQuery.StateOf(order).Should().Be(AllocationState.None);
    }

    // ---- Free text ------------------------------------------------------------------

    [TestMethod]
    public void Free_Text_Should_Match_Order_Number_Or_Product()
    {
        var order = BuildOrder(orderNumber: "ORD-1001", productName: "Orange Juice 1L", productNumber: "FRJ-001");

        Match(order, "ORD-1001").Should().BeTrue();
        Match(order, "1001").Should().BeTrue();
        Match(order, "juice").Should().BeTrue("free text is case-insensitive");
        Match(order, "FRJ").Should().BeTrue();
        Match(order, "crackers").Should().BeFalse();
    }

    // ---- Combining ------------------------------------------------------------------

    [TestMethod]
    public void Multiple_Tokens_Should_Combine_With_And()
    {
        var order = BuildOrder(priority: Priority.High, status: OrderStatus.Released);

        Match(order, "priority:high status:released").Should().BeTrue();
        Match(order, "priority:high status:allocated").Should().BeFalse("both terms must hold");
    }

    [TestMethod]
    public void Free_Text_Should_Combine_With_Keys()
    {
        var order = BuildOrder(orderNumber: "ORD-1001", priority: Priority.High);

        Match(order, "priority:high ORD-1001").Should().BeTrue();
        Match(order, "priority:low ORD-1001").Should().BeFalse();
    }

    [TestMethod]
    public void Repeated_Key_Should_Keep_The_Last_Value()
    {
        var order = BuildOrder(priority: Priority.Low);

        Match(order, "priority:high priority:low").Should().BeTrue();
        Match(order, "priority:low priority:high").Should().BeFalse();
    }

    // ---- Invalid input ---------------------------------------------------------------

    [TestMethod]
    public void Unknown_Key_Should_Match_Nothing()
    {
        var filter = OrderQuery.ParseFilter("foo:bar");

        filter.HasInvalidToken.Should().BeTrue();
        OrderQuery.Matches(BuildOrder(), filter).Should().BeFalse();
    }

    [TestMethod]
    public void Known_Key_With_Invalid_Value_Should_Match_Nothing()
    {
        var filter = OrderQuery.ParseFilter("priority:banana");

        filter.HasInvalidToken.Should().BeTrue();
        OrderQuery.Matches(BuildOrder(priority: Priority.High), filter).Should().BeFalse(
            "a typo must surface as an empty table, not be silently ignored");
    }

    [TestMethod]
    public void One_Invalid_Token_Should_Invalidate_The_Whole_Expression()
    {
        var order = BuildOrder(priority: Priority.High);

        Match(order, "priority:high nonsense:value").Should().BeFalse();
    }

    // ---- Sorting ---------------------------------------------------------------------

    [TestMethod]
    public void Sort_Should_Order_By_Each_Column()
    {
        var low = BuildOrder(orderNumber: "ORD-1003", priority: Priority.Low, allocated: 1, requested: 10);
        var high = BuildOrder(orderNumber: "ORD-1001", priority: Priority.High, allocated: 9, requested: 10);
        var normal = BuildOrder(orderNumber: "ORD-1002", priority: Priority.Normal, allocated: 5, requested: 10);

        var orders = new[] { low, high, normal };

        OrderQuery.Sort(orders, OrderSortColumn.Number, descending: false)
            .Select(x => x.OrderNumber).Should().Equal("ORD-1001", "ORD-1002", "ORD-1003");

        OrderQuery.Sort(orders, OrderSortColumn.Number, descending: true)
            .Select(x => x.OrderNumber).Should().Equal("ORD-1003", "ORD-1002", "ORD-1001");

        OrderQuery.Sort(orders, OrderSortColumn.Priority, descending: true)
            .Select(x => x.Priority).Should().Equal(Priority.High, Priority.Normal, Priority.Low);

        OrderQuery.Sort(orders, OrderSortColumn.Allocated, descending: true)
            .Select(OrderQuery.AllocatedQuantity).Should().Equal(9, 5, 1);
    }

    [TestMethod]
    public void Sort_Should_Break_Ties_By_Order_Number()
    {
        var second = BuildOrder(orderNumber: "ORD-1002", priority: Priority.High);
        var first = BuildOrder(orderNumber: "ORD-1001", priority: Priority.High);

        // Equal orders must not shuffle between renders.
        OrderQuery.Sort(new[] { second, first }, OrderSortColumn.Priority, descending: true)
            .Select(x => x.OrderNumber).Should().Equal("ORD-1001", "ORD-1002");
    }
}
