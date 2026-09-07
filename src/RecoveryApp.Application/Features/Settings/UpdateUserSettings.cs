using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Application.Features.Settings;

/// <summary>Replaces the caller's sleep, caffeine and digital sunset configuration.</summary>
/// <param name="Request">The new configuration.</param>
public sealed record UpdateUserSettingsCommand(UpdateUserSettingsRequest Request) : IRequest<UserSettingsResponse>;

/// <summary>Reads the caller's current configuration.</summary>
public sealed record GetUserSettingsQuery : IRequest<UserSettingsResponse>;

/// <summary>Validates <see cref="UpdateUserSettingsRequest"/>.</summary>
public sealed class UpdateUserSettingsRequestValidator : AbstractValidator<UpdateUserSettingsRequest>
{
    /// <summary>Initializes a new instance of the <see cref="UpdateUserSettingsRequestValidator"/> class.</summary>
    public UpdateUserSettingsRequestValidator()
    {
        RuleFor(x => x.CaffeineCutoffHours).InclusiveBetween(0, 24);
        RuleFor(x => x.DigitalSunsetMinutes).InclusiveBetween(0, 480);
        RuleFor(x => x.SoundMixConfig).NotNull();

        When(x => x.SoundMixConfig is not null, () =>
        {
            RuleFor(x => x.SoundMixConfig.MasterVolume).InclusiveBetween(0d, 1d);
            RuleFor(x => x.SoundMixConfig.FadeOutSeconds).InclusiveBetween(0, 300);
            RuleFor(x => x.SoundMixConfig.Layers)
                .Must(l => l is null || l.Count <= 16)
                .WithMessage("A mix may not carry more than 16 layers.");

            RuleForEach(x => x.SoundMixConfig.Layers).ChildRules(layer =>
            {
                layer.RuleFor(l => l.SoundId).NotEmpty().MaximumLength(64);
                layer.RuleFor(l => l.Volume).InclusiveBetween(0d, 1d);
            });
        });
    }
}

/// <summary>Handles the settings read and write.</summary>
/// <param name="db">Persistence surface.</param>
/// <param name="currentUser">Caller identity.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class UserSettingsHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<UpdateUserSettingsCommand, UserSettingsResponse>,
      IRequestHandler<GetUserSettingsQuery, UserSettingsResponse>
{
    /// <inheritdoc />
    public async Task<UserSettingsResponse> Handle(UpdateUserSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        UserSettings? settings = await db.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is null)
        {
            // The row is created alongside the account, but an upsert keeps the endpoint usable if
            // an account predates the settings table or a backfill is still running.
            settings = new UserSettings { UserId = userId };
            db.UserSettings.Add(settings);
        }

        UpdateUserSettingsRequest request = command.Request;

        settings.TargetSleepTime = request.TargetSleepTime;
        settings.TargetWakeTime = request.TargetWakeTime;
        settings.CaffeineCutoffHours = request.CaffeineCutoffHours;
        settings.DigitalSunsetMinutes = request.DigitalSunsetMinutes;
        settings.HapticEnabled = request.HapticEnabled;
        settings.SoundMixConfig = request.SoundMixConfig;
        settings.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToResponse(settings);
    }

    /// <inheritdoc />
    public async Task<UserSettingsResponse> Handle(GetUserSettingsQuery query, CancellationToken cancellationToken)
    {
        Guid userId = currentUser.RequireUserId();

        UserSettings settings = await db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Common.NotFoundException(nameof(UserSettings), userId);

        return ToResponse(settings);
    }

    private static UserSettingsResponse ToResponse(UserSettings settings) => new(
        settings.UserId,
        settings.TargetSleepTime,
        settings.TargetWakeTime,
        settings.CaffeineCutoffHours,
        settings.DigitalSunsetMinutes,
        settings.HapticEnabled,
        settings.SoundMixConfig,
        settings.UpdatedAt);
}
