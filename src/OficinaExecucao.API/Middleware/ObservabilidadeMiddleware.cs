using System.Diagnostics;
using Microsoft.AspNetCore.Routing;
using OficinaExecucao.Application.Observabilidade;

namespace OficinaExecucao.API.Middleware;

/// <summary>
/// Observabilidade das requisições HTTP:
///  - health checks não geram transação no APM;
///  - a transação recebe o nome do template da rota (nunca o osId concreto);
///  - atributos customizados osId e correlationId (trace id W3C, o mesmo dos logs);
///  - escopo de log com correlationId e osId.
/// Deve ser registrado depois de UseRouting (precisa do endpoint casado).
/// </summary>
public class ObservabilidadeMiddleware(RequestDelegate next, IObservabilidade observabilidade, ILogger<ObservabilidadeMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (NomeTransacao.DeveIgnorar(context.Request.Path.Value))
        {
            observabilidade.IgnorarTransacao();
            await next(context);
            return;
        }

        if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } template })
            observabilidade.NomearTransacaoWeb(NomeTransacao.Web(context.Request.Method, template));

        var correlationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        observabilidade.AdicionarAtributo("correlationId", correlationId);

        var escopo = new Dictionary<string, object> { ["correlationId"] = correlationId };
        if (context.Request.RouteValues["osId"]?.ToString() is { Length: > 0 } osId)
        {
            observabilidade.AdicionarAtributo("osId", osId);
            escopo["osId"] = osId;
        }

        using (logger.BeginScope(escopo))
        {
            await next(context);
        }
    }
}
