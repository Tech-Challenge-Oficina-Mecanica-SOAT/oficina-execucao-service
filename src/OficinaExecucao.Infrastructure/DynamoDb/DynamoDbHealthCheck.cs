using Amazon.DynamoDBv2;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace OficinaExecucao.Infrastructure.DynamoDb;

public sealed class DynamoDbHealthCheck(IAmazonDynamoDB client, IOptions<DynamoDbOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await client.DescribeTableAsync(options.Value.TableName, ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Não foi possível conectar ao DynamoDB.", ex);
        }
    }
}
