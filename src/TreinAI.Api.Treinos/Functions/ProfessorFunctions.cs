using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TreinAI.Api.Treinos.Models;
using TreinAI.Shared.Exceptions;
using TreinAI.Shared.Middleware;
using TreinAI.Shared.Models;
using TreinAI.Shared.Repositories;
using TreinAI.Shared.Validation;

namespace TreinAI.Api.Treinos.Functions;

/// <summary>
/// Professor-centric endpoints for managing students and their training status.
/// </summary>
public class ProfessorFunctions
{
    private readonly IRepository<Treino> _treinoRepository;
    private readonly IRepository<Aluno> _alunoRepository;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<ProfessorFunctions> _logger;

    public ProfessorFunctions(
        IRepository<Treino> treinoRepository,
        IRepository<Aluno> alunoRepository,
        TenantContext tenantContext,
        ILogger<ProfessorFunctions> logger)
    {
        _treinoRepository = treinoRepository;
        _alunoRepository = alunoRepository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/treinos/professor/alunos — Lists all students of the professor with training status.
    /// Status: "ativo" (has active plan), "expirado" (all plans expired), "sem_treino" (no plans).
    /// </summary>
    [Function("GetAlunosProfessor")]
    public async Task<HttpResponseData> GetAlunosProfessor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "treinos/professor/alunos")] HttpRequestData req)
    {
        if (_tenantContext.IsAluno)
            throw new ForbiddenException("Alunos não podem acessar esta funcionalidade.");

        var professorId = _tenantContext.UserId;

        _logger.LogInformation(
            "Getting alunos with treino status for professor {ProfessorId}, tenant {TenantId}",
            professorId, _tenantContext.TenantId);

        // 1. Get all students linked to this professor
        var alunos = await _alunoRepository.QueryAsync(
            _tenantContext.TenantId,
            a => a.ProfessorId == professorId && !a.IsDeleted);

        if (alunos.Count == 0)
            return await ValidationHelper.OkAsync(req, Array.Empty<AlunoTreinoStatusDto>());

        // 2. Get all treinos created by this professor (single query, then group in memory)
        var treinos = await _treinoRepository.QueryAsync(
            _tenantContext.TenantId,
            t => t.CreatedBy == professorId && !t.IsDeleted);

        // Group treinos by alunoId for fast lookup
        var treinosPorAluno = treinos
            .GroupBy(t => t.AlunoId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var now = DateTime.UtcNow;

        // 3. Build response with status for each aluno
        var result = alunos.Select(aluno =>
        {
            var dto = new AlunoTreinoStatusDto
            {
                AlunoId = aluno.Id,
                Nome = aluno.Nome,
                Email = aluno.Email,
                Telefone = aluno.Telefone,
                Objetivo = aluno.Objetivo,
                FotoUrl = aluno.FotoUrl,
                Ativo = aluno.Ativo
            };

            if (treinosPorAluno.TryGetValue(aluno.Id, out var alunoTreinos) && alunoTreinos.Count > 0)
            {
                // Check for an active plan: Ativo == true, started, not yet expired
                var treinoAtivo = alunoTreinos.FirstOrDefault(t =>
                    t.Ativo && t.DataInicio <= now && (!t.DataFim.HasValue || t.DataFim.Value >= now));

                if (treinoAtivo != null)
                {
                    dto.StatusTreino = "ativo";
                    dto.TreinoId = treinoAtivo.Id;
                    dto.TreinoNome = treinoAtivo.Nome;
                    dto.TreinoDataInicio = treinoAtivo.DataInicio;
                    dto.TreinoDataFim = treinoAtivo.DataFim;
                }
                else
                {
                    // No active plan → find the most recent one
                    var maisRecente = alunoTreinos.OrderByDescending(t => t.DataInicio).First();
                    dto.StatusTreino = "expirado";
                    dto.TreinoId = maisRecente.Id;
                    dto.TreinoNome = maisRecente.Nome;
                    dto.TreinoDataInicio = maisRecente.DataInicio;
                    dto.TreinoDataFim = maisRecente.DataFim;
                }
            }
            // else: default "sem_treino"

            return dto;
        })
        .OrderBy(d => d.Nome)
        .ToList();

        _logger.LogInformation(
            "Returned {Count} alunos for professor {ProfessorId}", result.Count, professorId);

        return await ValidationHelper.OkAsync(req, result);
    }

    /// <summary>
    /// POST /api/treinos/{treinoId}/duplicar?alunoId={destino} — Duplicate a training plan to another student.
    /// Only professors and admins can duplicate.
    /// </summary>
    [Function("DuplicarTreino")]
    public async Task<HttpResponseData> DuplicarTreino(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "treinos/{treinoId}/duplicar")] HttpRequestData req,
        string treinoId)
    {
        if (_tenantContext.IsAluno)
            throw new ForbiddenException("Alunos não podem duplicar treinos.");

        var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var alunoIdDestino = queryParams["alunoId"];

        if (string.IsNullOrWhiteSpace(alunoIdDestino))
            throw new BusinessValidationException("O parâmetro 'alunoId' é obrigatório.");

        // 1. Get the source treino
        var treinoOriginal = await _treinoRepository.GetByIdAsync(treinoId, _tenantContext.TenantId);
        if (treinoOriginal == null)
            throw new NotFoundException("Treino", treinoId);

        if (_tenantContext.IsProfessor && treinoOriginal.CreatedBy != _tenantContext.UserId)
            throw new ForbiddenException("Você só pode duplicar treinos que criou.");

        // 2. Verify destination student exists and belongs to this professor
        var alunoDestino = await _alunoRepository.GetByIdAsync(alunoIdDestino, _tenantContext.TenantId);
        if (alunoDestino == null)
            throw new NotFoundException("Aluno", alunoIdDestino);

        if (_tenantContext.IsProfessor && alunoDestino.ProfessorId != _tenantContext.UserId)
            throw new ForbiddenException("Você só pode duplicar treinos para seus próprios alunos.");

        // 3. Create the duplicate
        var treinoDuplicado = new Treino
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = _tenantContext.TenantId,
            AlunoId = alunoIdDestino,
            ProfessorId = _tenantContext.UserId,
            Nome = $"{treinoOriginal.Nome} (cópia)",
            Descricao = treinoOriginal.Descricao,
            DataInicio = DateTime.UtcNow,
            DataFim = treinoOriginal.DataFim.HasValue
                ? DateTime.UtcNow.Add(treinoOriginal.DataFim.Value - treinoOriginal.DataInicio)
                : null,
            Ativo = true,
            CreatedBy = _tenantContext.UserId,
            UpdatedBy = _tenantContext.UserId,
            Divisoes = treinoOriginal.Divisoes.Select(d => new DivisaoTreino
            {
                Nome = d.Nome,
                Descricao = d.Descricao,
                Ordem = d.Ordem,
                Exercicios = d.Exercicios.Select(e => new ExercicioTreino
                {
                    ExercicioId = e.ExercicioId,
                    Nome = e.Nome,
                    Ordem = e.Ordem,
                    Series = e.Series,
                    Repeticoes = e.Repeticoes,
                    Carga = e.Carga,
                    Metodo = e.Metodo,
                    DescansoSegundos = e.DescansoSegundos,
                    LinkVideo = e.LinkVideo,
                    Observacoes = e.Observacoes
                }).ToList()
            }).ToList()
        };

        _logger.LogInformation(
            "Duplicating treino {OriginalId} to aluno {AlunoId} as {NewId}",
            treinoId, alunoIdDestino, treinoDuplicado.Id);

        var created = await _treinoRepository.CreateAsync(treinoDuplicado);
        return await ValidationHelper.CreatedAsync(req, created);
    }
}
