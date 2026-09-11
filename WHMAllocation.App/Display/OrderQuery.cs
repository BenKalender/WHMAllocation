using WHMAllocation.Core.Entities;
using WHMAllocation.Core.Enums;

namespace WHMAllocation.App.Display;

/// <summary>How much of an order is actually reserved.</summary>
public enum AllocationState
{
    None,
    Partial,
    Full
}

/// <summary>Columns the orders table can be sorted by.</summary>
public enum OrderSortColumn
{
    Number,
    Priority,
    CompleteDelivery,
    Status,
    Allocated,
    Created
}

/// <summary>
/// A parsed filter expression. Every populated field narrows the result further — the terms
/// combine with AND.
/// </summary>
/// <param name="HasInvalidToken">
/// Set when the expression contained an unknown key or an unusable value. Such a filter matches
/// nothing, so a typo shows up as an empty table rather than being silently ignored.
/// </param>
public sealed record OrderFilter(
    Priority? Priority,
    OrderStatus? Status,
    bool? CompleteDelivery,
    AllocationState? Allocated,
    string? FreeText,
    bool HasInvalidToken)
{
    public static OrderFilter Empty { get; } = new(null, null, null, null, null, false);

    public bool IsEmpty =>
        Priority is null && Status is null && CompleteDelivery is null
        && Allocated is null && string.IsNullOrWhiteSpace(FreeText) && !HasInvalidToken;
}

/// <summary>
/// Filtering and sorting for the orders table. Pure in-memory logic over loaded entities: the
/// list is small and loads in full, so there is no server-side querying to do here.
/// </summary>
public static class OrderQuery
{
    /// <summary>Units the live (non-cancelled) lines asked for.</summary>
    public static int RequestedQuantity(Order order) =>
        order.OrderLines.Where(x => !x.IsCancelled).Sum(x => x.RequestedQuantity);

    /// <summary>Units currently reserved. Deactivated allocations do not count.</summary>
    public static int AllocatedQuantity(Order order) =>
        order.OrderLines.SelectMany(x => x.Allocations).Where(x => x.IsActive).Sum(x => x.Quantity);

    public static AllocationState StateOf(Order order)
    {
        int requested = RequestedQuantity(order);
        int allocated = AllocatedQuantity(order);

        // An order whose lines are all cancelled asks for nothing. Without this guard the
        // `allocated >= requested` test below would read 0 >= 0 as fully allocated.
        if (requested == 0 || allocated == 0)
            return AllocationState.None;

        return allocated >= requested ? AllocationState.Full : AllocationState.Partial;
    }

    // ---- Sorting -------------------------------------------------------------------

    public static IEnumerable<Order> Sort(IEnumerable<Order> orders, OrderSortColumn column, bool descending)
    {
        Func<Order, IComparable> key = column switch
        {
            OrderSortColumn.Number => x => x.OrderNumber,
            OrderSortColumn.Priority => x => (int)x.Priority,
            OrderSortColumn.CompleteDelivery => x => x.CompleteDeliveryRequired,
            OrderSortColumn.Status => x => (int)x.Status,
            OrderSortColumn.Allocated => x => AllocatedQuantity(x),
            OrderSortColumn.Created => x => x.CreatedAt,
            _ => x => x.OrderNumber
        };

        var sorted = descending
            ? orders.OrderByDescending(key)
            : orders.OrderBy(key);

        // Order number is the tie-breaker so equal keys never shuffle between renders.
        return sorted.ThenBy(x => x.OrderNumber, StringComparer.Ordinal);
    }

    /// <summary>Descending reads more naturally for these; the rest start ascending.</summary>
    public static bool DefaultDescendingFor(OrderSortColumn column) =>
        column is OrderSortColumn.Priority or OrderSortColumn.Allocated or OrderSortColumn.Created;

    // ---- Filtering -----------------------------------------------------------------

    /// <summary>
    /// Parses a GitHub-style expression such as <c>priority:high status:released</c>. A token
    /// containing ':' is treated as a key/value pair; everything else is free text.
    /// A repeated key keeps its last occurrence.
    /// </summary>
    public static OrderFilter ParseFilter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return OrderFilter.Empty;

        Priority? priority = null;
        OrderStatus? status = null;
        bool? completeDelivery = null;
        AllocationState? allocated = null;
        bool invalid = false;

        var freeTextParts = new List<string>();

        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = token.IndexOf(':');

            if (separator <= 0)
            {
                freeTextParts.Add(token);
                continue;
            }

            var key = token[..separator].ToLowerInvariant();
            var value = token[(separator + 1)..].ToLowerInvariant();

            switch (key)
            {
                case "priority" when TryParsePriority(value, out var parsedPriority):
                    priority = parsedPriority;
                    break;

                case "status" when TryParseStatus(value, out var parsedStatus):
                    status = parsedStatus;
                    break;

                case "complete" when TryParseBool(value, out var parsedComplete):
                    completeDelivery = parsedComplete;
                    break;

                case "allocated" when TryParseAllocationState(value, out var parsedState):
                    allocated = parsedState;
                    break;

                default:
                    // Unknown key, or a known key with a value it cannot accept.
                    invalid = true;
                    break;
            }
        }

        var freeText = freeTextParts.Count == 0 ? null : string.Join(' ', freeTextParts);

        return new OrderFilter(priority, status, completeDelivery, allocated, freeText, invalid);
    }

    public static bool Matches(Order order, OrderFilter filter)
    {
        if (filter.HasInvalidToken)
            return false;

        if (filter.Priority is not null && order.Priority != filter.Priority)
            return false;

        if (filter.Status is not null && order.Status != filter.Status)
            return false;

        if (filter.CompleteDelivery is not null && order.CompleteDeliveryRequired != filter.CompleteDelivery)
            return false;

        if (filter.Allocated is not null && StateOf(order) != filter.Allocated)
            return false;

        if (!string.IsNullOrWhiteSpace(filter.FreeText) && !MatchesFreeText(order, filter.FreeText))
            return false;

        return true;
    }

    private static bool MatchesFreeText(Order order, string freeText)
    {
        if (order.OrderNumber.Contains(freeText, StringComparison.OrdinalIgnoreCase))
            return true;

        return order.OrderLines.Any(line =>
            line.Product is not null
            && (line.Product.Name.Contains(freeText, StringComparison.OrdinalIgnoreCase)
                || line.Product.ProductNumber.Contains(freeText, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryParsePriority(string value, out Priority priority)
    {
        switch (value)
        {
            case "low": priority = Priority.Low; return true;
            case "normal": priority = Priority.Normal; return true;
            case "high": priority = Priority.High; return true;
            default: priority = default; return false;
        }
    }

    private static bool TryParseStatus(string value, out OrderStatus status)
    {
        switch (value)
        {
            case "released": status = OrderStatus.Released; return true;
            case "allocated": status = OrderStatus.Allocated; return true;
            case "partial":
            case "partiallyallocated": status = OrderStatus.PartiallyAllocated; return true;
            case "cancelled": status = OrderStatus.Cancelled; return true;
            default: status = default; return false;
        }
    }

    private static bool TryParseBool(string value, out bool parsed)
    {
        switch (value)
        {
            case "yes":
            case "true": parsed = true; return true;
            case "no":
            case "false": parsed = false; return true;
            default: parsed = default; return false;
        }
    }

    private static bool TryParseAllocationState(string value, out AllocationState state)
    {
        switch (value)
        {
            case "none": state = AllocationState.None; return true;
            case "partial": state = AllocationState.Partial; return true;
            case "full": state = AllocationState.Full; return true;
            default: state = default; return false;
        }
    }
}
