using OficinaExecucao.Domain;

namespace OficinaExecucao.Application.UseCases;

public sealed class ObterHistoricoUseCase(IExecucaoRepository repositorio)
{
    public Task<IReadOnlyList<HistoricoEntry>> ExecutarAsync(Guid osId, CancellationToken ct) => repositorio.ObterHistoricoAsync(osId, ct);
}
