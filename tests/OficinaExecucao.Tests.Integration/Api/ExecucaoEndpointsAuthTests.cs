using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OficinaExecucao.Application;
using OficinaExecucao.Application.Configuration;
using OficinaExecucao.Tests.Integration.Auth;
using OficinaExecucao.Tests.Integration.Fakes;
using Xunit;

namespace OficinaExecucao.Tests.Integration.Api;

[Collection("Integration")]
public class ExecucaoEndpointsAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExecucaoEndpointsAuthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IExecucaoRepository>();
                services.AddSingleton<IExecucaoRepository, InMemoryExecucaoRepository>();
                services.RemoveAll<IEventPublisher>();
                services.AddScoped<IEventPublisher, NoOpEventPublisher>();
            });
        });
    }

    [Fact]
    public async Task GetFila_SemCredenciais_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/fila");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFila_ComTokenJwtValido_Retorna200()
    {
        var client = _factory.CreateClient();
        var settings = _factory.Services.GetRequiredService<IJwtSettings>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.GerarToken(settings));

        var response = await client.GetAsync("/fila");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFila_ComApiKeyValida_Retorna200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", "execucao-internal-api-key-local-dev");

        var response = await client.GetAsync("/fila");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_SemCredenciais_ContinuaAberto()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
