using System.Text.Json;
using OficinaExecucao.Application.UseCases;

namespace OficinaExecucao.Application;

public sealed class MensagemDispatcher(
    IExecucaoRepository repositorio,
    ProcessarOsCriadaUseCase processarOsCriada,
    ProcessarOsCanceladaUseCase processarOsCancelada,
    ProcessarOrcamentoAprovadoUseCase processarOrcamentoAprovado) : IMensagemDispatcher
{
    public async Task ProcessarAsync(string mensagemJson, CancellationToken ct)
    {
        using var documento = JsonDocument.Parse(mensagemJson);
        var raiz = documento.RootElement;

        var eventId = raiz.GetProperty("eventId").GetString()!;
        var eventType = raiz.GetProperty("eventType").GetString()!;
        var data = raiz.GetProperty("data");
        var osId = Guid.Parse(data.GetProperty("osId").GetString()!);

        if (await repositorio.EventoJaProcessadoAsync(osId, eventId, ct))
            return;

        switch (eventType)
        {
            case "os.criada":
                await processarOsCriada.ExecutarAsync(osId, eventId, ct);
                break;
            case "os.cancelada":
                await processarOsCancelada.ExecutarAsync(osId, eventId, ct);
                break;
            case "orcamento.aprovado":
                await processarOrcamentoAprovado.ExecutarAsync(osId, eventId, ct);
                break;
            default:
                throw new InvalidOperationException($"Tipo de evento desconhecido: '{eventType}'.");
        }
    }
}
