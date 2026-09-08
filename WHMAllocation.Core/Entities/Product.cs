namespace WHMAllocation.Core.Entities;

public class Product : BaseEntity
{
    public string ProductNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}