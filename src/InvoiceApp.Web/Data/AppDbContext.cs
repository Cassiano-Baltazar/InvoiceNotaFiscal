using InvoiceApp.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanySettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LegalName).HasMaxLength(200);
            entity.Property(x => x.TradeName).HasMaxLength(100);
            entity.Property(x => x.OutputBasePath).HasMaxLength(500);
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            entity.Property(x => x.MonthlyAmount).HasPrecision(18, 2);
            entity.Property(x => x.WisePaymentLink).HasMaxLength(500);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.InvoiceNumber).IsUnique();
            entity.HasIndex(x => new { x.IssueYear, x.IssueMonth });
            entity.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.Tax).HasPrecision(18, 2);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.HasOne(x => x.Client)
                .WithMany(x => x.Invoices)
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(30);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.InvoiceId, x.Position });
            entity.HasOne(x => x.Invoice)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
