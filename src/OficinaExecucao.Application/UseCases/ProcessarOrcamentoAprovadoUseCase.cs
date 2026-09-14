using OficinaExecucao.Application;

namespace OficinaExecucao.Application.UseCases;

public sealed class ProcessarOrcamentoAprovadoUseCase(IExecucaoRepository repositorio)
{
    public async Task ExecutarAsync(Guid osId, string eventId, CancellationToken ct)
    {
        var execucao = await repositorio.ObterPorOsIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"Execução para a OS '{osId}' não encontrada.");

        var historico = execucao.AprovarOrcamento(origem: "evento");

        await repositorio.SalvarAsync(execucao, historico, ct, eventId);
    }
}
