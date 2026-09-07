namespace RecoveryApp.Application.Common;

/// <summary>Raised when credential exchange fails. Maps to HTTP 401.</summary>
/// <param name="message">Reason safe to surface to the caller.</param>
public sealed class AuthenticationFailedException(string message) : Exception(message);
