using System.Text.Json;

namespace OficinaExecucao.Application.Observabilidade;

/// <summary>
/// Campos do envelope usados só para observabilidade. A leitura é tolerante: nunca lança,
/// pois a falha de processamento (se houver) é responsabilidade do dispatcher.
/// </summary>
public sealed record ContextoMensagem(string? EventId, string? EventType, string? OsId, string? CorrelationId)
{
    /// <summary>Trace id para logs e atributos (ver <see cref="CorrelacaoHelper.ExtrairTraceId"/>).</summary>
    public string? TraceId => CorrelacaoHelper.ExtrairTraceId(CorrelationId);

    public static ContextoMensagem Ler(string mensagemJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(mensagemJson);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
                return new ContextoMensagem(null, null, null, null);

            string? osId = null;
            if (raiz.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                osId = Texto(data, "osId");

            return new ContextoMensagem(Texto(raiz, "eventId"), Texto(raiz, "eventType"), osId, Texto(raiz, "correlationId"));
        }
        catch (JsonException)
        {
            return new ContextoMensagem(null, null, null, null);
        }
    }

    private static string? Texto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;
}
