using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RecoveryApp.Domain.Common;

namespace RecoveryApp.Infrastructure.Persistence.Converters;

/// <summary>
/// Persists an enum using the same wire spelling the JSON API uses (for example <c>focus_90</c>),
/// so a row can be read straight out of psql without a lookup table and a renumbered enum member
/// cannot silently change the meaning of existing data.
/// </summary>
/// <typeparam name="TEnum">The enum being persisted.</typeparam>
public sealed class EnumWireConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    /// <summary>Initializes a new instance of the <see cref="EnumWireConverter{TEnum}"/> class.</summary>
    public EnumWireConverter()
        : base(
            value => EnumWireNames<TEnum>.ToWireString(value),
            wire => EnumWireNames<TEnum>.Parse(wire))
    {
    }
}
