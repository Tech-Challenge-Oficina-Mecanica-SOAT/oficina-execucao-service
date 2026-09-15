using Amazon;
using Amazon.SimpleNotificationService;

namespace OficinaExecucao.Infrastructure.Messaging;

public static class AmazonSnsClientFactory
{
    public static AmazonSimpleNotificationServiceConfig BuildConfig(SnsOptions options)
    {
        var config = new AmazonSimpleNotificationServiceConfig { RegionEndpoint = RegionEndpoint.USEast1 };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.UseHttp = true;
            config.AuthenticationRegion = RegionEndpoint.USEast1.SystemName;
        }

        return config;
    }

    public static IAmazonSimpleNotificationService Create(SnsOptions options) => new AmazonSimpleNotificationServiceClient(BuildConfig(options));
}
