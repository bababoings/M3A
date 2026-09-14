using M3A.Api.Dtos;
using M3A.Domain.ValueObjects;
using FluentValidation;

namespace M3A.Api.Validators;

/// <summary>Validation rules for creating a ticket.</summary>
public sealed class CreateTicketDtoValidator : AbstractValidator<CreateTicketDto>
{
    public CreateTicketDtoValidator()
    {
        RuleFor(dto => dto.EventId)
            .NotEmpty()
            .Matches(ResourceId.Pattern)
            .WithMessage($"'{{PropertyValue}}' does not match the required format {ResourceId.Pattern}.");
    }
}
