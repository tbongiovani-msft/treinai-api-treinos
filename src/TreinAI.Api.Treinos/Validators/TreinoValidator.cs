using FluentValidation;
using TreinAI.Shared.Models;

namespace TreinAI.Api.Treinos.Validators;

public class TreinoValidator : AbstractValidator<Treino>
{
    public TreinoValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("Nome do treino é obrigatório.")
            .MaximumLength(200).WithMessage("Nome deve ter no máximo 200 caracteres.");

        RuleFor(x => x.AlunoId)
            .NotEmpty().WithMessage("AlunoId é obrigatório.");

        RuleFor(x => x.Descricao)
            .MaximumLength(1000).WithMessage("Descrição deve ter no máximo 1000 caracteres.")
            .When(x => !string.IsNullOrEmpty(x.Descricao));

        RuleFor(x => x.DataInicio)
            .NotEmpty().WithMessage("Data de início é obrigatória.");

        RuleFor(x => x.DataFim)
            .GreaterThan(x => x.DataInicio).WithMessage("Data de fim deve ser posterior à data de início.")
            .When(x => x.DataFim.HasValue);

        RuleForEach(x => x.Divisoes).SetValidator(new DivisaoTreinoValidator());
    }
}

public class DivisaoTreinoValidator : AbstractValidator<DivisaoTreino>
{
    public DivisaoTreinoValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("Nome da divisão é obrigatório.")
            .MaximumLength(100).WithMessage("Nome da divisão deve ter no máximo 100 caracteres.");

        RuleFor(x => x.Descricao)
            .MaximumLength(500).WithMessage("Descrição da divisão deve ter no máximo 500 caracteres.")
            .When(x => !string.IsNullOrEmpty(x.Descricao));

        RuleForEach(x => x.Exercicios).SetValidator(new ExercicioTreinoValidator());
    }
}

public class ExercicioTreinoValidator : AbstractValidator<ExercicioTreino>
{
    public ExercicioTreinoValidator()
    {
        RuleFor(x => x.ExercicioId)
            .NotEmpty().WithMessage("ExercicioId é obrigatório.");

        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("Nome do exercício é obrigatório.");

        RuleFor(x => x.Series)
            .GreaterThan(0).WithMessage("Séries deve ser maior que zero.")
            .When(x => x.Series > 0);

        RuleFor(x => x.Repeticoes)
            .MaximumLength(50).WithMessage("Repetições deve ter no máximo 50 caracteres.")
            .When(x => !string.IsNullOrEmpty(x.Repeticoes));
    }
}
