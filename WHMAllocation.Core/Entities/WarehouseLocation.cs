namespace WHMAllocation.Core.Entities;

public class WarehouseLocation : BaseEntity
{
    public string Code { get; set; } = string.Empty;

    public bool IsLocked { get; set; }
}