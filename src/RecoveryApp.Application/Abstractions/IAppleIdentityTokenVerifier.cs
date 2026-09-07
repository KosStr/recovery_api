namespace RecoveryApp.Application.Abstractions;

/// <summary>Verifies the identity token issued by Sign in with Apple.</summary>
public interface IAppleIdentityTokenVerifier
{
    /// <summary>
    /// Validates the token signature, issuer, audience and expiry, and projects the claims the
    /// application needs.
    /// </summary>
    /// <param name="identityToken">The raw JWT produced by <c>ASAuthorizationAppleIDCredential</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verified principal.</returns>
    Task<AppleIdentity> VerifyAsync(string identityToken, CancellationToken cancellationToken = default);
}

/// <summary>The subset of Apple identity token claims the application consumes.</summary>
/// <param name="AppleUserId">The stable <c>sub</c> claim.</param>
/// <param name="Email">The <c>email</c> claim, released only on the first authorization.</param>
/// <param name="IsEmailVerified">Whether Apple asserts the email is verified.</param>
public readonly record struct AppleIdentity(string AppleUserId, string? Email, bool IsEmailVerified);
