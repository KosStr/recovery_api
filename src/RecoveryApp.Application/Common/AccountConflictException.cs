namespace RecoveryApp.Application.Common;

/// <summary>
/// Raised when an identity operation cannot be satisfied without destroying or reassigning data that
/// already belongs to someone. Maps to HTTP 409.
/// </summary>
/// <param name="message">Reason safe to surface to the caller.</param>
public sealed class AccountConflictException(string message) : Exception(message);
