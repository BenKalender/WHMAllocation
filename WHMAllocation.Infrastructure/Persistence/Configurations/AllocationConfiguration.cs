using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WHMAllocation.Core.Entities;

namespace WHMAllocation.Infrastructure.Persistence.Configurations;

public class AllocationConfiguration
    : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> builder)
    {
        builder.ToTable("Allocations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.HasOne(x => x.Sku)
            .WithMany()
            .HasForeignKey(x => x.SkuId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
