using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RecoveryApp.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the API host. The connection string is
/// only used to pick the provider and to run <c>database update</c>; scaffolding a migration does
/// not open a connection.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string ConnectionStringVariable = "ConnectionStrings__Postgres";

    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=recoveryapp;Username=recoveryapp;Password=recoveryapp";

    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? DefaultConnectionString;

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
