namespace WHMAllocation.Core.Entities;

public class Allocation : BaseEntity
{
    public Guid OrderLineId { get; set; }

    public Guid SkuId { get; set; }

    public int Quantity { get; set; }

    public bool IsActive { get; set; } = true;

    public OrderLine OrderLine { get; set; } = null!;

    public Sku Sku { get; set; } = null!;
}