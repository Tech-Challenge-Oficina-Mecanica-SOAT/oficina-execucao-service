namespace OficinaExecucao.Application.Observabilidade;

/// <summary>
/// Guarda (por escopo DI) o correlationId da mensagem em processamento, para que os eventos
/// publicados durante o consumo mantenham o correlationId de origem.
/// </summary>
public sealed class CorrelacaoAtual
{
    // Estado mutável com escopo DI: usar apenas dentro do escopo por mensagem do consumidor.
    public string? CorrelationId { get; set; }
}
