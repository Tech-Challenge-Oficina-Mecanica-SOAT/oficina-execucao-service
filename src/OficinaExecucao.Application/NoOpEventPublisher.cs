using Microsoft.Extensions.Logging;

namespace OficinaExecucao.Application;

public sealed class NoOpEventPublisher(ILogger<NoOpEventPublisher> logger) : IEventPublisher
{
    public Task PublicarAsync<T>(string eventType, T data, CancellationToken ct)
    {
        logger.LogInformation("Evento {EventType} não publicado (IEventPublisher real ainda não implementado): {@Data}", eventType, data);
        return Task.CompletedTask;
    }
}
