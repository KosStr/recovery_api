using System.Text.Json.Serialization;

namespace RecoveryApp.Domain.Enums;

/// <summary>The mutation recorded in the sync changelog for a single entity row.</summary>
public enum SyncOperation
{
    /// <summary>The row was created on the server for the first time.</summary>
    [JsonStringEnumMemberName("insert")]
    Insert = 1,

    /// <summary>An existing row was overwritten by a newer client revision.</summary>
    [JsonStringEnumMemberName("update")]
    Update = 2,

    /// <summary>The row was soft deleted (tombstoned) and must be removed on other devices.</summary>
    [JsonStringEnumMemberName("delete")]
    Delete = 3,
}
