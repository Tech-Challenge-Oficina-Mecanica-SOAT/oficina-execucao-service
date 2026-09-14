using OficinaExecucao.Domain;

namespace OficinaExecucao.Application.UseCases;

public sealed class ProcessarOsCriadaUseCase(IExecucaoRepository repositorio)
{
    public async Task ExecutarAsync(Guid osId, string eventId, CancellationToken ct)
    {
        var execucao = Execucao.NovaNaFila(osId);
        var historico = new HistoricoEntry(execucao.Status, execucao.Status, DateTimeOffset.UtcNow, "evento");

        await repositorio.SalvarAsync(execucao, historico, ct, eventId);
    }
}
