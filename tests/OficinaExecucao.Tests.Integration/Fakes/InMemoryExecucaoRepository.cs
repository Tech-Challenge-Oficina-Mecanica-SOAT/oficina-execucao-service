using OficinaExecucao.Application;
using OficinaExecucao.Domain;

namespace OficinaExecucao.Tests.Integration.Fakes;

public sealed class InMemoryExecucaoRepository : IExecucaoRepository
{
    private readonly Dictionary<Guid, Execucao> _execucoes = new();
    private readonly Dictionary<Guid, List<HistoricoEntry>> _historico = new();

    public Execucao Adicionar(Execucao execucao)
    {
        _execucoes[execucao.OsId] = execucao;
        _historico[execucao.OsId] = [];
        return execucao;
    }

    public Task<Execucao?> ObterPorOsIdAsync(Guid osId, CancellationToken ct) =>
        Task.FromResult(_execucoes.TryGetValue(osId, out var execucao) ? execucao : null);

    public Task SalvarAsync(Execucao execucao, HistoricoEntry novaEntrada, CancellationToken ct)
    {
        _execucoes[execucao.OsId] = execucao;
        if (!_historico.TryGetValue(execucao.OsId, out var lista))
            _historico[execucao.OsId] = lista = [];
        lista.Add(novaEntrada);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoricoEntry>> ObterHistoricoAsync(Guid osId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HistoricoEntry>>(_historico.TryGetValue(osId, out var lista) ? lista : []);

    public Task<IReadOnlyList<Execucao>> ListarPorStatusAsync(StatusExecucao status, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Execucao>>(_execucoes.Values.Where(e => e.Status == status).ToList());

    public Task<bool> EventoJaProcessadoAsync(Guid osId, string eventId, CancellationToken ct) => Task.FromResult(false);
}
