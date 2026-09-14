using FluentValidation;
using M3A.Api.Dtos;
using M3A.Domain.ValueObjects;

namespace M3A.Api.Validators;

/// <summary>Validates <see cref="UpdateEventDto"/>. Failures surface as HTTP 400 ProblemDetails.</summary>
public sealed class UpdateEventDtoValidator : AbstractValidator<UpdateEventDto>
{
    /// <summary>Configures the rule set.</summary>
    public UpdateEventDtoValidator()
    {
        RuleFor(dto => dto.Name)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(dto => dto.EventCapacity)
            .GreaterThan(0);

        RuleFor(dto => dto.VenueId)
            .NotEmpty()
            .Must(ResourceId.IsValid)
            .WithMessage(dto => $"'{dto.VenueId}' is not a valid resource identifier.");
    }
}
