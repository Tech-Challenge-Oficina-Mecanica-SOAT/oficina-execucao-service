using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OficinaExecucao.API.Contracts;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Tests.BDD.Support;
using Reqnroll;

namespace OficinaExecucao.Tests.BDD.StepDefinitions;

[Binding]
public class FilaSteps(ContextoBdd ctx)
{
    [Given("que o evento \"os.criada\" foi consumido para a OS")]
    public async Task DadoQueOEventoOsCriadaFoiConsumido()
    {
        ctx.OsId = Guid.NewGuid();
        using var scope = AmbienteBdd.Factory.Services.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<ProcessarOsCriadaUseCase>();
        await useCase.ExecutarAsync(ctx.OsId, Guid.NewGuid().ToString(), CancellationToken.None);
    }

    [When("consulto a execução da OS")]
    public async Task QuandoConsultoAExecucaoDaOs()
    {
        ctx.UltimaResposta = await ctx.Http.GetAsync($"/execucao/{ctx.OsId}");
    }

    [Then("o status da execução é {string}")]
    public async Task EntaoOStatusDaExecucaoE(string statusEsperado)
    {
        ctx.UltimaResposta!.StatusCode.Should().Be(HttpStatusCode.OK);
        var execucao = await ctx.UltimaResposta.Content.ReadFromJsonAsync<ExecucaoResponse>();
        execucao!.Status.Should().Be(statusEsperado);
    }
}
