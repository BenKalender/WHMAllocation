namespace WHMAllocation.Core.Entities;

public class OrderLine : BaseEntity
{
    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public int RequestedQuantity { get; set; }

    public bool IsCancelled { get; set; }

    public Order Order { get; set; } = null!;

    public Product Product { get; set; } = null!;

    public ICollection<Allocation> Allocations { get; set; }
        = new List<Allocation>();
}