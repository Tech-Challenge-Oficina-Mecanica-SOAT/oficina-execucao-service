namespace OficinaExecucao.Application.UseCases;

public sealed class FinalizarUseCase(IExecucaoRepository repositorio, IEventPublisher publisher)
{
    public async Task<Domain.Execucao> ExecutarAsync(Guid osId, decimal tempoRealHoras, CancellationToken ct)
    {
        var execucao = await repositorio.ObterPorOsIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"Execução para a OS '{osId}' não encontrada.");

        var historico = execucao.Finalizar(tempoRealHoras);

        await repositorio.SalvarAsync(execucao, historico, ct);

        await publisher.PublicarAsync("execucao.finalizada", new
        {
            osId,
            execucaoId = execucao.ExecucaoId,
            tempoRealHoras,
            finalizadaEm = execucao.FinalizadaEm
        }, ct);

        return execucao;
    }
}
