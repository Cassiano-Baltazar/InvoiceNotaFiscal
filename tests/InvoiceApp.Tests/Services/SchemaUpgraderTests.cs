using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data;
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

    public void Dispose() => database.Dispose();
}
