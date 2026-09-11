using WHMAllocation.Core.Results;

namespace WHMAllocation.Core.Interfaces.Services;

public interface IInventoryCorrectionService
{
    /// <summary>
    /// Applies a manual physical stock count to a SKU and repairs any reservation it invalidates.
    /// </summary>
    /// <param name="skuId">The SKU being counted.</param>
    /// <param name="newQuantity">
    /// The corrected <em>physical</em> quantity. Available stock is derived from it, not
    /// assigned — see the SKU quantity convention in the project documentation.
    /// </param>
    /// <returns>
    /// How far the correction had to escalate: no shortfall, covered by substitutes, orders
    /// downgraded, or orders released in full.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="newQuantity"/> is negative. This is a caller precondition rather than a
    /// domain outcome: a UI is expected to reject a negative count before calling.
    /// </exception>
    Task<CorrectionResult> CorrectSkuQuantityAsync(Guid skuId, int newQuantity);
}
