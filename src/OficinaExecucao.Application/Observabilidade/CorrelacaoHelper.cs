using System.Text.RegularExpressions;

namespace OficinaExecucao.Application.Observabilidade;

/// <summary>
/// O correlationId dos eventos do grupo é o traceparent W3C da requisição de origem
/// (00-&lt;trace id 32 hex&gt;-&lt;span id 16 hex&gt;-&lt;flags&gt;). Nos logs e nos atributos usamos só o trace id.
/// </summary>
public static partial class CorrelacaoHelper
{
    [GeneratedRegex("^[0-9a-fA-F]{2}-(?<trace>[0-9a-fA-F]{32})-[0-9a-fA-F]{16}-[0-9a-fA-F]{2}$")]
    private static partial Regex TraceparentRegex();

    private const string TraceIdZerado = "00000000000000000000000000000000";

    public static bool EhTraceparent(string? valor) => TentarLerTraceId(valor, out _);

    /// <summary>Trace id do traceparent; se não for um traceparent, o próprio valor; nulo se vazio.</summary>
    public static string? ExtrairTraceId(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return null;

        return TentarLerTraceId(correlationId, out var traceId) ? traceId : correlationId;
    }

    private static bool TentarLerTraceId(string? valor, out string traceId)
    {
        traceId = string.Empty;
        if (string.IsNullOrWhiteSpace(valor))
            return false;

        var match = TraceparentRegex().Match(valor);
        if (!match.Success)
            return false;

        var candidato = match.Groups["trace"].Value.ToLowerInvariant();
        if (candidato == TraceIdZerado)
            return false;

        traceId = candidato;
        return true;
    }
}
