namespace OficinaExecucao.Application.UseCases;

public sealed class IniciarReparoUseCase(IExecucaoRepository repositorio, IEventPublisher publisher)
{
    public async Task<Domain.Execucao> ExecutarAsync(Guid osId, CancellationToken ct)
    {
        var execucao = await repositorio.ObterPorOsIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"Execução para a OS '{osId}' não encontrada.");

        var historico = execucao.IniciarReparo();

        await repositorio.SalvarAsync(execucao, historico, ct);

        await publisher.PublicarAsync("execucao.iniciada", new
        {
            osId,
            execucaoId = execucao.ExecucaoId,
            iniciadaEm = execucao.IniciadaEm
        }, ct);

        return execucao;
    }
}
