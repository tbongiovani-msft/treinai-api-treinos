using FluentValidation;
using TreinAI.Shared.Models;

namespace TreinAI.Api.Treinos.Validators;

public class ExercicioValidator : AbstractValidator<Exercicio>
{
    public ExercicioValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("Nome do exercício é obrigatório.")
            .MaximumLength(200).WithMessage("Nome deve ter no máximo 200 caracteres.");

        RuleFor(x => x.GrupoMuscular)
            .NotEmpty().WithMessage("Grupo muscular é obrigatório.")
            .MaximumLength(100).WithMessage("Grupo muscular deve ter no máximo 100 caracteres.");

        RuleFor(x => x.Descricao)
            .MaximumLength(1000).WithMessage("Descrição deve ter no máximo 1000 caracteres.")
            .When(x => !string.IsNullOrEmpty(x.Descricao));

        RuleFor(x => x.LinkVideo)
            .Must(BeAValidUrl).WithMessage("Link do vídeo deve ser uma URL válida.")
            .When(x => !string.IsNullOrEmpty(x.LinkVideo));
    }

    private static bool BeAValidUrl(string? url)
    {
        return string.IsNullOrEmpty(url) || Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}
