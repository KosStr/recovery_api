using System.Text.Json;
using System.Text.Json.Serialization;
using RecoveryApp.Domain.Common;
using RecoveryApp.Domain.Enums;

namespace RecoveryApp.UnitTests;

/// <summary>
/// The database column, the JSON payload and the generated TypeScript client must all spell an enum
/// the same way. These tests fail loudly if a member is renamed without its wire name.
/// </summary>
public sealed class EnumWireNamesTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData(SessionType.Focus90, "focus_90")]
    [InlineData(SessionType.PowerNap, "power_nap")]
    [InlineData(SessionType.Nsdr, "nsdr")]
    [InlineData(SessionType.DigitalDetox, "digital_detox")]
    public void SessionTypeUsesTheDocumentedWireName(SessionType value, string expected)
    {
        Assert.Equal(expected, EnumWireNames<SessionType>.ToWireString(value));
    }

    [Theory]
    [InlineData(AudioCategory.Nsdr, "nsdr")]
    [InlineData(AudioCategory.Soundscape, "soundscape")]
    [InlineData(AudioCategory.Breathing, "breathing")]
    public void AudioCategoryUsesTheDocumentedWireName(AudioCategory value, string expected)
    {
        Assert.Equal(expected, EnumWireNames<AudioCategory>.ToWireString(value));
    }

    [Fact]
    public void PersistedNameRoundTrips()
    {
        foreach (SessionType value in Enum.GetValues<SessionType>())
        {
            Assert.Equal(value, EnumWireNames<SessionType>.Parse(EnumWireNames<SessionType>.ToWireString(value)));
        }
    }

    [Fact]
    public void JsonAndDatabaseSpellingsAgree()
    {
        foreach (SessionType value in Enum.GetValues<SessionType>())
        {
            string json = JsonSerializer.Serialize(value, Options).Trim('"');

            Assert.Equal(EnumWireNames<SessionType>.ToWireString(value), json);
        }
    }

    [Fact]
    public void UnknownValueIsRejected()
    {
        Assert.False(EnumWireNames<SessionType>.TryParse("meditation", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumWireNames<SessionType>.Parse("meditation"));
    }
}
