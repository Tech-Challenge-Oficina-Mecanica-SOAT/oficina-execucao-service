using OficinaExecucao.Domain;

namespace OficinaExecucao.Application.UseCases;

public sealed class RegistrarDiagnosticoUseCase(IExecucaoRepository repositorio, IEventPublisher publisher)
{
    public async Task<Execucao> ExecutarAsync(
        Guid osId,
        IReadOnlyList<ItemPeca> pecas,
        IReadOnlyList<ItemServico> servicos,
        decimal? tempoEstimadoHoras,
        string? observacoes,
        CancellationToken ct)
    {
        var execucao = await repositorio.ObterPorOsIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"Execução para a OS '{osId}' não encontrada.");

        var historico = execucao.RegistrarDiagnostico(pecas, servicos, tempoEstimadoHoras, observacoes);

        await repositorio.SalvarAsync(execucao, historico, ct);

        await publisher.PublicarAsync("diagnostico.concluido", new
        {
            osId,
            diagnosticoId = execucao.DiagnosticoId,
            pecas,
            servicos,
            tempoEstimadoHoras,
            concluidoEm = historico.MudouEm
        }, ct);

        return execucao;
    }
}
