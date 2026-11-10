namespace OficinaExecucao.Application.Observabilidade;

/// <summary>
/// Guarda (por escopo DI) o correlationId da mensagem em processamento, para que os eventos
/// publicados durante o consumo mantenham o correlationId de origem.
/// </summary>
public sealed class CorrelacaoAtual
{
    public string? CorrelationId { get; set; }
}
