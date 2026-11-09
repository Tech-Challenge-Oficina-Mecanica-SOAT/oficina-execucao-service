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
public class DiagnosticoSteps(ContextoBdd ctx)
{
    private Guid _pecaId;
    private Guid _servicoId;

    [Given("uma execução na fila aguardando diagnóstico")]
    public async Task DadoUmaExecucaoNaFilaAguardandoDiagnostico()
    {
        ctx.OsId = Guid.NewGuid();
        using var scope = AmbienteBdd.Factory.Services.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<ProcessarOsCriadaUseCase>();
        await useCase.ExecutarAsync(ctx.OsId, Guid.NewGuid().ToString(), CancellationToken.None);
    }

    [When("registro o diagnóstico com 1 peça e 1 serviço")]
    public async Task QuandoRegistroODiagnostico()
    {
        _pecaId = Guid.NewGuid();
        _servicoId = Guid.NewGuid();

        ctx.UltimaResposta = await ctx.Http.PostAsJsonAsync($"/execucao/{ctx.OsId}/diagnostico", new RegistrarDiagnosticoRequest
        {
            Pecas = [new ItemPecaDto { PecaId = _pecaId, Quantidade = 1 }],
            Servicos = [new ItemServicoDto { ServicoId = _servicoId, Quantidade = 1 }]
        });
    }

    [Then("o evento \"diagnostico.concluido\" é publicado")]
    public void EntaoOEventoDiagnosticoConcluidoEPublicado()
    {
        ctx.UltimaResposta!.StatusCode.Should().Be(HttpStatusCode.NoContent);
        EventoPublicado().Should().NotBeNull();
    }

    [Then("o evento publicado contém as peças e serviços informados")]
    public void EntaoOEventoContemAsPecasEServicos()
    {
        var evento = EventoPublicado()!;
        var json = JsonSerializer.Serialize(evento.Data);
        json.Should().Contain(_pecaId.ToString());
        json.Should().Contain(_servicoId.ToString());
    }

    private CapturingEventPublisher.EventoPublicado? EventoPublicado() =>
        AmbienteBdd.Factory.Publisher.Publicados.FirstOrDefault(e =>
            e.EventType == "diagnostico.concluido" &&
            JsonSerializer.Serialize(e.Data).Contains(ctx.OsId.ToString()));
}
