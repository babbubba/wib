using Microsoft.EntityFrameworkCore;
using WIB.Domain;

namespace WIB.Infrastructure.Data;

public class WibDbContext : DbContext
{
    public WibDbContext(DbContextOptions<WibDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreLocation> StoreLocations => Set<StoreLocation>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductAlias> ProductAliases => Set<ProductAlias>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<BudgetMonth> BudgetMonths => Set<BudgetMonth>();
    public DbSet<ExpenseAggregate> ExpenseAggregates => Set<ExpenseAggregate>();
    public DbSet<LabelingEvent> LabelingEvents => Set<LabelingEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>()
            .HasOne(c => c.Parent)
            .WithMany(c => c!.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Product>()
            .HasOne(p => p.ProductType)
            .WithMany()
            .HasForeignKey(p => p.ProductTypeId);

        modelBuilder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId);

        modelBuilder.Entity<ProductAlias>()
            .HasOne(a => a.Product)
            .WithMany(p => p.Aliases)
            .HasForeignKey(a => a.ProductId);

        modelBuilder.Entity<Receipt>()
            .HasOne(r => r.Store)
            .WithMany(s => s.Receipts)
            .HasForeignKey(r => r.StoreId);

        modelBuilder.Entity<Receipt>()
            .HasOne(r => r.StoreLocation)
            .WithMany()
            .HasForeignKey(r => r.StoreLocationId);

        modelBuilder.Entity<ReceiptLine>()
            .HasOne(l => l.Receipt)
            .WithMany(r => r.Lines)
            .HasForeignKey(l => l.ReceiptId);

        modelBuilder.Entity<ReceiptLine>()
            .HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId);

        modelBuilder.Entity<PriceHistory>()
            .HasOne(ph => ph.Product)
            .WithMany()
            .HasForeignKey(ph => ph.ProductId);

        modelBuilder.Entity<PriceHistory>()
            .HasOne(ph => ph.Store)
            .WithMany()
            .HasForeignKey(ph => ph.StoreId);

        modelBuilder.Entity<StoreLocation>()
            .HasOne(sl => sl.Store)
            .WithMany(s => s.Locations)
            .HasForeignKey(sl => sl.StoreId);

        // Precision for monetary/quantities
        modelBuilder.Entity<ReceiptLine>().Property(p => p.Qty).HasPrecision(10, 3);
        modelBuilder.Entity<ReceiptLine>().Property(p => p.UnitPrice).HasPrecision(10, 3);
        modelBuilder.Entity<ReceiptLine>().Property(p => p.LineTotal).HasPrecision(10, 3);
        modelBuilder.Entity<ReceiptLine>().Property(p => p.VatRate).HasPrecision(5, 2);
        modelBuilder.Entity<ReceiptLine>().Property(p => p.PredictionConfidence).HasPrecision(3, 2);
        modelBuilder.Entity<Receipt>().Property(p => p.Total).HasPrecision(10, 3);
        modelBuilder.Entity<Receipt>().Property(p => p.TaxTotal).HasPrecision(10, 3);
        modelBuilder.Entity<PriceHistory>().Property(p => p.UnitPrice).HasPrecision(10, 3);
        modelBuilder.Entity<BudgetMonth>().Property(p => p.LimitAmount).HasPrecision(10, 2);
        modelBuilder.Entity<ExpenseAggregate>().Property(p => p.Amount).HasPrecision(12, 2);
        modelBuilder.Entity<LabelingEvent>().Property(p => p.Confidence).HasPrecision(3, 2);

        // ProductType Aliases as JSON (store as text for portability)
        modelBuilder.Entity<ProductType>().Property(pt => pt.AliasesJson).HasColumnType("text");
    }
}
