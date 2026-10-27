using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Tests.Services;

/// <summary>Databases created before invoice items existed must keep working after the upgrade.</summary>
public class SchemaUpgraderTests : IDisposable
{
    private readonly TestDatabase database = new();

    // Simulates a database from the previous version: same schema, minus the items table.
    private void MakeLegacyDatabaseWithOneInvoice()
    {
        database.SeedCompany();
        var client = database.SeedClient(amount: 4000m);
        var invoice = database.SeedInvoice(client.Id);
        Execute("DROP TABLE \"InvoiceItems\";");
        Execute($"UPDATE \"Invoices\" SET \"ServiceDescription\" = 'Old description' WHERE \"Id\" = {invoice.Id};");
    }

    private void Execute(string sql)
    {
        using var command = database.Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private List<(string Name, string Type, bool NotNull, bool Pk)> Columns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = command.ExecuteReader();
        var columns = new List<(string, string, bool, bool)>();
        while (reader.Read())
            columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1, reader.GetInt32(5) > 0));
        return columns;
    }

    [Fact]
    public async Task Creates_items_table_and_one_item_per_old_invoice()
    {
        MakeLegacyDatabaseWithOneInvoice();

        using (var db = database.CreateContext())
            await SchemaUpgrader.UpgradeAsync(db);

        using var check = database.CreateContext();
        var invoice = await check.Invoices.Include(x => x.Items).SingleAsync();
        var item = Assert.Single(invoice.Items);
        Assert.Equal(1, item.Position);
        Assert.Equal("Old description", item.Description);
        Assert.Equal(1m, item.Quantity);
        Assert.Equal("month", item.Unit);
        Assert.Equal(4000m, item.UnitPrice);
        Assert.Equal(4000m, item.Amount);
    }

    [Fact]
    public async Task Running_twice_does_not_duplicate_items()
    {
        MakeLegacyDatabaseWithOneInvoice();

        using (var db = database.CreateContext())
        {
            await SchemaUpgrader.UpgradeAsync(db);
            await SchemaUpgrader.UpgradeAsync(db);
        }

        using var check = database.CreateContext();
        Assert.Single(check.InvoiceItems);
    }

    [Fact]
    public async Task Upgraded_table_matches_the_table_the_model_creates()
    {
        MakeLegacyDatabaseWithOneInvoice();
        using (var db = database.CreateContext())
            await SchemaUpgrader.UpgradeAsync(db);

        using var fresh = new TestDatabase();

        Assert.Equal(Columns(fresh.Connection, "InvoiceItems"), Columns(database.Connection, "InvoiceItems"));
    }

    [Fact]
    public async Task Upgraded_database_supports_cascade_delete()
    {
        MakeLegacyDatabaseWithOneInvoice();
        using (var db = database.CreateContext())
            await SchemaUpgrader.UpgradeAsync(db);

        using var context = database.CreateContext();
        var invoice = await context.Invoices.SingleAsync();
        context.Invoices.Remove(invoice);
        await context.SaveChangesAsync();

        using var check = database.CreateContext();
        Assert.Empty(check.InvoiceItems);
    }

    [Fact]
    public async Task Does_nothing_on_a_new_database()
    {
        database.SeedCompany();
        var client = database.SeedClient();
        database.SeedInvoice(client.Id);

        using (var db = database.CreateContext())
            await SchemaUpgrader.UpgradeAsync(db);

        using var check = database.CreateContext();
        Assert.Single(check.InvoiceItems);
    }

    // ---------- NFS-e table ----------

    [Fact]
    public async Task Creates_nfse_table_matching_the_model_on_a_database_without_it()
    {
        MakeLegacyDatabaseWithOneInvoice();
        Execute("DROP TABLE \"Nfses\";");

        using (var db = database.CreateContext())
            await SchemaUpgrader.UpgradeAsync(db);

        using var fresh = new TestDatabase();
        Assert.Equal(Columns(fresh.Connection, "Nfses"), Columns(database.Connection, "Nfses"));
        Assert.Equal(Indexes(fresh.Connection, "Nfses"), Indexes(database.Connection, "Nfses"));
    }

    [Fact]
    public async Task Upgraded_nfse_table_is_usable_and_blocks_deleting_the_invoice()
    {
        MakeLegacyDatabaseWithOneInvoice();
        Execute("DROP TABLE \"Nfses\";");
        using (var db = database.CreateContext())
            await SchemaUpgrader.UpgradeAsync(db);

        using var context = database.CreateContext();
        var invoice = await context.Invoices.SingleAsync();
        context.Nfses.Add(new Nfse { InvoiceId = invoice.Id, TakerName = "X", Description = "Y", AmountBrl = 10.5m });
        await context.SaveChangesAsync();

        Assert.Throws<SqliteException>(() => Execute($"DELETE FROM \"Invoices\" WHERE \"Id\" = {invoice.Id};"));
        using var check = database.CreateContext();
        Assert.Equal(10.5m, (await check.Nfses.SingleAsync()).AmountBrl);
    }

    // ---------- Digital certificate table ----------

    [Fact]
    public async Task Creates_certificate_table_matching_the_model_on_a_database_without_it()
    {
        MakeLegacyDatabaseWithOneInvoice();
        Execute("DROP TABLE \"DigitalCertificates\";");

        using (var db = database.CreateContext())
            await SchemaUpgrader.UpgradeAsync(db);

        using var fresh = new TestDatabase();
        Assert.Equal(Columns(fresh.Connection, "DigitalCertificates"), Columns(database.Connection, "DigitalCertificates"));
    }

    [Fact]
    public async Task Upgraded_certificate_table_is_usable()
    {
        MakeLegacyDatabaseWithOneInvoice();
        Execute("DROP TABLE \"DigitalCertificates\";");
        using (var db = database.CreateContext())
            await SchemaUpgrader.UpgradeAsync(db);

        using (var context = database.CreateContext())
        {
            context.DigitalCertificates.Add(new DigitalCertificate { FileName = "a.pfx", PfxData = [1, 2, 3], ProtectedPassword = "x", Subject = "S", Issuer = "I", Thumbprint = "T" });
            await context.SaveChangesAsync();
        }

        using var check = database.CreateContext();
        Assert.Equal([1, 2, 3], (await check.DigitalCertificates.SingleAsync()).PfxData);
    }

    private static List<(string Name, bool Unique)> Indexes(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list(\"{table}\");";
        using var reader = command.ExecuteReader();
        var indexes = new List<(string, bool)>();
        while (reader.Read())
            if (!reader.GetString(1).StartsWith("sqlite_"))
                indexes.Add((reader.GetString(1), reader.GetInt32(2) == 1));
        return indexes.OrderBy(x => x.Item1).ToList();
    }

    public void Dispose() => database.Dispose();
}
