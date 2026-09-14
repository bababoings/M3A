using FluentValidation;
using M3A.Api.Dtos;
using M3A.Domain.ValueObjects;

namespace M3A.Api.Validators;

/// <summary>Validates <see cref="CreateEventDto"/>. Failures surface as HTTP 400 ProblemDetails.</summary>
public sealed class CreateEventDtoValidator : AbstractValidator<CreateEventDto>
{
    /// <summary>Configures the rule set.</summary>
    public CreateEventDtoValidator()
    {
        RuleFor(dto => dto.Name)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(dto => dto.DateTime)
            .Must(dt => dt > DateTimeOffset.UtcNow)
            .WithMessage("DateTime must be in the future.");

        RuleFor(dto => dto.EventCapacity)
            .GreaterThan(0);

        RuleFor(dto => dto.VenueId)
            .NotEmpty()
            .Must(ResourceId.IsValid)
            .WithMessage(dto => $"'{dto.VenueId}' is not a valid resource identifier.");
    }
}
