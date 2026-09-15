using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaExecucao.Application.Configuration;

namespace OficinaExecucao.Tests.E2E;

public abstract class E2ETestBase : IAsyncLifetime
{
    protected readonly LocalStackFixture LocalStack;
    protected HttpClient Client { get; private set; } = null!;

    private WebApplicationFactory<Program>? _factory;
    private AmazonSimpleNotificationServiceClient? _snsClient;
    private AmazonSQSClient? _sqsClient;

    protected E2ETestBase(LocalStackFixture localStack)
    {
        LocalStack = localStack;
    }

    public async Task InitializeAsync()
    {
        var credentials = new BasicAWSCredentials("test", "test");
        _snsClient = new AmazonSimpleNotificationServiceClient(credentials, new Amazon.SimpleNotificationService.AmazonSimpleNotificationServiceConfig { ServiceURL = LocalStack.ServiceUrl, UseHttp = true });
        _sqsClient = new AmazonSQSClient(credentials, new Amazon.SQS.AmazonSQSConfig { ServiceURL = LocalStack.ServiceUrl, UseHttp = true });

        // Drain any message left behind by a previously-failed test in this run: a test that
        // fails mid-sequence can leave its already-published event unread on the shared
        // verification queue, which the NEXT test's LerProximaMensagemDaFilaDeVerificacaoAsync
        // call would then misread as its own. Real AWS enforces a 60s cooldown between
        // PurgeQueue calls on the same queue; if LocalStack does too, swallow it — a briefly
        // non-empty queue is exactly the pre-existing risk, but a hard failure here would be worse.
        try
        {
            await _sqsClient.PurgeQueueAsync(LocalStack.VerificacaoQueueUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Aviso: falha ao purgar a fila de verificação (seguindo mesmo assim): {ex.Message}");
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DynamoDb:ServiceUrl"] = LocalStack.ServiceUrl,
                    ["DynamoDb:TableName"] = LocalStack.TableName,
                    ["Sqs:ServiceUrl"] = LocalStack.ServiceUrl,
                    ["Sqs:QueueUrl"] = LocalStack.QueueUrl,
                    ["Sns:ServiceUrl"] = LocalStack.ServiceUrl,
                    ["Sns:TopicArns:diagnostico.concluido"] = LocalStack.DiagnosticoConcluidoTopicArn,
                    ["Sns:TopicArns:execucao.iniciada"] = LocalStack.ExecucaoIniciadaTopicArn,
                    ["Sns:TopicArns:execucao.finalizada"] = LocalStack.ExecucaoFinalizadaTopicArn
                });
            });
        });

        var settings = _factory.Services.GetRequiredService<IJwtSettings>();
        Client = _factory.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarToken(settings));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        _snsClient?.Dispose();
        _sqsClient?.Dispose();
    }

    private static string GerarToken(IJwtSettings settings)
    {
        var credenciais = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey)),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "teste-e2e")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credenciais);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    protected async Task PublicarEventoFakeAsync(string topicArn, string eventType, object data, string? eventId = null)
    {
        var envelope = new
        {
            eventId = eventId ?? Guid.NewGuid().ToString(),
            eventType,
            eventVersion = "1.0",
            occurredAt = DateTimeOffset.UtcNow.ToString("O"),
            correlationId = Guid.NewGuid().ToString(),
            producer = "teste-e2e",
            data
        };

        await _snsClient!.PublishAsync(new PublishRequest
        {
            TopicArn = topicArn,
            Message = JsonSerializer.Serialize(envelope)
        });
    }

    /// <summary>
    /// Poll-until-true helper. Deliberately NOT generic over a return value: callers fetch
    /// whatever they need INSIDE the predicate, and do one more direct call after
    /// <see cref="AguardarAsync"/> returns if they need that value for assertions.
    ///
    /// The 35s default is sized to absorb LocalStack's SNS-&gt;SQS cold-start latency, which
    /// <see cref="LocalStackFixture"/>'s one-time warm-up already pays for once per test run.
    /// If that warm-up is ever removed, this default needs to go back up.
    /// </summary>
    protected static async Task AguardarAsync(Func<Task<bool>> condicaoAsync, TimeSpan? timeout = null)
    {
        var limite = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(35));

        while (DateTimeOffset.UtcNow < limite)
        {
            if (await condicaoAsync())
                return;

            await Task.Delay(200);
        }

        throw new TimeoutException("Condição não satisfeita dentro do tempo limite.");
    }

    protected async Task<JsonDocument> LerProximaMensagemDaFilaDeVerificacaoAsync(TimeSpan? timeout = null)
    {
        var limite = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(10));

        while (DateTimeOffset.UtcNow < limite)
        {
            var response = await _sqsClient!.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = LocalStack.VerificacaoQueueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 2
            });

            if (response.Messages is { Count: > 0 } mensagens)
            {
                await _sqsClient.DeleteMessageAsync(LocalStack.VerificacaoQueueUrl, mensagens[0].ReceiptHandle);
                return JsonDocument.Parse(mensagens[0].Body);
            }
        }

        throw new TimeoutException("Nenhuma mensagem chegou na fila de verificação dentro do tempo limite.");
    }
}
