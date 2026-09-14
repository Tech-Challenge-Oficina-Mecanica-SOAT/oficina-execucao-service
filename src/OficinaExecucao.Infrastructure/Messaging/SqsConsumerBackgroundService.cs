using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OficinaExecucao.Application;

namespace OficinaExecucao.Infrastructure.Messaging;

public sealed class SqsConsumerBackgroundService(
    IAmazonSQS client,
    IOptions<SqsOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<SqsConsumerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessarLoteAsync(stoppingToken);
        }
    }

    public async Task ProcessarLoteAsync(CancellationToken ct)
    {
        ReceiveMessageResponse response;
        try
        {
            response = await client.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = options.Value.QueueUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 20
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao receber mensagens da fila; nova tentativa em 5s.");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return;
        }

        foreach (var mensagem in response.Messages ?? [])
        {
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IMensagemDispatcher>();

            try
            {
                await dispatcher.ProcessarAsync(mensagem.Body, ct);
                await client.DeleteMessageAsync(options.Value.QueueUrl, mensagem.ReceiptHandle, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar mensagem {MessageId}, será reprocessada.", mensagem.MessageId);
            }
        }
    }
}
