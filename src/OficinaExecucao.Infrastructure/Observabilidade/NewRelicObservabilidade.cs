using NewRelic.Api.Agent;
using OficinaExecucao.Application.Observabilidade;
using NewRelicApi = NewRelic.Api.Agent.NewRelic;

namespace OficinaExecucao.Infrastructure.Observabilidade;

/// <summary>
/// Implementação sobre a API do agente .NET do New Relic. Sem o agente carregado (testes, execução local)
/// as chamadas da NewRelic.Agent.Api são no-ops.
/// </summary>
public sealed class NewRelicObservabilidade : IObservabilidade
{
    public void NomearTransacaoWeb(string nome) =>
        NewRelicApi.SetTransactionName(NomeTransacao.CategoriaWeb, nome);

    public void IgnorarTransacao() => NewRelicApi.IgnoreTransaction();

    public void AdicionarAtributo(string chave, string valor) =>
        NewRelicApi.GetAgent().CurrentTransaction.AddCustomAttribute(chave, valor);

    // [Transaction] cria uma transação "OtherTransaction" para o método (e seu Task, por ser async),
    // o que funciona em um BackgroundService, onde o agente não cria transação sozinho.
    // O nome padrão (classe/método) é trocado por "Message/SQS/<eventType>".
    [Transaction]
    public async Task ProcessarMensagemAsync(ContextoMensagem contexto, Func<Task> processamento)
    {
        var transacao = NewRelicApi.GetAgent().CurrentTransaction;

        NewRelicApi.SetTransactionName(NomeTransacao.CategoriaMensagem, NomeTransacao.Mensagem(contexto.EventType));
        Atributo(transacao, "eventType", contexto.EventType);
        Atributo(transacao, "eventId", contexto.EventId);
        Atributo(transacao, "osId", contexto.OsId);
        Atributo(transacao, "correlationId", contexto.TraceId);

        // Se o correlationId do envelope é um traceparent válido, tentamos continuar a trace de origem.
        // Se o New Relic UI de fato liga os spans (e não só correlaciona por correlationId/osId) depende do
        // contexto de trace que o produtor enviou; o caminho garantido é a correlação por correlationId/osId.
        if (CorrelacaoHelper.EhTraceparent(contexto.CorrelationId))
        {
            transacao.AcceptDistributedTraceHeaders(
                contexto.CorrelationId!,
                static (traceparent, chave) => chave.Equals("traceparent", StringComparison.OrdinalIgnoreCase) ? [traceparent] : [],
                TransportType.Queue);
        }

        try
        {
            await processamento();
        }
        catch (Exception ex)
        {
            NewRelicApi.NoticeError(ex);
            throw;
        }
    }

    private static void Atributo(ITransaction transacao, string chave, string? valor)
    {
        if (valor is not null)
            transacao.AddCustomAttribute(chave, valor);
    }
}
