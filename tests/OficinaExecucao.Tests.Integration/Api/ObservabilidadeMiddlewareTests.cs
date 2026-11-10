using System.Net;
using System.Net.Http.Headers;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using OficinaExecucao.Application;
using OficinaExecucao.Application.Configuration;
using OficinaExecucao.Application.Observabilidade;
using OficinaExecucao.Tests.Integration.Auth;
using OficinaExecucao.Tests.Integration.Fakes;
using Xunit;

namespace OficinaExecucao.Tests.Integration.Api;

[Collection("Integration")]
public class ObservabilidadeMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class Gravador : IObservabilidade
    {
        public List<string> Nomes { get; } = [];
        public int Ignoradas { get; private set; }
        public Dictionary<string, string> Atributos { get; } = [];

        public void NomearTransacaoWeb(string nome) => Nomes.Add(nome);
        public void IgnorarTransacao() => Ignoradas++;
        public void AdicionarAtributo(string chave, string valor) => Atributos[chave] = valor;
        public Task ProcessarMensagemAsync(ContextoMensagem contexto, Func<Task> processamento) => processamento();
    }

    private readonly Gravador _gravador = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ObservabilidadeMiddlewareTests(WebApplicationFactory<Program> factory)
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
                services.RemoveAll<IObservabilidade>();
                services.AddSingleton<IObservabilidade>(_gravador);

                var dynamoDbMock = new Mock<IAmazonDynamoDB>();
                dynamoDbMock
                    .Setup(c => c.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = TableStatus.ACTIVE } });
                services.RemoveAll<IAmazonDynamoDB>();
                services.AddSingleton(dynamoDbMock.Object);
            });
        });
    }

    private HttpClient ClienteAutenticado()
    {
        var client = _factory.CreateClient();
        var settings = _factory.Services.GetRequiredService<IJwtSettings>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.GerarToken(settings));
        return client;
    }

    [Fact]
    public async Task RotaComOsId_NomeiaPeloTemplateEAdicionaAtributos()
    {
        var osId = Guid.NewGuid();

        await ClienteAutenticado().GetAsync($"/execucao/{osId}");

        _gravador.Nomes.Should().ContainSingle().Which.Should().Be("GET /execucao/{osId}");
        _gravador.Atributos["osId"].Should().Be(osId.ToString());
        _gravador.Atributos.Should().ContainKey("correlationId");
        _gravador.Ignoradas.Should().Be(0);
    }

    [Fact]
    public async Task RotaSemOsId_NomeiaPeloTemplateSemAtributoOsId()
    {
        await ClienteAutenticado().GetAsync("/fila");

        _gravador.Nomes.Should().ContainSingle().Which.Should().Be("GET /fila");
        _gravador.Atributos.Should().NotContainKey("osId");
        _gravador.Atributos.Should().ContainKey("correlationId");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthChecks_SaoIgnoradosPeloApm(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _gravador.Ignoradas.Should().Be(1);
        _gravador.Nomes.Should().BeEmpty();
    }
}
