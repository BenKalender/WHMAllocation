namespace WHMAllocation.Core.Interfaces.Services;

public interface IInventoryCorrectionService
{
    Task CorrectSkuQuantityAsync(
        Guid skuId,
        int newQuantity);
}