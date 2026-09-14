using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OficinaExecucao.API.Contracts;
using OficinaExecucao.Application;
using OficinaExecucao.Application.Configuration;
using OficinaExecucao.Domain;
using OficinaExecucao.Tests.Integration.Auth;
using OficinaExecucao.Tests.Integration.Fakes;
using Xunit;

namespace OficinaExecucao.Tests.Integration.Api;

[Collection("Integration")]
public class ExecucaoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExecucaoEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExecucaoRepository>();
                services.AddSingleton<IExecucaoRepository, InMemoryExecucaoRepository>();
            });
        });
    }

    private HttpClient CriarClienteAutenticado()
    {
        var client = _factory.CreateClient();
        var settings = _factory.Services.GetRequiredService<IJwtSettings>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.GerarToken(settings));
        return client;
    }

    [Fact]
    public async Task PostDiagnostico_QuandoOsExiste_Retorna204EAtualizaStatus()
    {
        var client = CriarClienteAutenticado();
        var repositorio = (InMemoryExecucaoRepository)_factory.Services.CreateScope().ServiceProvider.GetRequiredService<IExecucaoRepository>();
        var osId = Guid.NewGuid();
        repositorio.Adicionar(Execucao.NovaNaFila(osId));

        var response = await client.PostAsJsonAsync($"/execucao/{osId}/diagnostico", new RegistrarDiagnosticoRequest
        {
            Pecas = [new ItemPecaDto { PecaId = Guid.NewGuid(), Quantidade = 1 }],
            Servicos = [new ItemServicoDto { ServicoId = Guid.NewGuid(), Quantidade = 1 }]
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var consulta = await client.GetFromJsonAsync<ExecucaoResponse>($"/execucao/{osId}");
        consulta!.Status.Should().Be(nameof(StatusExecucao.DiagnosticoConcluido));
    }

    [Fact]
    public async Task PostDiagnostico_QuandoOsNaoExiste_Retorna404()
    {
        var client = CriarClienteAutenticado();

        var response = await client.PostAsJsonAsync($"/execucao/{Guid.NewGuid()}/diagnostico", new RegistrarDiagnosticoRequest
        {
            Pecas = [],
            Servicos = []
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostFinalizar_QuandoNaoEstaEmReparo_Retorna409()
    {
        var client = CriarClienteAutenticado();
        var repositorio = (InMemoryExecucaoRepository)_factory.Services.CreateScope().ServiceProvider.GetRequiredService<IExecucaoRepository>();
        var osId = Guid.NewGuid();
        repositorio.Adicionar(Execucao.NovaNaFila(osId));

        var response = await client.PostAsJsonAsync($"/execucao/{osId}/finalizar", new FinalizarRequest { TempoRealHoras = 1m });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostDiagnostico_SemPecasNoBody_Retorna400()
    {
        var client = CriarClienteAutenticado();

        var response = await client.PostAsJsonAsync($"/execucao/{Guid.NewGuid()}/diagnostico", new { servicos = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetFila_RetornaOsAdicionadas()
    {
        var client = CriarClienteAutenticado();
        var repositorio = (InMemoryExecucaoRepository)_factory.Services.CreateScope().ServiceProvider.GetRequiredService<IExecucaoRepository>();
        var osId = Guid.NewGuid();
        repositorio.Adicionar(Execucao.NovaNaFila(osId));

        var itens = await client.GetFromJsonAsync<List<ItemFilaResponse>>("/fila?status=AguardandoDiagnostico");

        itens.Should().ContainSingle(i => i.OsId == osId);
    }

    [Fact]
    public async Task GetHistorico_DepoisDeUmaTransicao_RetornaUmaEntrada()
    {
        var client = CriarClienteAutenticado();
        var repositorio = (InMemoryExecucaoRepository)_factory.Services.CreateScope().ServiceProvider.GetRequiredService<IExecucaoRepository>();
        var osId = Guid.NewGuid();
        repositorio.Adicionar(Execucao.NovaNaFila(osId));
        await client.PostAsJsonAsync($"/execucao/{osId}/diagnostico", new RegistrarDiagnosticoRequest { Pecas = [], Servicos = [] });

        var historico = await client.GetFromJsonAsync<List<HistoricoExecucaoResponse>>($"/execucao/{osId}/historico");

        historico.Should().ContainSingle(h => h.StatusNovo == nameof(StatusExecucao.DiagnosticoConcluido));
    }
}
