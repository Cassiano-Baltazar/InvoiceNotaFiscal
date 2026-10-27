using InvoiceApp.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Nfse> Nfses => Set<Nfse>();
    public DbSet<DigitalCertificate> DigitalCertificates => Set<DigitalCertificate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanySettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LegalName).HasMaxLength(200);
            entity.Property(x => x.TradeName).HasMaxLength(100);
            entity.Property(x => x.OutputBasePath).HasMaxLength(500);
        });

        modelBuilder.Entity<DigitalCertificate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.PfxData).IsRequired();
            entity.Property(x => x.ProtectedPassword).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Issuer).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Thumbprint).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            entity.Property(x => x.MonthlyAmount).HasPrecision(18, 2);
            entity.Property(x => x.WisePaymentLink).HasMaxLength(500);
            entity.Property(x => x.CountryCode).HasMaxLength(2);
            entity.Property(x => x.TaxId).HasMaxLength(40);
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

        modelBuilder.Entity<Nfse>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(20);
            entity.Property(x => x.Series).HasMaxLength(10);
            entity.Property(x => x.TakerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.IssRate).HasPrecision(9, 4);
            entity.Property(x => x.AmountBrl).HasPrecision(18, 2);
            entity.Property(x => x.Discount).HasPrecision(18, 2);
            entity.Property(x => x.IrAmount).HasPrecision(18, 2);
            entity.Property(x => x.InssAmount).HasPrecision(18, 2);
            entity.Property(x => x.CsllAmount).HasPrecision(18, 2);
            entity.Property(x => x.PisAmount).HasPrecision(18, 2);
            entity.Property(x => x.CofinsAmount).HasPrecision(18, 2);
            entity.Property(x => x.IssWithheldAmount).HasPrecision(18, 2);
            entity.Property(x => x.TakerTaxId).HasMaxLength(40);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3);
            entity.Property(x => x.AmountForeign).HasPrecision(18, 2);
            entity.Property(x => x.ResultCountryCode).HasMaxLength(2);
            entity.Ignore(x => x.TotalWithheld);
            entity.HasIndex(x => x.InvoiceId);
            entity.HasIndex(x => new { x.Series, x.Number }).IsUnique();
            entity.HasOne(x => x.Invoice)
                .WithMany(x => x.Nfses)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
