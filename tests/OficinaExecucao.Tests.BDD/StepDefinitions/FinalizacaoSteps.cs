using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OficinaExecucao.API.Contracts;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Tests.BDD.Support;
using Reqnroll;

namespace OficinaExecucao.Tests.BDD.StepDefinitions;

[Binding]
public class FinalizacaoSteps(ContextoBdd ctx)
{
    [Given("uma execução em reparo")]
    public async Task DadoUmaExecucaoEmReparo()
    {
        ctx.OsId = Guid.NewGuid();

        using var scope = AmbienteBdd.Factory.Services.CreateScope();

        var processarOsCriada = scope.ServiceProvider.GetRequiredService<ProcessarOsCriadaUseCase>();
        await processarOsCriada.ExecutarAsync(ctx.OsId, Guid.NewGuid().ToString(), CancellationToken.None);

        var diagnostico = await ctx.Http.PostAsJsonAsync($"/execucao/{ctx.OsId}/diagnostico", new RegistrarDiagnosticoRequest
        {
            Pecas = [new ItemPecaDto { PecaId = Guid.NewGuid(), Quantidade = 1 }],
            Servicos = [new ItemServicoDto { ServicoId = Guid.NewGuid(), Quantidade = 1 }]
        });
        diagnostico.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var processarOrcamentoAprovado = scope.ServiceProvider.GetRequiredService<ProcessarOrcamentoAprovadoUseCase>();
        await processarOrcamentoAprovado.ExecutarAsync(ctx.OsId, Guid.NewGuid().ToString(), CancellationToken.None);

        var iniciarReparo = await ctx.Http.PostAsync($"/execucao/{ctx.OsId}/iniciar-reparo", content: null);
        iniciarReparo.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [When("finalizo o reparo informando {int} horas de trabalho")]
    public async Task QuandoFinalizoOReparo(int horas)
    {
        ctx.UltimaResposta = await ctx.Http.PostAsJsonAsync($"/execucao/{ctx.OsId}/finalizar", new FinalizarRequest
        {
            TempoRealHoras = horas
        });
    }

    [Then("o evento \"execucao.finalizada\" é publicado")]
    public void EntaoOEventoExecucaoFinalizadaEPublicado()
    {
        ctx.UltimaResposta!.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var evento = AmbienteBdd.Factory.Publisher.Publicados.FirstOrDefault(e =>
            e.EventType == "execucao.finalizada" &&
            JsonSerializer.Serialize(e.Data).Contains(ctx.OsId.ToString()));

        evento.Should().NotBeNull();
    }

    [Then("o histórico da execução mostra a transição para {string}")]
    public async Task EntaoOHistoricoMostraATransicaoPara(string statusEsperado)
    {
        var historico = await ctx.Http.GetFromJsonAsync<List<HistoricoExecucaoResponse>>($"/execucao/{ctx.OsId}/historico");

        historico!.Should().Contain(h => h.StatusNovo == statusEsperado);
    }
}
