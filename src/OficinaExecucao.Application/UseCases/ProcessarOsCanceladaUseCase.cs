using Microsoft.Extensions.Logging;
using OficinaExecucao.Domain;
using OficinaExecucao.Domain.Exceptions;

namespace OficinaExecucao.Application.UseCases;

public sealed class ProcessarOsCanceladaUseCase(IExecucaoRepository repositorio, ILogger<ProcessarOsCanceladaUseCase> logger)
{
    public async Task ExecutarAsync(Guid osId, string eventId, CancellationToken ct)
    {
        var execucao = await repositorio.ObterPorOsIdAsync(osId, ct);
        if (execucao is null)
        {
            // Conhecido: SNS não garante ordem entre tópicos diferentes. Se os.cancelada chegar
            // antes de os.criada ser processado, a OS acaba criada e nunca cancelada. Fix real
            // (tombstone ou retry limitado) fica para a Semana 4 — ver README.
            logger.LogWarning("os.cancelada para OS {OsId} ignorado: execução ainda não existe na tabela.", osId);
            return;
        }

        HistoricoEntry historico;
        try
        {
            historico = execucao.Cancelar(origem: "evento");
        }
        catch (TransicaoInvalidaException)
        {
            logger.LogInformation("os.cancelada para OS {OsId} ignorado: execução já está em {Status}.", osId, execucao.Status);
            return;
        }

        await repositorio.SalvarAsync(execucao, historico, ct, eventId);
    }
}
