using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using OficinaExecucao.Application.Configuration;

namespace OficinaExecucao.Tests.BDD.Support;

/// <summary>Estado de um cenário, compartilhado entre os passos (o Reqnroll cria um por cenário).</summary>
public sealed class ContextoBdd
{
    public HttpClient Http { get; } = CriarClienteAutenticado();

    public Guid OsId { get; set; }

    public HttpResponseMessage? UltimaResposta { get; set; }

    private static HttpClient CriarClienteAutenticado()
    {
        var client = AmbienteBdd.Factory.CreateClient();
        var settings = AmbienteBdd.Factory.Services.GetRequiredService<IJwtSettings>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.GerarToken(settings));
        return client;
    }
}
