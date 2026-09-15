using Amazon;
using Amazon.SQS;

namespace OficinaExecucao.Infrastructure.Messaging;

public static class AmazonSqsClientFactory
{
    public static AmazonSQSConfig BuildConfig(SqsOptions options)
    {
        var config = new AmazonSQSConfig { RegionEndpoint = RegionEndpoint.USEast1 };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.UseHttp = true;
            config.AuthenticationRegion = RegionEndpoint.USEast1.SystemName;
        }

        return config;
    }

    public static IAmazonSQS Create(SqsOptions options) => new AmazonSQSClient(BuildConfig(options));
}
