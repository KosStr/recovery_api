namespace RecoveryApp.Application.Abstractions;

/// <summary>Ambient identity of the caller, resolved from the bearer token.</summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user id, or <see langword="null"/> for anonymous callers.</summary>
    Guid? UserId { get; }

    /// <summary>The authenticated user id, throwing when the caller is anonymous.</summary>
    /// <returns>The authenticated user id.</returns>
    Guid RequireUserId();
}
