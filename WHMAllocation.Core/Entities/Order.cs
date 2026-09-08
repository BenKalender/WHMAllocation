using WHMAllocation.Core.Enums;

namespace WHMAllocation.Core.Entities;

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;

    public Priority Priority { get; set; }

    public bool CompleteDeliveryRequired { get; set; }

    public OrderStatus Status { get; set; }

    public ICollection<OrderLine> OrderLines { get; set; }
        = new List<OrderLine>();
}