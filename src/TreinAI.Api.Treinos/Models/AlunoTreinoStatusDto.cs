namespace TreinAI.Api.Treinos.Models;

/// <summary>
/// DTO returned by GET /treinos/professor/alunos.
/// Represents a student with their current training plan status.
/// </summary>
public class AlunoTreinoStatusDto
{
    public string AlunoId { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Objetivo { get; set; }
    public string? FotoUrl { get; set; }
    public bool Ativo { get; set; }

    /// <summary>
    /// "ativo" | "expirado" | "sem_treino"
    /// </summary>
    public string StatusTreino { get; set; } = "sem_treino";

    /// <summary>
    /// Name of the active or most recent training plan (if any).
    /// </summary>
    public string? TreinoNome { get; set; }

    /// <summary>
    /// Id of the active or most recent training plan (if any).
    /// </summary>
    public string? TreinoId { get; set; }

    /// <summary>
    /// Start date of the active or most recent training plan.
    /// </summary>
    public DateTime? TreinoDataInicio { get; set; }

    /// <summary>
    /// End date of the active or most recent training plan.
    /// </summary>
    public DateTime? TreinoDataFim { get; set; }
}
