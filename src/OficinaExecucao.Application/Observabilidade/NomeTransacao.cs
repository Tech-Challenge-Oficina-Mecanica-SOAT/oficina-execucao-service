using System.Text.RegularExpressions;

namespace OficinaExecucao.Application.Observabilidade;

public static partial class NomeTransacao
{
    public const string CategoriaWeb = "Web";
    public const string CategoriaMensagem = "Message";

    [GeneratedRegex(@"\{(?<nome>\*{0,2}\w+\??)(:[^}]*)?\}")]
    private static partial Regex ParametroRegex();

    /// <summary>Ex.: "GET /execucao/{osId}" (template da rota, sem restrições como ":guid", nunca o osId concreto).</summary>
    public static string Web(string metodoHttp, string templateRota) =>
        $"{metodoHttp.ToUpperInvariant()} {ParametroRegex().Replace(templateRota, "{${nome}}")}";

    private const string TipoDesconhecido = "desconhecido";

    // Lista fechada: o eventType vem do corpo da mensagem, então nomes livres criariam métricas ilimitadas no APM.
    private static readonly HashSet<string> TiposConhecidos = new(StringComparer.Ordinal)
    {
        "os.criada", "os.cancelada", "orcamento.aprovado",
    };

    /// <summary>Ex.: "SQS/os.criada". Tipos fora da lista conhecida (ou nulos/vazios/longos) viram "SQS/desconhecido".</summary>
    public static string Mensagem(string? eventType) =>
        $"SQS/{(eventType is not null && TiposConhecidos.Contains(eventType) ? eventType : TipoDesconhecido)}";

    /// <summary>Health checks (liveness/readiness) são ruído para o APM.</summary>
    public static bool DeveIgnorar(string? path) =>
        path is not null &&
        (path.Equals("/health", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase));
}
