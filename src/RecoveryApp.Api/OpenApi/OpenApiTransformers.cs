using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace RecoveryApp.Api.OpenApi;

/// <summary>
/// Copies the C# XML documentation onto the generated schemas so the OpenAPI document — and any
/// TypeScript client generated from it — carries the same prose as the source.
/// </summary>
/// <param name="documentation">The loaded documentation index.</param>
public sealed class XmlDocumentationSchemaTransformer(XmlDocumentationStore documentation) : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        Type type = context.JsonTypeInfo.Type;

        schema.Description ??= documentation.GetTypeSummary(type);

        if (schema.Properties is { Count: > 0 })
        {
            foreach (JsonPropertyInfo property in context.JsonTypeInfo.Properties)
            {
                if (schema.Properties.TryGetValue(property.Name, out OpenApiSchema? propertySchema)
                    && propertySchema.Description is null)
                {
                    propertySchema.Description = documentation.GetMemberSummary(type, ClrName(property));
                }
            }
        }

        return Task.CompletedTask;
    }

    private static string ClrName(JsonPropertyInfo property) =>
        (property.AttributeProvider as System.Reflection.MemberInfo)?.Name ?? property.Name;
}

/// <summary>
/// Declares the bearer scheme once in <c>components</c> and attaches it to the operations that
/// actually require a token, so generated clients know which calls need credentials.
/// </summary>
public sealed class BearerSecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    /// <summary>The component name the security scheme is registered under.</summary>
    public const string SchemeName = "Bearer";

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Access token returned by POST /api/v1/auth/apple.",
        };

        return Task.CompletedTask;
    }
}

/// <summary>Marks authorized operations as requiring the bearer scheme.</summary>
public sealed class BearerSecurityOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (!RequiresAuthorization(context.Description.ActionDescriptor.EndpointMetadata))
        {
            return Task.CompletedTask;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = BearerSecurityDocumentTransformer.SchemeName,
                },
            }] = [],
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Mirrors how the authorization middleware resolves an endpoint: metadata is ordered from least
    /// to most specific, so whichever of <see cref="IAuthorizeData"/> and <see cref="IAllowAnonymous"/>
    /// appears last wins.
    /// </summary>
    /// <remarks>
    /// Asking merely "is there any <see cref="IAllowAnonymous"/>?" gets this wrong for an authorized
    /// endpoint inside an anonymous route group — the auth group is exactly that shape — and the
    /// document would then tell a generated client no token is needed on a call that returns 401.
    /// </remarks>
    private static bool RequiresAuthorization(IList<object> metadata)
    {
        int authorize = -1;
        int anonymous = -1;

        for (int i = 0; i < metadata.Count; i++)
        {
            switch (metadata[i])
            {
                case IAllowAnonymous:
                    anonymous = i;
                    break;
                case IAuthorizeData:
                    authorize = i;
                    break;
                default:
                    break;
            }
        }

        return authorize >= 0 && authorize > anonymous;
    }
}
