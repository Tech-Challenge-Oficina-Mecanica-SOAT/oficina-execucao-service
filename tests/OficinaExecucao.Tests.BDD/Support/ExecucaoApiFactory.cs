using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OficinaExecucao.Application;

namespace OficinaExecucao.Tests.BDD.Support;

/// <summary>A API inteira em memória: repositório DynamoDB e publicador SNS trocados pelos fakes
/// acima, consumer SQS desligado. Os cenários acionam os endpoints REST reais.</summary>
public sealed class ExecucaoApiFactory : WebApplicationFactory<Program>
{
    public InMemoryExecucaoRepository Repositorio { get; } = new();
    public CapturingEventPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IExecucaoRepository>();
            services.AddSingleton<IExecucaoRepository>(Repositorio);
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(Publisher);
        });
    }
}
