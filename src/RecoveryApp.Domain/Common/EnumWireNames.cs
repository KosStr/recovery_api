using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json.Serialization;

namespace RecoveryApp.Domain.Common;

/// <summary>
/// Resolves the stable, over-the-wire string for an enum member from its
/// <see cref="JsonStringEnumMemberNameAttribute"/> attribute, so that the JSON payload, the database column
/// and any generated TypeScript client all agree on a single spelling (for example
/// <c>focus_90</c> rather than <c>Focus90</c>).
/// </summary>
/// <typeparam name="TEnum">The enum being mapped.</typeparam>
public static class EnumWireNames<TEnum>
    where TEnum : struct, Enum
{
    private static readonly FrozenDictionary<TEnum, string> ToWire = Enum.GetValues<TEnum>()
        .ToFrozenDictionary(value => value, ReadWireName);

    private static readonly FrozenDictionary<string, TEnum> FromWire =
        ToWire.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>All wire values for <typeparamref name="TEnum"/>, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Enum.GetValues<TEnum>().Select(ToWireString)];

    /// <summary>Returns the wire spelling for <paramref name="value"/>.</summary>
    /// <param name="value">The enum member.</param>
    /// <returns>The wire spelling, for example <c>power_nap</c>.</returns>
    public static string ToWireString(TEnum value) =>
        ToWire.TryGetValue(value, out string? wire)
            ? wire
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unknown {typeof(TEnum).Name} member.");

    /// <summary>Parses a wire spelling back into an enum member.</summary>
    /// <param name="wire">The wire spelling, case insensitive.</param>
    /// <returns>The matching enum member.</returns>
    public static TEnum Parse(string wire) =>
        FromWire.TryGetValue(wire, out TEnum value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {typeof(TEnum).Name} value.");

    /// <summary>Attempts to parse a wire spelling back into an enum member.</summary>
    /// <param name="wire">The wire spelling, case insensitive.</param>
    /// <param name="value">The matching enum member when parsing succeeds.</param>
    /// <returns><see langword="true"/> when <paramref name="wire"/> is a known value.</returns>
    public static bool TryParse(string? wire, out TEnum value)
    {
        if (wire is not null)
        {
            return FromWire.TryGetValue(wire, out value);
        }

        value = default;
        return false;
    }

    private static string ReadWireName(TEnum value) =>
        typeof(TEnum).GetField(value.ToString())
            ?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()
            ?.Name
        ?? value.ToString().ToLowerInvariant();
}
