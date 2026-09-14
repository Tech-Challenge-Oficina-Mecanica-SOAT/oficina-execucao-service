namespace OficinaExecucao.Infrastructure.Messaging;

public sealed class SqsOptions
{
    public required string QueueUrl { get; init; }
    public string? ServiceUrl { get; init; }
}
