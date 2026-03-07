using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TreinAI.Api.Treinos.Validators;
using TreinAI.Shared.Exceptions;
using TreinAI.Shared.Middleware;
using TreinAI.Shared.Models;
using TreinAI.Shared.Repositories;
using TreinAI.Shared.Services;
using TreinAI.Shared.Validation;

namespace TreinAI.Api.Treinos.Functions;

/// <summary>
/// CRUD operations for Treino (training plans).
/// Professors create training plans for their students.
/// </summary>
public class TreinoFunctions
{
    private readonly IRepository<Treino> _repository;
    private readonly IRepository<Aluno> _alunoRepo;
    private readonly INotificationService _notifications;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<TreinoFunctions> _logger;

    public TreinoFunctions(
        IRepository<Treino> repository,
        IRepository<Aluno> alunoRepo,
        INotificationService notifications,
        TenantContext tenantContext,
        ILogger<TreinoFunctions> logger)
    {
        _repository = repository;
        _alunoRepo = alunoRepo;
        _notifications = notifications;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the Aluno record ID from the user ID for aluno role users.
    /// </summary>
    private async Task<string?> ResolveAlunoRecordIdAsync()
    {
        var alunos = await _alunoRepo.QueryAsync(
            _tenantContext.TenantId, a => a.UserId == _tenantContext.UserId);
        return alunos.FirstOrDefault()?.Id;
    }

    /// <summary>
    /// GET /api/treinos — List training plans.
    /// Professors see plans they created; alunos see their own plans.
    /// </summary>
    [Function("GetTreinos")]
    public async Task<HttpResponseData> GetTreinos(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "treinos")] HttpRequestData req)
    {
        _logger.LogInformation("Getting treinos for tenant {TenantId}", _tenantContext.TenantId);

        var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var alunoId = queryParams["alunoId"];

        IReadOnlyList<Treino> treinos;

        if (!string.IsNullOrEmpty(alunoId))
        {
            treinos = await _repository.QueryAsync(
                _tenantContext.TenantId,
                t => t.AlunoId == alunoId);
        }
        else if (_tenantContext.IsProfessor)
        {
            treinos = await _repository.QueryAsync(
                _tenantContext.TenantId,
                t => t.CreatedBy == _tenantContext.UserId);
        }
        else if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            treinos = alunoRecordId != null
                ? await _repository.QueryAsync(_tenantContext.TenantId, t => t.AlunoId == alunoRecordId)
                : Array.Empty<Treino>();
        }
        else
        {
            treinos = await _repository.GetAllAsync(_tenantContext.TenantId);
        }

        return await ValidationHelper.OkAsync(req, treinos);
    }

    /// <summary>
    /// GET /api/treinos/{id} — Get a specific training plan.
    /// </summary>
    [Function("GetTreinoById")]
    public async Task<HttpResponseData> GetTreinoById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "treinos/{id}")] HttpRequestData req,
        string id)
    {
        var treino = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (treino == null)
            throw new NotFoundException("Treino", id);

        if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            if (treino.AlunoId != alunoRecordId)
                throw new ForbiddenException("Você só pode acessar seus próprios treinos.");
        }

        return await ValidationHelper.OkAsync(req, treino);
    }

    /// <summary>
    /// POST /api/treinos — Create a new training plan.
    /// Only admin and professor can create.
    /// </summary>
    [Function("CreateTreino")]
    public async Task<HttpResponseData> CreateTreino(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "treinos")] HttpRequestData req)
    {
        if (_tenantContext.IsAluno)
            throw new ForbiddenException("Alunos não podem criar treinos.");

        var validator = new TreinoValidator();
        var treino = await ValidationHelper.ValidateRequestAsync(req, validator);

        treino.TenantId = _tenantContext.TenantId;
        treino.CreatedBy = _tenantContext.UserId;
        treino.UpdatedBy = _tenantContext.UserId;

        _logger.LogInformation("Creating treino '{TreinoNome}' for aluno {AlunoId}", treino.Nome, treino.AlunoId);

        var created = await _repository.CreateAsync(treino);

        // E16-02: Notify the aluno about the new treino
        try
        {
            var aluno = await _alunoRepo.GetByIdAsync(treino.AlunoId, _tenantContext.TenantId);
            if (aluno?.UserId != null)
            {
                var validadeMsg = treino.DataFim.HasValue
                    ? $" — válido até {treino.DataFim.Value:dd/MM/yyyy}"
                    : "";
                await _notifications.CreateAsync(
                    _tenantContext.TenantId,
                    aluno.UserId,
                    "Novo treino disponível",
                    $"Seu professor atribuiu um novo treino: {treino.Nome}{validadeMsg}.",
                    "novo_treino",
                    $"/treinos/{created.Id}",
                    _tenantContext.UserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification for new treino {TreinoId}", created.Id);
        }

        return await ValidationHelper.CreatedAsync(req, created);
    }

    /// <summary>
    /// PUT /api/treinos/{id} — Update a training plan.
    /// </summary>
    [Function("UpdateTreino")]
    public async Task<HttpResponseData> UpdateTreino(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "treinos/{id}")] HttpRequestData req,
        string id)
    {
        var existing = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (existing == null)
            throw new NotFoundException("Treino", id);

        if (_tenantContext.IsAluno)
            throw new ForbiddenException("Alunos não podem editar treinos.");

        if (_tenantContext.IsProfessor && existing.CreatedBy != _tenantContext.UserId)
            throw new ForbiddenException("Você só pode editar treinos que criou.");

        var validator = new TreinoValidator();
        var treino = await ValidationHelper.ValidateRequestAsync(req, validator);

        treino.Id = id;
        treino.TenantId = _tenantContext.TenantId;
        treino.CreatedAt = existing.CreatedAt;
        treino.CreatedBy = existing.CreatedBy;
        treino.UpdatedBy = _tenantContext.UserId;

        var updated = await _repository.UpdateAsync(treino);
        return await ValidationHelper.OkAsync(req, updated);
    }

    /// <summary>
    /// DELETE /api/treinos/{id} — Soft-delete a training plan.
    /// </summary>
    [Function("DeleteTreino")]
    public async Task<HttpResponseData> DeleteTreino(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "treinos/{id}")] HttpRequestData req,
        string id)
    {
        if (_tenantContext.IsAluno)
            throw new ForbiddenException("Alunos não podem excluir treinos.");

        var existing = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (existing == null)
            throw new NotFoundException("Treino", id);

        if (_tenantContext.IsProfessor && existing.CreatedBy != _tenantContext.UserId)
            throw new ForbiddenException("Você só pode excluir treinos que criou.");

        await _repository.DeleteAsync(id, _tenantContext.TenantId);
        return ValidationHelper.NoContent(req);
    }

    /// <summary>
    /// GET /api/treinos/aluno/{alunoId}/ativo — Get the currently active training plan for a student.
    /// </summary>
    [Function("GetTreinoAtivo")]
    public async Task<HttpResponseData> GetTreinoAtivo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "treinos/aluno/{alunoId}/ativo")] HttpRequestData req,
        string alunoId)
    {
        if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            if (alunoId != alunoRecordId)
                throw new ForbiddenException("Você só pode acessar seus próprios treinos.");
        }

        var now = DateTime.UtcNow;
        var treinos = await _repository.QueryAsync(
            _tenantContext.TenantId,
            t => t.AlunoId == alunoId && t.Ativo && t.DataInicio <= now &&
                 (!t.DataFim.HasValue || t.DataFim.Value >= now));

        var treinoAtivo = treinos.FirstOrDefault();
        if (treinoAtivo == null)
            throw new NotFoundException($"Nenhum treino ativo encontrado para o aluno '{alunoId}'.");

        return await ValidationHelper.OkAsync(req, treinoAtivo);
    }
}
