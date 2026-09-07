using System.Security.Claims;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;

namespace RecoveryApp.Api.Security;

/// <summary>Resolves the caller from the validated bearer token on the current request.</summary>
/// <param name="accessor">Accessor for the ambient <see cref="HttpContext"/>.</param>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            ClaimsPrincipal? principal = accessor.HttpContext?.User;

            string? value = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal?.FindFirstValue("sub");

            return Guid.TryParse(value, out Guid userId) ? userId : null;
        }
    }

    /// <inheritdoc />
    public Guid RequireUserId() =>
        UserId ?? throw new AuthenticationFailedException("The request is not associated with an authenticated user.");
}
