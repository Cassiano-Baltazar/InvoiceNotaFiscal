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
        await AddForeignTradeColumnsAsync(db, cancellationToken);
    }

    // v5: data the NFS-e needs for a foreign taker (tax id, country code) and for foreign trade
    // (currency, amount in that currency, how the service is provided).
    private static async Task AddForeignTradeColumnsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await AddColumnAsync(db, "Clients", "CountryCode", "TEXT NULL", cancellationToken);
        await AddColumnAsync(db, "Clients", "TaxId", "TEXT NULL", cancellationToken);
        await AddColumnAsync(db, "Clients", "MissingTaxIdReason", "INTEGER NULL", cancellationToken);

        await AddColumnAsync(db, "Nfses", "TakerTaxId", "TEXT NULL", cancellationToken);
        await AddColumnAsync(db, "Nfses", "TakerMissingTaxIdReason", "INTEGER NULL", cancellationToken);
        await AddColumnAsync(db, "Nfses", "CurrencyCode", "TEXT NULL", cancellationToken);
        await AddColumnAsync(db, "Nfses", "AmountForeign", "TEXT NOT NULL DEFAULT '0.0'", cancellationToken);
        await AddColumnAsync(db, "Nfses", "ServiceMode", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await AddColumnAsync(db, "Nfses", "RelatedToTaker", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnAsync(db, "Nfses", "ResultCountryCode", "TEXT NULL", cancellationToken);

        // Notes created before this version: the foreign amount and currency are the invoice's.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "Nfses"
            SET "CurrencyCode" = (SELECT i."Currency" FROM "Invoices" i WHERE i."Id" = "Nfses"."InvoiceId"),
                "AmountForeign" = (SELECT i."Total" FROM "Invoices" i WHERE i."Id" = "Nfses"."InvoiceId")
            WHERE "CurrencyCode" IS NULL;
            """, cancellationToken);
    }

    private static async Task AddColumnAsync(AppDbContext db, string table, string column, string definition, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0)
                return;
        }

        var sql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
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
