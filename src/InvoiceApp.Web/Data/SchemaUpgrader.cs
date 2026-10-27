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
