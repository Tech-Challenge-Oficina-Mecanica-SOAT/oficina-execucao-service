using System.Security.Cryptography;
using System.Text;
using AspNetCore.Authentication.ApiKey;
using Microsoft.Extensions.Configuration;

namespace OficinaExecucao.Infrastructure.Auth;

public class InternalApiKeyProvider(IConfiguration configuration) : IApiKeyProvider
{
    public Task<IApiKey?> ProvideAsync(string key)
    {
        var configuredKey = configuration["InternalApi:ApiKey"];

        IApiKey? result = !string.IsNullOrEmpty(configuredKey) && ChavesIguais(configuredKey, key)
            ? new InternalApiKey(key)
            : null;

        return Task.FromResult(result);
    }

    private static bool ChavesIguais(string configuredKey, string key)
    {
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var keyBytes = Encoding.UTF8.GetBytes(key);

        return configuredBytes.Length == keyBytes.Length &&
               CryptographicOperations.FixedTimeEquals(configuredBytes, keyBytes);
    }

    private sealed class InternalApiKey(string key) : IApiKey
    {
        public string Key { get; } = key;
        public string OwnerName { get; } = "execucao-service";
        public IReadOnlyCollection<System.Security.Claims.Claim> Claims { get; } = new List<System.Security.Claims.Claim>();
    }
}
