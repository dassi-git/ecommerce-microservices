using InventoryService.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<InventoryItemEntity> InventoryItems => Set<InventoryItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItemEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProductId).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });
    }
}
