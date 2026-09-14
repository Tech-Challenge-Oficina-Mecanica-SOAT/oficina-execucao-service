namespace OficinaExecucao.Application.Configuration;

public interface IJwtSettings
{
    string SecretKey { get; }
    string Issuer { get; }
    string LambdaIssuer { get; }
    string Audience { get; }
}
