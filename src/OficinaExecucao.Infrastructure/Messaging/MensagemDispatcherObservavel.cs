using Microsoft.Extensions.Logging;
using OficinaExecucao.Application;
using OficinaExecucao.Application.Observabilidade;

namespace OficinaExecucao.Infrastructure.Messaging;

/// <summary>
/// Envolve o dispatcher real com a observabilidade do consumo: transação de background no APM,
/// escopo de log (correlationId = trace id de origem, osId) e correlação de entrada para os eventos publicados.
/// </summary>
public sealed class MensagemDispatcherObservavel(
    IMensagemDispatcher interno,
    IObservabilidade observabilidade,
    CorrelacaoAtual correlacaoAtual,
    ILogger<MensagemDispatcherObservavel> logger) : IMensagemDispatcher
{
    public Task ProcessarAsync(string mensagemJson, CancellationToken ct)
    {
        var contexto = ContextoMensagem.Ler(mensagemJson);
        correlacaoAtual.CorrelationId = contexto.CorrelationId;

        var escopo = new Dictionary<string, object>();
        if (contexto.TraceId is not null)
            escopo["correlationId"] = contexto.TraceId;
        if (contexto.OsId is not null)
            escopo["osId"] = contexto.OsId;

        return observabilidade.ProcessarMensagemAsync(contexto, async () =>
        {
            using (logger.BeginScope(escopo))
            {
                await interno.ProcessarAsync(mensagemJson, ct);
            }
        });
    }
}
