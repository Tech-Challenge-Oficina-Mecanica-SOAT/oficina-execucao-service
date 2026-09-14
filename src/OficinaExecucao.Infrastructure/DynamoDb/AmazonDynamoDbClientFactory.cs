using Amazon;
using Amazon.DynamoDBv2;

namespace OficinaExecucao.Infrastructure.DynamoDb;

public static class AmazonDynamoDbClientFactory
{
    public static AmazonDynamoDBConfig BuildConfig(DynamoDbOptions options)
    {
        var config = new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.USEast1 };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.UseHttp = true;
        }

        return config;
    }

    public static IAmazonDynamoDB Create(DynamoDbOptions options) => new AmazonDynamoDBClient(BuildConfig(options));
}
