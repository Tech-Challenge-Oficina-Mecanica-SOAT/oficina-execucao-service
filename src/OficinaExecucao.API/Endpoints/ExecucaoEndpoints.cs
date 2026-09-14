using OficinaExecucao.API.Contracts;
using OficinaExecucao.Application.UseCases;
using OficinaExecucao.Domain;

namespace OficinaExecucao.API.Endpoints;

public static class ExecucaoEndpoints
{
    public static void MapExecucaoEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("").RequireAuthorization();

        grupo.MapGet("/fila", async (StatusExecucao? status, ListarFilaUseCase useCase, CancellationToken ct) =>
        {
            var itens = await useCase.ExecutarAsync(status, ct);
            return Results.Ok(itens.Select(e => new ItemFilaResponse(e.OsId, e.Status.ToString(), DateTimeOffset.UtcNow)));
        });

        grupo.MapPost("/execucao/{osId:guid}/diagnostico", async (Guid osId, RegistrarDiagnosticoRequest request, RegistrarDiagnosticoUseCase useCase, CancellationToken ct) =>
        {
            var pecas = request.Pecas.Select(p => new ItemPeca(p.PecaId, p.Quantidade)).ToList();
            var servicos = request.Servicos.Select(s => new ItemServico(s.ServicoId, s.Quantidade)).ToList();

            try
            {
                await useCase.ExecutarAsync(osId, pecas, servicos, request.TempoEstimadoHoras, request.Observacoes, ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        grupo.MapPost("/execucao/{osId:guid}/iniciar-reparo", async (Guid osId, IniciarReparoUseCase useCase, CancellationToken ct) =>
        {
            try
            {
                await useCase.ExecutarAsync(osId, ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        grupo.MapPost("/execucao/{osId:guid}/finalizar", async (Guid osId, FinalizarRequest request, FinalizarUseCase useCase, CancellationToken ct) =>
        {
            try
            {
                await useCase.ExecutarAsync(osId, request.TempoRealHoras ?? 0m, ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        grupo.MapGet("/execucao/{osId:guid}", async (Guid osId, ConsultarExecucaoUseCase useCase, CancellationToken ct) =>
        {
            var execucao = await useCase.ExecutarAsync(osId, ct);
            if (execucao is null)
                return Results.NotFound();

            return Results.Ok(new ExecucaoResponse(
                execucao.OsId,
                execucao.Status.ToString(),
                execucao.Pecas.Select(p => new ItemPecaDto { PecaId = p.PecaId, Quantidade = p.Quantidade }).ToList(),
                execucao.Servicos.Select(s => new ItemServicoDto { ServicoId = s.ServicoId, Quantidade = s.Quantidade }).ToList(),
                execucao.IniciadaEm,
                execucao.FinalizadaEm,
                execucao.TempoRealHoras));
        });

        grupo.MapGet("/execucao/{osId:guid}/historico", async (Guid osId, ObterHistoricoUseCase useCase, CancellationToken ct) =>
        {
            var historico = await useCase.ExecutarAsync(osId, ct);
            return Results.Ok(historico.Select(h => new HistoricoExecucaoResponse(h.StatusAnterior.ToString(), h.StatusNovo.ToString(), h.MudouEm, h.Origem)));
        });
    }
}
