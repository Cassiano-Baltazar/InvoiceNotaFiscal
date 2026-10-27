using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Services;

public interface IClientService
{
    Task<List<Client>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Client?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<Client> SaveAsync(Client client, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public class ClientService(AppDbContext db) : IClientService
{
    public Task<List<Client>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Clients.OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task<Client?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        await db.Clients.FindAsync([id], cancellationToken);

    public async Task<Client> SaveAsync(Client client, CancellationToken cancellationToken = default)
    {
        if (client.Id == 0)
        {
            client.CreatedAt = DateTime.UtcNow;
            db.Clients.Add(client);
        }
        else
        {
            db.Clients.Update(client);
        }

        await db.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var client = await db.Clients.FindAsync([id], cancellationToken);
        if (client is null)
            return;

        var hasInvoices = await db.Invoices.AnyAsync(x => x.ClientId == id, cancellationToken);
        if (hasInvoices)
        {
            client.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        db.Clients.Remove(client);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public interface ICompanySettingsService
{
    Task<CompanySettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CompanySettings settings, CancellationToken cancellationToken = default);
}

public class CompanySettingsService(AppDbContext db) : ICompanySettingsService
{
    public async Task<CompanySettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.CompanySettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
            return settings;

        settings = new CompanySettings();
        db.CompanySettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task SaveAsync(CompanySettings settings, CancellationToken cancellationToken = default)
    {
        settings.Id = 1;
        db.CompanySettings.Update(settings);
        await db.SaveChangesAsync(cancellationToken);
    }
}
