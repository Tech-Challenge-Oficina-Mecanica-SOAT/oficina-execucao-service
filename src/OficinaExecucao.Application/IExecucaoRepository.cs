using OficinaExecucao.Domain;

namespace OficinaExecucao.Application;

public interface IExecucaoRepository
{
    Task<Execucao?> ObterPorOsIdAsync(Guid osId, CancellationToken ct);

    Task SalvarAsync(Execucao execucao, HistoricoEntry novaEntrada, CancellationToken ct, string? eventId = null);

    Task<IReadOnlyList<HistoricoEntry>> ObterHistoricoAsync(Guid osId, CancellationToken ct);

    Task<IReadOnlyList<Execucao>> ListarPorStatusAsync(StatusExecucao status, CancellationToken ct);

    Task<bool> EventoJaProcessadoAsync(Guid osId, string eventId, CancellationToken ct);
}
