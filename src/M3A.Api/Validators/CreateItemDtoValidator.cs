using M3A.Api.Dtos;
using FluentValidation;

namespace M3A.Api.Validators;

/// <summary>Validates <see cref="CreateItemDto"/>. Failures surface as HTTP 400 ProblemDetails.</summary>
public sealed class CreateItemDtoValidator : AbstractValidator<CreateItemDto>
{
    /// <summary>Configures the rule set.</summary>
    public CreateItemDtoValidator()
    {
        RuleFor(dto => dto.Name)
            .NotEmpty()
            .MaximumLength(256);
    }
}
