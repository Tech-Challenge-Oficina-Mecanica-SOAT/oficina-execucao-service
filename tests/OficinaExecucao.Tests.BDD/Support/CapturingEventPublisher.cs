using System.Collections.Concurrent;
using OficinaExecucao.Application;

namespace OficinaExecucao.Tests.BDD.Support;

/// <summary>Publicador falso: guarda em memória cada evento publicado, para os steps conferirem o payload.</summary>
public sealed class CapturingEventPublisher : IEventPublisher
{
    public sealed record EventoPublicado(string EventType, object? Data);

    public ConcurrentBag<EventoPublicado> Publicados { get; } = new();

    public Task PublicarAsync<T>(string eventType, T data, CancellationToken ct)
    {
        Publicados.Add(new EventoPublicado(eventType, data));
        return Task.CompletedTask;
    }
}
