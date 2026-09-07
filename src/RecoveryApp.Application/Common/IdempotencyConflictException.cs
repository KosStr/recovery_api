namespace RecoveryApp.Application.Common;

/// <summary>
/// Raised when an <c>Idempotency-Key</c> is replayed with a different payload or against a
/// different endpoint. Maps to HTTP 409.
/// </summary>
/// <param name="message">Reason safe to surface to the caller.</param>
public sealed class IdempotencyConflictException(string message) : Exception(message);
