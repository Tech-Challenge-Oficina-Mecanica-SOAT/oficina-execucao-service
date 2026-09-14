using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using OficinaExecucao.Application;
using OficinaExecucao.Domain;

namespace OficinaExecucao.Infrastructure.DynamoDb;

public sealed class DynamoDbExecucaoRepository(IAmazonDynamoDB client, IOptions<DynamoDbOptions> options) : IExecucaoRepository
{
    private string TableName => options.Value.TableName;

    private static string Pk(Guid osId) => $"OS#{osId}";

    public async Task<Execucao?> ObterPorOsIdAsync(Guid osId, CancellationToken ct)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new(Pk(osId)),
                ["SK"] = new("STATUS")
            }
        }, ct);

        if (response?.Item is null || response.Item.Count == 0)
            return null;

        return MapParaExecucao(osId, response.Item);
    }

    public async Task SalvarAsync(Execucao execucao, HistoricoEntry novaEntrada, CancellationToken ct, string? eventId = null)
    {
        var jaExistia = await ObterPorOsIdAsync(execucao.OsId, ct) is not null;

        var statusItem = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(Pk(execucao.OsId)),
            ["SK"] = new("STATUS"),
            ["status"] = new(execucao.Status.ToString()),
            ["pecas"] = new(JsonSerializer.Serialize(execucao.Pecas)),
            ["servicos"] = new(JsonSerializer.Serialize(execucao.Servicos)),
            ["adicionadaEm"] = new(jaExistia ? await ObterAdicionadaEmAsync(execucao.OsId, ct) : DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
        };

        if (execucao.DiagnosticoId is { } diagnosticoId) statusItem["diagnosticoId"] = new(diagnosticoId.ToString());
        if (execucao.ExecucaoId is { } execucaoId) statusItem["execucaoId"] = new(execucaoId.ToString());
        if (execucao.TempoEstimadoHoras is { } tempoEstimado) statusItem["tempoEstimadoHoras"] = new() { N = tempoEstimado.ToString(CultureInfo.InvariantCulture) };
        if (execucao.Observacoes is { } observacoes) statusItem["observacoes"] = new(observacoes);
        if (execucao.IniciadaEm is { } iniciadaEm) statusItem["iniciadaEm"] = new(iniciadaEm.ToString("O", CultureInfo.InvariantCulture));
        if (execucao.FinalizadaEm is { } finalizadaEm) statusItem["finalizadaEm"] = new(finalizadaEm.ToString("O", CultureInfo.InvariantCulture));
        if (execucao.TempoRealHoras is { } tempoReal) statusItem["tempoRealHoras"] = new() { N = tempoReal.ToString(CultureInfo.InvariantCulture) };

        var mudouEmFormatado = novaEntrada.MudouEm.ToString("O", CultureInfo.InvariantCulture);
        var historicoItem = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(Pk(execucao.OsId)),
            ["SK"] = new($"HISTORICO#{mudouEmFormatado}"),
            ["statusAnterior"] = new(novaEntrada.StatusAnterior.ToString()),
            ["statusNovo"] = new(novaEntrada.StatusNovo.ToString()),
            ["mudouEm"] = new(mudouEmFormatado),
            ["origem"] = new(novaEntrada.Origem)
        };

        var transactItems = new List<TransactWriteItem>
        {
            new() { Put = new Put { TableName = TableName, Item = statusItem } },
            new() { Put = new Put { TableName = TableName, Item = historicoItem } }
        };

        if (eventId is not null)
        {
            transactItems.Add(new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = TableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["PK"] = new(Pk(execucao.OsId)),
                        ["SK"] = new($"HISTORICO#EVT#{eventId}"),
                        ["processadoEm"] = new(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                    }
                }
            });
        }

        await client.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = transactItems }, ct);
    }

    public async Task<IReadOnlyList<HistoricoEntry>> ObterHistoricoAsync(Guid osId, CancellationToken ct)
    {
        var response = await client.QueryAsync(new QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :prefixo)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new(Pk(osId)),
                [":prefixo"] = new("HISTORICO#")
            }
        }, ct);

        return response.Items
            .Where(item => !item["SK"].S.StartsWith("HISTORICO#EVT#"))
            .Select(item => new HistoricoEntry(
                Enum.Parse<StatusExecucao>(item["statusAnterior"].S),
                Enum.Parse<StatusExecucao>(item["statusNovo"].S),
                DateTimeOffset.Parse(item["mudouEm"].S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                item["origem"].S))
            .ToList();
    }

    public async Task<IReadOnlyList<Execucao>> ListarPorStatusAsync(StatusExecucao status, CancellationToken ct)
    {
        var response = await client.QueryAsync(new QueryRequest
        {
            TableName = TableName,
            IndexName = "status-index",
            KeyConditionExpression = "#status = :status",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":status"] = new(status.ToString()) }
        }, ct);

        return response.Items
            .Select(item => MapParaExecucao(Guid.Parse(item["PK"].S.Replace("OS#", string.Empty)), item))
            .ToList();
    }

    public async Task<bool> EventoJaProcessadoAsync(Guid osId, string eventId, CancellationToken ct)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new(Pk(osId)),
                ["SK"] = new($"HISTORICO#EVT#{eventId}")
            }
        }, ct);

        return response.Item is { Count: > 0 };
    }

    private async Task<string> ObterAdicionadaEmAsync(Guid osId, CancellationToken ct)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue> { ["PK"] = new(Pk(osId)), ["SK"] = new("STATUS") }
        }, ct);

        return response.Item["adicionadaEm"].S;
    }

    private static Execucao MapParaExecucao(Guid osId, Dictionary<string, AttributeValue> item)
    {
        return Execucao.Reidratar(
            osId,
            Enum.Parse<StatusExecucao>(item["status"].S),
            item.TryGetValue("diagnosticoId", out var diagnosticoId) ? Guid.Parse(diagnosticoId.S) : null,
            item.TryGetValue("execucaoId", out var execucaoId) ? Guid.Parse(execucaoId.S) : null,
            item.TryGetValue("pecas", out var pecas) ? JsonSerializer.Deserialize<IReadOnlyList<ItemPeca>>(pecas.S)! : Array.Empty<ItemPeca>(),
            item.TryGetValue("servicos", out var servicos) ? JsonSerializer.Deserialize<IReadOnlyList<ItemServico>>(servicos.S)! : Array.Empty<ItemServico>(),
            item.TryGetValue("tempoEstimadoHoras", out var tempoEstimado) ? decimal.Parse(tempoEstimado.N, CultureInfo.InvariantCulture) : null,
            item.TryGetValue("observacoes", out var observacoes) ? observacoes.S : null,
            item.TryGetValue("iniciadaEm", out var iniciadaEm) ? DateTimeOffset.Parse(iniciadaEm.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null,
            item.TryGetValue("finalizadaEm", out var finalizadaEm) ? DateTimeOffset.Parse(finalizadaEm.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null,
            item.TryGetValue("tempoRealHoras", out var tempoReal) ? decimal.Parse(tempoReal.N, CultureInfo.InvariantCulture) : null,
            DateTimeOffset.Parse(item["adicionadaEm"].S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }
}
