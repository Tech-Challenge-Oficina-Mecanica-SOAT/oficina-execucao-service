using OficinaExecucao.Domain;

namespace OficinaExecucao.Application.UseCases;

public sealed class ListarFilaUseCase(IExecucaoRepository repositorio)
{
    private static readonly StatusExecucao[] StatusEmFila =
    [
        StatusExecucao.AguardandoDiagnostico,
        StatusExecucao.EmDiagnostico,
        StatusExecucao.AguardandoReparo,
        StatusExecucao.EmReparo
    ];

    public async Task<IReadOnlyList<Execucao>> ExecutarAsync(StatusExecucao? status, CancellationToken ct)
    {
        if (status is not null)
            return await repositorio.ListarPorStatusAsync(status.Value, ct);

        var todas = new List<Execucao>();
        foreach (var valor in StatusEmFila)
        {
            todas.AddRange(await repositorio.ListarPorStatusAsync(valor, ct));
        }

        return todas;
    }
}
