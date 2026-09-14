namespace OficinaExecucao.Infrastructure.DynamoDb;

public sealed class DynamoDbOptions
{
    public required string TableName { get; init; }
    public string? ServiceUrl { get; init; }
}
