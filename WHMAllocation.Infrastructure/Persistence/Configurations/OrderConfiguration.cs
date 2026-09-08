using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WHMAllocation.Core.Entities;

namespace WHMAllocation.Infrastructure.Persistence.Configurations;

public class OrderConfiguration
    : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(x => x.OrderNumber)
            .IsUnique();

        builder.Property(x => x.Priority)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.CompleteDeliveryRequired)
            .IsRequired();

        builder.HasMany(x => x.OrderLines)
            .WithOne(x => x.Order)
            .HasForeignKey(x => x.OrderId);
    }
}