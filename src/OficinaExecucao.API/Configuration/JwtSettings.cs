using Microsoft.Extensions.Configuration;
using OficinaExecucao.Application.Configuration;

namespace OficinaExecucao.API.Configuration;

public class JwtSettings : IJwtSettings
{
    public string SecretKey { get; }
    public string Issuer { get; }
    public string LambdaIssuer { get; }
    public string Audience { get; }

    public JwtSettings(IConfiguration configuration)
    {
        SecretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey não configurada.");
        Issuer = configuration["Jwt:Issuer"] ?? "execucao-api";
        LambdaIssuer = configuration["Jwt:LambdaIssuer"] ?? "oficina-mecanica-lambda";
        Audience = configuration["Jwt:Audience"] ?? "oficina-cliente";
    }
}
