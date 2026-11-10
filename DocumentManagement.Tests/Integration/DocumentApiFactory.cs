using DocumentManagement.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentManagement.Tests.Integration;

public sealed class DocumentApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _databaseConnection = new("Data Source=:memory:");

    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), $"DocumentManagementTests-{Guid.NewGuid():N}");

    public async Task InitializeDatabaseAsync()
    {
        await _databaseConnection.OpenAsync();

        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.Database.EnsureCreatedAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["FileStorage:RootPath"] = StoragePath,
                ["FileStorage:MaxFileSizeBytes"] = (10 * 1024 * 1024).ToString(),
                ["ConnectionStrings:DefaultConnection"] = "Server=TEST_DATABASE_MUST_NOT_BE_USED;Database=Forbidden;Trusted_Connection=True;"
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_databaseConnection));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _databaseConnection.DisposeAsync();
        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }
}
