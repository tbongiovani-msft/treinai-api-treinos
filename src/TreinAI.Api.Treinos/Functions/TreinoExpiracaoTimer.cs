using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TreinAI.Shared.Models;
using TreinAI.Shared.Repositories;
using TreinAI.Shared.Services;

namespace TreinAI.Api.Treinos.Functions;

/// <summary>
/// Timer-triggered function that checks for expiring treinos and sends notifications.
/// Runs daily at 08:00 UTC (05:00 BRT).
/// E16-03: Notification when treino is about to expire (5, 2, 1 day).
/// </summary>
public class TreinoExpiracaoTimer
{
    private readonly IRepository<Treino> _treinoRepo;
    private readonly IRepository<Aluno> _alunoRepo;
    private readonly INotificationService _notifications;
    private readonly ILogger<TreinoExpiracaoTimer> _logger;

    public TreinoExpiracaoTimer(
        IRepository<Treino> treinoRepo,
        IRepository<Aluno> alunoRepo,
        INotificationService notifications,
        ILogger<TreinoExpiracaoTimer> logger)
    {
        _treinoRepo = treinoRepo;
        _alunoRepo = alunoRepo;
        _notifications = notifications;
        _logger = logger;
    }

    [Function("TreinoExpiracaoTimer")]
    public async Task Run([TimerTrigger("0 0 8 * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("TreinoExpiracaoTimer triggered at {Time}", DateTime.UtcNow);

        // Get all alunos (we need tenantIds and userIds)
        // NOTE: In production with many tenants, this should iterate per tenant.
        // For now (single tenant), we query all alunos and treinos.
        var today = DateTime.UtcNow.Date;
        var checkDays = new[] { 5, 2, 1 };

        // Get all active alunos
        // Since timer triggers don't have TenantContext, we query across the known tenant
        var tenantId = Environment.GetEnvironmentVariable("DefaultTenantId") ?? "t-treinai-001";

        var treinos = await _treinoRepo.QueryAsync(tenantId, t => t.Ativo && t.DataFim.HasValue);
        var alunoIds = treinos.Select(t => t.AlunoId).Distinct().ToList();

        if (alunoIds.Count == 0)
        {
            _logger.LogInformation("No active treinos with expiration dates found.");
            return;
        }

        var alunos = await _alunoRepo.GetAllAsync(tenantId);
        var alunoMap = alunos.ToDictionary(a => a.Id, a => a);

        var notificationsSent = 0;

        foreach (var treino in treinos)
        {
            if (!treino.DataFim.HasValue) continue;

            var daysUntilExpiry = (treino.DataFim.Value.Date - today).Days;

            if (!checkDays.Contains(daysUntilExpiry)) continue;

            if (!alunoMap.TryGetValue(treino.AlunoId, out var aluno) || aluno.UserId == null)
                continue;

            var mensagem = daysUntilExpiry switch
            {
                1 => $"Seu treino \"{treino.Nome}\" expira amanhã! Fale com seu professor para renovação.",
                2 => $"Seu treino \"{treino.Nome}\" expira em 2 dias ({treino.DataFim.Value:dd/MM/yyyy}).",
                5 => $"Seu treino \"{treino.Nome}\" expira em 5 dias ({treino.DataFim.Value:dd/MM/yyyy}). Considere falar com seu professor.",
                _ => null
            };

            if (mensagem == null) continue;

            try
            {
                await _notifications.CreateAsync(
                    tenantId,
                    aluno.UserId,
                    "Treino expirando",
                    mensagem,
                    "treino_expirando",
                    $"/treinos/{treino.Id}",
                    "system");
                notificationsSent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send expiration notification for treino {TreinoId}", treino.Id);
            }
        }

        _logger.LogInformation("TreinoExpiracaoTimer completed. Sent {Count} notifications.", notificationsSent);
    }
}
