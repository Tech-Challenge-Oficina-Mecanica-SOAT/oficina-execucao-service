using Serilog;
using Serilog.Formatting.Compact;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using OficinaExecucao.API.Endpoints;
using OficinaExecucao.Application;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain.Exceptions;
using OficinaExecucao.Infrastructure.DynamoDb;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<DynamoDbOptions>(builder.Configuration.GetSection("DynamoDb"));
builder.Services.AddSingleton<Amazon.DynamoDBv2.IAmazonDynamoDB>(
    _ => new Amazon.DynamoDBv2.AmazonDynamoDBClient(Amazon.RegionEndpoint.USEast1));
builder.Services.AddScoped<IExecucaoRepository, DynamoDbExecucaoRepository>();
builder.Services.AddScoped<IEventPublisher, NoOpEventPublisher>();
builder.Services.AddScoped<ListarFilaUseCase>();
builder.Services.AddScoped<ConsultarExecucaoUseCase>();
builder.Services.AddScoped<ObterHistoricoUseCase>();
builder.Services.AddScoped<RegistrarDiagnosticoUseCase>();
builder.Services.AddScoped<IniciarReparoUseCase>();
builder.Services.AddScoped<FinalizarUseCase>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseExceptionHandler(handlerApp => handlerApp.Run(async context =>
{
    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    if (feature?.Error is TransicaoInvalidaException ex)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { code = "transicao_invalida", message = ex.Message });
        return;
    }

    if (feature?.Error is Microsoft.AspNetCore.Http.BadHttpRequestException badRequestEx)
    {
        context.Response.StatusCode = badRequestEx.StatusCode;
        await context.Response.WriteAsJsonAsync(new { code = "requisicao_invalida", message = badRequestEx.Message });
        return;
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
}));

app.MapExecucaoEndpoints();

app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
