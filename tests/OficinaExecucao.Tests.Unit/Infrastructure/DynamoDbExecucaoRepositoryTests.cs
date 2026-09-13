using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using OficinaExecucao.Domain;
using OficinaExecucao.Infrastructure.DynamoDb;
using Xunit;

namespace OficinaExecucao.Tests.Unit.Infrastructure;

public class DynamoDbExecucaoRepositoryTests
{
    private static IOptions<DynamoDbOptions> Options() => Microsoft.Extensions.Options.Options.Create(new DynamoDbOptions { TableName = "oficina-execucao-test" });

    [Fact]
    public async Task ObterPorOsIdAsync_QuandoNaoExiste_RetornaNull()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });

        var repositorio = new DynamoDbExecucaoRepository(client.Object, Options());
        var resultado = await repositorio.ObterPorOsIdAsync(Guid.NewGuid(), CancellationToken.None);

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task ObterPorOsIdAsync_QuandoExiste_MapeiaOsCamposDeVolta()
    {
        var osId = Guid.NewGuid();
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new($"OS#{osId}"),
                    ["SK"] = new("STATUS"),
                    ["status"] = new(nameof(StatusExecucao.EmReparo)),
                    ["pecas"] = new("[]"),
                    ["servicos"] = new("[]"),
                    ["adicionadaEm"] = new(DateTimeOffset.UtcNow.ToString("O"))
                }
            });

        var repositorio = new DynamoDbExecucaoRepository(client.Object, Options());
        var resultado = await repositorio.ObterPorOsIdAsync(osId, CancellationToken.None);

        resultado.Should().NotBeNull();
        resultado!.OsId.Should().Be(osId);
        resultado.Status.Should().Be(StatusExecucao.EmReparo);
    }

    [Fact]
    public async Task SalvarAsync_EnviaUmUnicoTransactWriteComOsDoisItens()
    {
        var osId = Guid.NewGuid();
        var execucao = Execucao.NovaNaFila(osId);
        var historico = new HistoricoEntry(StatusExecucao.AguardandoDiagnostico, StatusExecucao.DiagnosticoConcluido, DateTimeOffset.UtcNow, "api");
        TransactWriteItemsRequest? capturado = null;
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((req, _) => capturado = req)
            .ReturnsAsync(new TransactWriteItemsResponse());

        var repositorio = new DynamoDbExecucaoRepository(client.Object, Options());
        await repositorio.SalvarAsync(execucao, historico, CancellationToken.None);

        capturado.Should().NotBeNull();
        capturado!.TransactItems.Should().HaveCount(2);
        capturado.TransactItems.Should().Contain(i => i.Put.Item["SK"].S == "STATUS");
        capturado.TransactItems.Should().Contain(i => i.Put.Item["SK"].S.StartsWith("HISTORICO#") && !i.Put.Item["SK"].S.StartsWith("HISTORICO#EVT#"));
    }

    [Fact]
    public async Task EventoJaProcessadoAsync_QuandoItemExiste_RetornaTrue()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue> { ["PK"] = new("x"), ["SK"] = new("HISTORICO#EVT#abc") } });

        var repositorio = new DynamoDbExecucaoRepository(client.Object, Options());
        var resultado = await repositorio.EventoJaProcessadoAsync(Guid.NewGuid(), "abc", CancellationToken.None);

        resultado.Should().BeTrue();
    }
}
