using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Infrastructure.Auth;
using RecoveryApp.Infrastructure.Persistence;

namespace RecoveryApp.Infrastructure;

/// <summary>Composition root for the infrastructure layer.</summary>
public static class DependencyInjection
{
    /// <summary>Name of the Postgres connection string in configuration.</summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>
    /// Registers the EF Core context, the token services and the server clock.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<DatabaseInitializer>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AppleAuthOptions>()
            .Bind(configuration.GetSection(AppleAuthOptions.SectionName))
            .Validate(
                options => options.UseStubVerification,
                "Signature-checked Apple identity token verification is not implemented yet. Either leave "
                + "AppleAuth:UseStubVerification set to true, or replace the IAppleIdentityTokenVerifier "
                + "registration with one that validates against https://appleid.apple.com/auth/keys.")
            .ValidateOnStart();

        services.TryAddSingletonTimeProvider();

        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAppleIdentityTokenVerifier, StubAppleIdentityTokenVerifier>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        // Handlers depend on TimeProvider rather than DateTimeOffset.UtcNow so tests can drive the
        // clock; tests substitute a FakeTimeProvider for this registration.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
