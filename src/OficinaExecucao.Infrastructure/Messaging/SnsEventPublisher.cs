using System.Diagnostics;
using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;
using OficinaExecucao.Application;
using OficinaExecucao.Application.Observabilidade;

namespace OficinaExecucao.Infrastructure.Messaging;

public sealed class SnsEventPublisher(
    IAmazonSimpleNotificationService client,
    IOptions<SnsOptions> options,
    CorrelacaoAtual? correlacaoAtual = null) : IEventPublisher
{
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task PublicarAsync<T>(string eventType, T data, CancellationToken ct)
    {
        if (!options.Value.TopicArns.TryGetValue(eventType, out var topicArn))
            throw new InvalidOperationException($"Nenhum tópico SNS configurado para o evento '{eventType}'.");

        var envelope = new
        {
            eventId = Guid.NewGuid().ToString(),
            eventType,
            eventVersion = "1.0",
            occurredAt = DateTimeOffset.UtcNow.ToString("O"),
            correlationId = Activity.Current?.Id ?? correlacaoAtual?.CorrelationId ?? Guid.NewGuid().ToString(),
            producer = "execucao-service",
            data
        };

        await client.PublishAsync(new PublishRequest
        {
            TopicArn = topicArn,
            Message = JsonSerializer.Serialize(envelope, EnvelopeJsonOptions)
        }, ct);
    }
}
