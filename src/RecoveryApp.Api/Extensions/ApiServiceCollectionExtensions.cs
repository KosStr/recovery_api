using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RecoveryApp.Api.Middleware;
using RecoveryApp.Api.OpenApi;
using RecoveryApp.Api.Security;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Infrastructure.Auth;
using RecoveryApp.Infrastructure.Persistence;

namespace RecoveryApp.Api.Extensions;

/// <summary>Wires up the HTTP layer: serialization, authentication, OpenAPI, health and errors.</summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>The name of the generated OpenAPI document.</summary>
    public const string OpenApiDocumentName = "v1";

    /// <summary>Registers every service the API layer owns.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.ConfigureHttpJsonOptions(ConfigureJson);
        services.Configure<JsonOptions>(ConfigureJson);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);

        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddAuthorization();
        services.AddApiAuthentication();
        services.AddApiOpenApi();

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"]);

        return services;
    }

    private static void ConfigureJson(JsonOptions options)
    {
        // Enum members carry [JsonStringEnumMemberName], so this emits "focus_90" rather than 1.
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }

    private static void AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                JwtOptions options = jwt.Value;

                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
    }

    private static void AddApiOpenApi(this IServiceCollection services)
    {
        XmlDocumentationStore documentation = XmlDocumentationStore.Load(
            Assembly.GetExecutingAssembly(),
            typeof(AppleSignInRequest).Assembly,
            typeof(Domain.Entities.User).Assembly);

        services.AddSingleton(documentation);

        services.AddOpenApi(OpenApiDocumentName, options =>
        {
            options.AddDocumentTransformer<BearerSecurityDocumentTransformer>();
            options.AddOperationTransformer<BearerSecurityOperationTransformer>();
            options.AddSchemaTransformer(new XmlDocumentationSchemaTransformer(documentation));

            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "RecoveryApp API";
                document.Info.Version = "1.0.0";
                document.Info.Description =
                    "Backend for the Digital Detox, Sleep and Recovery app. The client is local-first: it "
                    + "writes to its own store first and reconciles through /api/v1/sync.";

                return Task.CompletedTask;
            });
        });
    }
}
