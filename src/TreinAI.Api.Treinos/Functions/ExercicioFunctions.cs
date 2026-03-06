using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TreinAI.Api.Treinos.Validators;
using TreinAI.Shared.Exceptions;
using TreinAI.Shared.Middleware;
using TreinAI.Shared.Models;
using TreinAI.Shared.Repositories;
using TreinAI.Shared.Validation;

namespace TreinAI.Api.Treinos.Functions;

/// <summary>
/// CRUD for the Exercicio catalog (exercise library).
/// Exercises are shared across tenants but can be tenant-specific too.
/// </summary>
public class ExercicioFunctions
{
    private readonly IRepository<Exercicio> _repository;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<ExercicioFunctions> _logger;

    public ExercicioFunctions(
        IRepository<Exercicio> repository,
        TenantContext tenantContext,
        ILogger<ExercicioFunctions> logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/exercicios — List exercises. Supports ?grupoMuscular= filter.
    /// </summary>
    [Function("GetExercicios")]
    public async Task<HttpResponseData> GetExercicios(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "exercicios")] HttpRequestData req)
    {
        var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var grupoMuscular = queryParams["grupoMuscular"];
        var search = queryParams["search"];

        IReadOnlyList<Exercicio> exercicios;

        if (!string.IsNullOrEmpty(grupoMuscular))
        {
            exercicios = await _repository.QueryAsync(
                _tenantContext.TenantId,
                e => e.GrupoMuscular == grupoMuscular);
        }
        else if (!string.IsNullOrEmpty(search))
        {
            exercicios = await _repository.QueryAsync(
                _tenantContext.TenantId,
                e => e.Nome.Contains(search));
        }
        else
        {
            exercicios = await _repository.GetAllAsync(_tenantContext.TenantId);
        }

        return await ValidationHelper.OkAsync(req, exercicios);
    }

    /// <summary>
    /// GET /api/exercicios/{id}
    /// </summary>
    [Function("GetExercicioById")]
    public async Task<HttpResponseData> GetExercicioById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "exercicios/{id}")] HttpRequestData req,
        string id)
    {
        var exercicio = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (exercicio == null)
            throw new NotFoundException("Exercicio", id);

        return await ValidationHelper.OkAsync(req, exercicio);
    }

    /// <summary>
    /// POST /api/exercicios — Create a new exercise in the catalog.
    /// Only admin and professor can create.
    /// </summary>
    [Function("CreateExercicio")]
    public async Task<HttpResponseData> CreateExercicio(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "exercicios")] HttpRequestData req)
    {
        if (_tenantContext.IsAluno)
            throw new ForbiddenException("Alunos não podem criar exercícios.");

        var validator = new ExercicioValidator();
        var exercicio = await ValidationHelper.ValidateRequestAsync(req, validator);

        exercicio.TenantId = _tenantContext.TenantId;
        exercicio.CreatedBy = _tenantContext.UserId;
        exercicio.UpdatedBy = _tenantContext.UserId;

        var created = await _repository.CreateAsync(exercicio);
        return await ValidationHelper.CreatedAsync(req, created);
    }

    /// <summary>
    /// PUT /api/exercicios/{id}
    /// </summary>
    [Function("UpdateExercicio")]
    public async Task<HttpResponseData> UpdateExercicio(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "exercicios/{id}")] HttpRequestData req,
        string id)
    {
        if (_tenantContext.IsAluno)
            throw new ForbiddenException("Alunos não podem editar exercícios.");

        var existing = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (existing == null)
            throw new NotFoundException("Exercicio", id);

        var validator = new ExercicioValidator();
        var exercicio = await ValidationHelper.ValidateRequestAsync(req, validator);

        exercicio.Id = id;
        exercicio.TenantId = _tenantContext.TenantId;
        exercicio.CreatedAt = existing.CreatedAt;
        exercicio.CreatedBy = existing.CreatedBy;
        exercicio.UpdatedBy = _tenantContext.UserId;

        var updated = await _repository.UpdateAsync(exercicio);
        return await ValidationHelper.OkAsync(req, updated);
    }

    /// <summary>
    /// DELETE /api/exercicios/{id}
    /// </summary>
    [Function("DeleteExercicio")]
    public async Task<HttpResponseData> DeleteExercicio(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "exercicios/{id}")] HttpRequestData req,
        string id)
    {
        if (_tenantContext.IsAluno)
            throw new ForbiddenException("Alunos não podem excluir exercícios.");

        var existing = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (existing == null)
            throw new NotFoundException("Exercicio", id);

        await _repository.DeleteAsync(id, _tenantContext.TenantId);
        return ValidationHelper.NoContent(req);
    }
}
