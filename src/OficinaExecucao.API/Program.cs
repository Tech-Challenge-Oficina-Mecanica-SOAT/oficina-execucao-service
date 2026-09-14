using Serilog;
using Serilog.Formatting.Compact;
using Scalar.AspNetCore;
using System.Text;
using System.Text.Json.Serialization;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OficinaExecucao.API.Configuration;
using OficinaExecucao.API.Endpoints;
using OficinaExecucao.Application;
using OficinaExecucao.Application.Configuration;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain.Exceptions;
using OficinaExecucao.Infrastructure.Auth;
using OficinaExecucao.Infrastructure.DynamoDb;
using OficinaExecucao.Infrastructure.Messaging;

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
    sp => OficinaExecucao.Infrastructure.DynamoDb.AmazonDynamoDbClientFactory.Create(sp.GetRequiredService<IOptions<DynamoDbOptions>>().Value));
builder.Services.AddScoped<IExecucaoRepository, DynamoDbExecucaoRepository>();
builder.Services.Configure<SnsOptions>(builder.Configuration.GetSection("Sns"));
builder.Services.AddSingleton<Amazon.SimpleNotificationService.IAmazonSimpleNotificationService>(
    sp => AmazonSnsClientFactory.Create(sp.GetRequiredService<IOptions<SnsOptions>>().Value));
builder.Services.AddScoped<IEventPublisher, SnsEventPublisher>();
builder.Services.AddScoped<ListarFilaUseCase>();
builder.Services.AddScoped<ConsultarExecucaoUseCase>();
builder.Services.AddScoped<ObterHistoricoUseCase>();
builder.Services.AddScoped<RegistrarDiagnosticoUseCase>();
builder.Services.AddScoped<IniciarReparoUseCase>();
builder.Services.AddScoped<FinalizarUseCase>();

builder.Services.AddScoped<ProcessarOsCriadaUseCase>();
builder.Services.AddScoped<ProcessarOsCanceladaUseCase>();
builder.Services.AddScoped<ProcessarOrcamentoAprovadoUseCase>();
builder.Services.AddScoped<IMensagemDispatcher, MensagemDispatcher>();

builder.Services.Configure<SqsOptions>(builder.Configuration.GetSection("Sqs"));
builder.Services.AddSingleton<Amazon.SQS.IAmazonSQS>(
    sp => AmazonSqsClientFactory.Create(sp.GetRequiredService<IOptions<SqsOptions>>().Value));
builder.Services.AddHostedService<SqsConsumerBackgroundService>();

var jwtSettings = new JwtSettings(builder.Configuration);
builder.Services.AddSingleton<IJwtSettings>(jwtSettings);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuers = [jwtSettings.Issuer, jwtSettings.LambdaIssuer],
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };
    })
    .AddApiKeyInHeader<InternalApiKeyProvider>("ApiKey", options =>
    {
        options.Realm = "OficinaExecucao Internal API";
        options.KeyName = "X-Internal-Api-Key";
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme, "ApiKey")
        .RequireAuthenticatedUser()
        .Build();
});

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

app.UseAuthentication();
app.UseAuthorization();

app.MapExecucaoEndpoints();

app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
