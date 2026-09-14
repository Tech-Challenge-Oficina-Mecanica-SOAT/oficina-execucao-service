using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Testcontainers.LocalStack;
using Xunit;

namespace OficinaExecucao.Tests.E2E;

public sealed class LocalStackFixture : IAsyncLifetime
{
    private LocalStackContainer? _container;

    public string ServiceUrl { get; private set; } = string.Empty;
    public string TableName { get; } = "oficina-execucao-e2e";
    public string QueueUrl { get; private set; } = string.Empty;
    public string DiagnosticoConcluidoTopicArn { get; private set; } = string.Empty;
    public string ExecucaoIniciadaTopicArn { get; private set; } = string.Empty;
    public string ExecucaoFinalizadaTopicArn { get; private set; } = string.Empty;
    public string VerificacaoQueueUrl { get; private set; } = string.Empty;
    public string OsCriadaTopicArn { get; private set; } = string.Empty;
    public string OsCanceladaTopicArn { get; private set; } = string.Empty;
    public string OrcamentoAprovadoTopicArn { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new LocalStackBuilder()
            .WithImage("localstack/localstack:3.0")
            .Build();
        await _container.StartAsync();
        ServiceUrl = _container.GetConnectionString();

        var credentials = new BasicAWSCredentials("test", "test");

        using var dynamoClient = new AmazonDynamoDBClient(credentials, new AmazonDynamoDBConfig { ServiceURL = ServiceUrl, UseHttp = true });
        await CriarTabelaAsync(dynamoClient);

        using var sqsClient = new AmazonSQSClient(credentials, new AmazonSQSConfig { ServiceURL = ServiceUrl, UseHttp = true });
        using var snsClient = new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig { ServiceURL = ServiceUrl, UseHttp = true });

        var (queueUrl, queueArn) = await CriarFilaComDlqAsync(sqsClient, "oficina-execucao-queue", "oficina-execucao-dlq");
        QueueUrl = queueUrl;

        var (verifQueueUrl, verifQueueArn) = await CriarFilaSimplesAsync(sqsClient, "oficina-execucao-verificacao-queue");
        VerificacaoQueueUrl = verifQueueUrl;

        DiagnosticoConcluidoTopicArn = await CriarTopicoEAssinarAsync(snsClient, "diagnostico-concluido", verifQueueArn);
        ExecucaoIniciadaTopicArn = await CriarTopicoEAssinarAsync(snsClient, "execucao-iniciada", verifQueueArn);
        ExecucaoFinalizadaTopicArn = await CriarTopicoEAssinarAsync(snsClient, "execucao-finalizada", verifQueueArn);

        OsCriadaTopicArn = await CriarTopicoEAssinarAsync(snsClient, "os-criada", queueArn);
        OsCanceladaTopicArn = await CriarTopicoEAssinarAsync(snsClient, "os-cancelada", queueArn);
        OrcamentoAprovadoTopicArn = await CriarTopicoEAssinarAsync(snsClient, "orcamento-aprovado", queueArn);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private async Task CriarTabelaAsync(IAmazonDynamoDB client)
    {
        await client.CreateTableAsync(new CreateTableRequest
        {
            TableName = TableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            KeySchema =
            [
                new KeySchemaElement("PK", KeyType.HASH),
                new KeySchemaElement("SK", KeyType.RANGE)
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition("PK", ScalarAttributeType.S),
                new AttributeDefinition("SK", ScalarAttributeType.S),
                new AttributeDefinition("status", ScalarAttributeType.S),
                new AttributeDefinition("adicionadaEm", ScalarAttributeType.S)
            ],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = "status-index",
                    KeySchema =
                    [
                        new KeySchemaElement("status", KeyType.HASH),
                        new KeySchemaElement("adicionadaEm", KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                }
            ]
        });
    }

    private static async Task<(string QueueUrl, string QueueArn)> CriarFilaComDlqAsync(IAmazonSQS client, string queueName, string dlqName)
    {
        var dlq = await client.CreateQueueAsync(new CreateQueueRequest { QueueName = dlqName });
        var dlqAttrs = await client.GetQueueAttributesAsync(dlq.QueueUrl, ["QueueArn"]);

        var queue = await client.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = queueName,
            Attributes = new Dictionary<string, string>
            {
                ["RedrivePolicy"] = JsonSerializer.Serialize(new { deadLetterTargetArn = dlqAttrs.QueueARN, maxReceiveCount = "3" })
            }
        });
        var queueAttrs = await client.GetQueueAttributesAsync(queue.QueueUrl, ["QueueArn"]);

        return (queue.QueueUrl, queueAttrs.QueueARN);
    }

    private static async Task<(string QueueUrl, string QueueArn)> CriarFilaSimplesAsync(IAmazonSQS client, string queueName)
    {
        var queue = await client.CreateQueueAsync(new CreateQueueRequest { QueueName = queueName });
        var queueAttrs = await client.GetQueueAttributesAsync(queue.QueueUrl, ["QueueArn"]);

        return (queue.QueueUrl, queueAttrs.QueueARN);
    }

    private static async Task<string> CriarTopicoEAssinarAsync(IAmazonSimpleNotificationService client, string topicName, string queueArn)
    {
        var topic = await client.CreateTopicAsync(topicName);

        await client.SubscribeAsync(new SubscribeRequest
        {
            TopicArn = topic.TopicArn,
            Protocol = "sqs",
            Endpoint = queueArn,
            Attributes = new Dictionary<string, string> { ["RawMessageDelivery"] = "true" }
        });

        return topic.TopicArn;
    }
}
