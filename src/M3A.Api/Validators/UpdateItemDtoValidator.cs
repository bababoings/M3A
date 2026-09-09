using M3A.Api.Dtos;
using FluentValidation;

namespace M3A.Api.Validators;

/// <summary>Validates <see cref="UpdateItemDto"/>. Failures surface as HTTP 400 ProblemDetails.</summary>
public sealed class UpdateItemDtoValidator : AbstractValidator<UpdateItemDto>
{
    /// <summary>Configures the rule set.</summary>
    public UpdateItemDtoValidator()
    {
        RuleFor(dto => dto.Name)
            .NotEmpty()
            .MaximumLength(256);
    }
}
