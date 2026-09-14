namespace OficinaExecucao.Infrastructure.Messaging;

public sealed class SnsOptions
{
    public required Dictionary<string, string> TopicArns { get; init; }
}
