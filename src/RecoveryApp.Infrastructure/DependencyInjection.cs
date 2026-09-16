using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Infrastructure.Auth;
using RecoveryApp.Infrastructure.Persistence;

namespace RecoveryApp.Infrastructure;

/// <summary>Composition root for the infrastructure layer.</summary>
public static class DependencyInjection
{
    /// <summary>Name of the Postgres connection string in configuration.</summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>Named <see cref="HttpClient"/> used to fetch Apple's OIDC metadata and signing keys.</summary>
    public const string AppleMetadataClientName = "apple-oidc";

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
            .ValidateOnStart();

        services.TryAddSingletonTimeProvider();

        services.AddSingleton<ITokenService, TokenService>();
        services.AddAppleVerification(configuration);

        return services;
    }

    /// <summary>
    /// Registers the Apple identity token verifier. Real JWKS verification is the default; the stub
    /// has to be asked for explicitly, so it cannot reach an environment by being forgotten.
    /// </summary>
    private static void AddAppleVerification(this IServiceCollection services, IConfiguration configuration)
    {
        bool useStub = configuration
            .GetSection(AppleAuthOptions.SectionName)
            .GetValue(nameof(AppleAuthOptions.UseStubVerification), defaultValue: false);

        if (useStub)
        {
            services.AddScoped<IAppleIdentityTokenVerifier, StubAppleIdentityTokenVerifier>();
            return;
        }

        // A named client so Apple's metadata fetch gets its own handler lifetime and timeout rather
        // than sharing whatever the rest of the app is doing.
        services.AddHttpClient(AppleMetadataClientName, client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(provider =>
        {
            AppleAuthOptions options = provider
                .GetRequiredService<IOptions<AppleAuthOptions>>()
                .Value;

            HttpClient http = provider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(AppleMetadataClientName);

            return new ConfigurationManager<OpenIdConnectConfiguration>(
                options.MetadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(http) { RequireHttps = true })
            {
                AutomaticRefreshInterval = options.KeyCacheDuration,
            };
        });

        services.AddScoped<IAppleIdentityTokenVerifier, AppleJwksIdentityTokenVerifier>();
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
