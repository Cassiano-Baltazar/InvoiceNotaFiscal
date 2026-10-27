using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Data;

/// <summary>
/// The app uses EnsureCreated (no migrations), which never changes an existing database.
/// This brings databases created by older versions up to date. Every step is idempotent.
/// </summary>
public static class SchemaUpgrader
{
    public static async Task UpgradeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await AddInvoiceItemsAsync(db, cancellationToken);
        await AddNfsesAsync(db, cancellationToken);
        await AddDigitalCertificatesAsync(db, cancellationToken);
    }

    // v4: digital certificate (.pfx) used to sign and send NFS-e.
    private static async Task AddDigitalCertificatesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "DigitalCertificates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DigitalCertificates" PRIMARY KEY,
                "FileName" TEXT NOT NULL,
                "PfxData" BLOB NOT NULL,
                "ProtectedPassword" TEXT NOT NULL,
                "Subject" TEXT NOT NULL,
                "Issuer" TEXT NOT NULL,
                "Thumbprint" TEXT NOT NULL,
                "ValidFrom" TEXT NOT NULL,
                "ValidUntil" TEXT NOT NULL,
                "UploadedAt" TEXT NOT NULL
            );
            """, cancellationToken);
    }

    // v3: NFS-e (Brazilian service invoices) linked to invoices.
    private static async Task AddNfsesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Nfses" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Nfses" PRIMARY KEY AUTOINCREMENT,
                "InvoiceId" INTEGER NOT NULL,
                "Status" INTEGER NOT NULL,
                "Source" INTEGER NOT NULL,
                "Number" TEXT NULL,
                "Series" TEXT NULL,
                "IssuedAt" TEXT NULL,
                "ServiceDate" TEXT NULL,
                "VerificationCode" TEXT NULL,
                "NationalAccessKey" TEXT NULL,
                "Link" TEXT NULL,
                "MunicipalStatusCode" INTEGER NULL,
                "MunicipalStatusDescription" TEXT NULL,
                "ProviderCnpj" TEXT NULL,
                "TakerName" TEXT NOT NULL,
                "TakerCountry" TEXT NULL,
                "TakerCountryCode" TEXT NULL,
                "Description" TEXT NOT NULL,
                "Notes" TEXT NULL,
                "ActivityCode" TEXT NULL,
                "ServiceListItem" TEXT NULL,
                "NbsCode" TEXT NULL,
                "TaxSituation" TEXT NULL,
                "IssRate" TEXT NOT NULL,
                "AmountBrl" TEXT NOT NULL,
                "Discount" TEXT NOT NULL,
                "IrAmount" TEXT NOT NULL,
                "InssAmount" TEXT NOT NULL,
                "CsllAmount" TEXT NOT NULL,
                "PisAmount" TEXT NOT NULL,
                "CofinsAmount" TEXT NOT NULL,
                "IssWithheldAmount" TEXT NOT NULL,
                "FileName" TEXT NULL,
                "XmlContent" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_Nfses_Invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES "Invoices" ("Id") ON DELETE RESTRICT
            );
            """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_Nfses_InvoiceId" ON "Nfses" ("InvoiceId");
            """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Nfses_Series_Number" ON "Nfses" ("Series", "Number");
            """, cancellationToken);
    }

    // v2: invoices can have several items. Older invoices get one item built from their
    // service description and subtotal, so they look exactly as before.
    private static async Task AddInvoiceItemsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "InvoiceItems" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_InvoiceItems" PRIMARY KEY AUTOINCREMENT,
                "InvoiceId" INTEGER NOT NULL,
                "Position" INTEGER NOT NULL,
                "Description" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL,
                "Unit" TEXT NOT NULL,
                "UnitPrice" TEXT NOT NULL,
                "Amount" TEXT NOT NULL,
                CONSTRAINT "FK_InvoiceItems_Invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES "Invoices" ("Id") ON DELETE CASCADE
            );
            """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_InvoiceItems_InvoiceId_Position" ON "InvoiceItems" ("InvoiceId", "Position");
            """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "InvoiceItems" ("InvoiceId", "Position", "Description", "Quantity", "Unit", "UnitPrice", "Amount")
            SELECT i."Id", 1, i."ServiceDescription", '1.0', 'month', i."Subtotal", i."Subtotal"
            FROM "Invoices" i
            WHERE NOT EXISTS (SELECT 1 FROM "InvoiceItems" x WHERE x."InvoiceId" = i."Id");
            """, cancellationToken);
    }
}
