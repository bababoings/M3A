using M3A.Api.Dtos;
using FluentValidation;

namespace M3A.Api.Validators;

/// <summary>Validates <see cref="CreateVenueDto"/>. Failures surface as HTTP 400 ProblemDetails.</summary>
public sealed class CreateVenueDtoValidator : AbstractValidator<CreateVenueDto>
{
    /// <summary>Configures the rule set.</summary>
    public CreateVenueDtoValidator()
    {
        RuleFor(dto => dto.Location)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(dto => dto.Capacity)
            .GreaterThan(0);
    }
}
