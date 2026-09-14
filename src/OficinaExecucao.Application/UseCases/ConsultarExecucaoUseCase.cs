using OficinaExecucao.Domain;

namespace OficinaExecucao.Application.UseCases;

public sealed class ConsultarExecucaoUseCase(IExecucaoRepository repositorio)
{
    public Task<Execucao?> ExecutarAsync(Guid osId, CancellationToken ct) => repositorio.ObterPorOsIdAsync(osId, ct);
}
