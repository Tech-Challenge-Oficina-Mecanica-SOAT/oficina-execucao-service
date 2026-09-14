namespace OficinaExecucao.Application;

public interface IEventPublisher
{
    Task PublicarAsync<T>(string eventType, T data, CancellationToken ct);
}
