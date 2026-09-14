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

    public Task InitializeAsync()
    {
        var credentials = new BasicAWSCredentials("test", "test");
        _snsClient = new AmazonSimpleNotificationServiceClient(credentials, new Amazon.SimpleNotificationService.AmazonSimpleNotificationServiceConfig { ServiceURL = LocalStack.ServiceUrl, UseHttp = true });
        _sqsClient = new AmazonSQSClient(credentials, new Amazon.SQS.AmazonSQSConfig { ServiceURL = LocalStack.ServiceUrl, UseHttp = true });

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

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        _snsClient?.Dispose();
        _sqsClient?.Dispose();
        return Task.CompletedTask;
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
    /// Poll-until-true helper. Deliberately NOT generic over a return value: an earlier
    /// draft of this helper was generic (`AguardarAsync&lt;T&gt;` returning `T`), constrained
    /// `where T : class` — which breaks the moment a caller needs to poll for a `bool` or a
    /// `JsonElement` result (both value types), since `T?` for a `class`-constrained `T`
    /// doesn't accept structs, and unwrapping `Nullable&lt;T&gt;` back to `T` inside the method
    /// doesn't compile generically either. Keeping this non-generic (just "is the condition
    /// met yet") sidesteps the whole problem: callers fetch whatever they need INSIDE the
    /// predicate and, if they need the fetched value for assertions afterward, do one more
    /// direct call after `AguardarAsync` returns (the condition already proved it will
    /// succeed by then).
    /// </summary>
    protected static async Task AguardarAsync(Func<Task<bool>> condicaoAsync, TimeSpan? timeout = null)
    {
        var limite = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(40));

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
