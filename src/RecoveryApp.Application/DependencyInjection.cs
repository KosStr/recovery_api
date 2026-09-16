using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace RecoveryApp.Application;

/// <summary>Composition root for the application layer.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers every MediatR handler and FluentValidation validator declared in this assembly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Assembly assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: false);

        // Shared by every auth exchange; not a handler, so the MediatR scan does not pick it up.
        services.AddScoped<Features.Auth.AuthTokenIssuer>();

        return services;
    }
}
