namespace WHMAllocation.Core.Entities;

public class Sku : BaseEntity
{
    public Guid ProductId { get; set; }

    public Guid WarehouseLocationId { get; set; }

    public int Quantity { get; set; }

    // Navigation Properties

    public Product Product { get; set; } = null!;

    public WarehouseLocation WarehouseLocation { get; set; } = null!;
}