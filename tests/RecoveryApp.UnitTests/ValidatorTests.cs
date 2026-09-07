using System.Text.Json;
using FluentValidation.Results;
using RecoveryApp.Application.Common;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Application.Features.Settings;
using RecoveryApp.Application.Features.Sync;
using RecoveryApp.Domain.Common;
using RecoveryApp.Domain.Enums;

namespace RecoveryApp.UnitTests;

/// <summary>Covers the request rules that protect the sync and settings endpoints.</summary>
public sealed class ValidatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly SyncPushRequestValidator _push = new();
    private readonly UpdateUserSettingsRequestValidator _settings = new();

    [Fact]
    public void EmptyPushIsValid()
    {
        Assert.True(_push.Validate(new SyncPushRequest(null, null)).IsValid);
    }

    [Fact]
    public void WellFormedPushIsValid()
    {
        SyncPushRequest request = new([Session()], [Checkin()]);

        ValidationResult result = _push.Validate(request);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void ScoreOutsideOneToFiveIsRejected(int score)
    {
        SyncPushRequest request = new(null, [Checkin() with { Score = score }]);

        Assert.False(_push.Validate(request).IsValid);
    }

    [Fact]
    public void SessionEndingBeforeItStartedIsRejected()
    {
        SyncPushRequest request = new([Session() with { EndedAt = Noon.AddMinutes(-5) }], null);

        Assert.False(_push.Validate(request).IsValid);
    }

    [Fact]
    public void BatchLargerThanTheProtocolLimitIsRejected()
    {
        EnergyCheckinDto[] oversized = [.. Enumerable
            .Range(0, SyncLimits.MaxPushBatchSize + 1)
            .Select(_ => Checkin() with { Id = Guid.CreateVersion7() })];

        Assert.False(_push.Validate(new SyncPushRequest(null, oversized)).IsValid);
    }

    [Fact]
    public void RowWithoutARevisionStampIsRejected()
    {
        // Without updatedAt the merge policy has nothing to compare, so the row must not be accepted.
        SyncPushRequest request = new([Session() with { UpdatedAt = default }], null);

        Assert.False(_push.Validate(request).IsValid);
    }

    [Fact]
    public void DefaultSettingsAreValid()
    {
        Assert.True(_settings.Validate(Settings()).IsValid);
    }

    [Theory]
    [InlineData(25, 60)]
    [InlineData(8, 481)]
    [InlineData(-1, 60)]
    public void SettingsOutsideTheAllowedRangeAreRejected(int caffeineCutoffHours, int digitalSunsetMinutes)
    {
        UpdateUserSettingsRequest request = Settings() with
        {
            CaffeineCutoffHours = caffeineCutoffHours,
            DigitalSunsetMinutes = digitalSunsetMinutes,
        };

        Assert.False(_settings.Validate(request).IsValid);
    }

    [Fact]
    public void MixLayerVolumeAboveOneIsRejected()
    {
        UpdateUserSettingsRequest request = Settings() with
        {
            SoundMixConfig = new SoundMixConfig
            {
                Layers = [new SoundLayer { SoundId = "rain_heavy", Volume = 1.4 }],
            },
        };

        Assert.False(_settings.Validate(request).IsValid);
    }

    private static UserSessionDto Session() => new(
        Guid.CreateVersion7(),
        SessionType.Focus90,
        Noon,
        Noon.AddMinutes(90),
        TargetDurationSeconds: 5_400,
        ActualDurationSeconds: 5_400,
        CompletedSuccessfully: true,
        InterruptionsCount: 0,
        Metadata: new Dictionary<string, JsonElement>(),
        ClientCreatedAt: Noon,
        UpdatedAt: Noon.AddMinutes(91),
        DeletedAt: null);

    private static EnergyCheckinDto Checkin() => new(
        Guid.CreateVersion7(),
        Score: 4,
        Context: "after a walk",
        Tags: ["outdoors", "post_nap"],
        RecordedAt: Noon,
        UpdatedAt: Noon,
        DeletedAt: null);

    private static UpdateUserSettingsRequest Settings() => new(
        new TimeOnly(22, 30),
        new TimeOnly(6, 30),
        CaffeineCutoffHours: 8,
        DigitalSunsetMinutes: 60,
        HapticEnabled: true,
        SoundMixConfig: new SoundMixConfig());
}
