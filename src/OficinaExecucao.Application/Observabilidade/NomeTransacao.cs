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

    /// <summary>Ex.: "SQS/os.criada".</summary>
    public static string Mensagem(string? eventType) =>
        $"SQS/{(string.IsNullOrWhiteSpace(eventType) ? "desconhecido" : eventType)}";

    /// <summary>Health checks (liveness/readiness) são ruído para o APM.</summary>
    public static bool DeveIgnorar(string? path) =>
        path is not null && path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
}
