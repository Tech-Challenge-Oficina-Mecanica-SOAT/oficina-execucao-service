using OficinaExecucao.Domain.Exceptions;

namespace OficinaExecucao.Domain;

public sealed class Execucao
{
    public Guid OsId { get; }
    public StatusExecucao Status { get; private set; }
    public Guid? DiagnosticoId { get; private set; }
    public Guid? ExecucaoId { get; private set; }
    public IReadOnlyList<ItemPeca> Pecas { get; private set; } = Array.Empty<ItemPeca>();
    public IReadOnlyList<ItemServico> Servicos { get; private set; } = Array.Empty<ItemServico>();
    public decimal? TempoEstimadoHoras { get; private set; }
    public string? Observacoes { get; private set; }
    public DateTimeOffset? IniciadaEm { get; private set; }
    public DateTimeOffset? FinalizadaEm { get; private set; }
    public decimal? TempoRealHoras { get; private set; }
    public DateTimeOffset AdicionadaEm { get; private set; }

    private Execucao(Guid osId, StatusExecucao status)
    {
        OsId = osId;
        Status = status;
    }

    public static Execucao NovaNaFila(Guid osId) => new(osId, StatusExecucao.AguardandoDiagnostico)
    {
        AdicionadaEm = DateTimeOffset.UtcNow
    };

    public static Execucao Reidratar(
        Guid osId,
        StatusExecucao status,
        Guid? diagnosticoId,
        Guid? execucaoId,
        IReadOnlyList<ItemPeca> pecas,
        IReadOnlyList<ItemServico> servicos,
        decimal? tempoEstimadoHoras,
        string? observacoes,
        DateTimeOffset? iniciadaEm,
        DateTimeOffset? finalizadaEm,
        decimal? tempoRealHoras,
        DateTimeOffset adicionadaEm)
    {
        return new Execucao(osId, status)
        {
            DiagnosticoId = diagnosticoId,
            ExecucaoId = execucaoId,
            Pecas = pecas,
            Servicos = servicos,
            TempoEstimadoHoras = tempoEstimadoHoras,
            Observacoes = observacoes,
            IniciadaEm = iniciadaEm,
            FinalizadaEm = finalizadaEm,
            TempoRealHoras = tempoRealHoras,
            AdicionadaEm = adicionadaEm
        };
    }

    public HistoricoEntry RegistrarDiagnostico(
        IReadOnlyList<ItemPeca> pecas, IReadOnlyList<ItemServico> servicos, decimal? tempoEstimadoHoras, string? observacoes, string origem = "api")
    {
        if (Status is not (StatusExecucao.AguardandoDiagnostico or StatusExecucao.EmDiagnostico))
            throw new TransicaoInvalidaException(Status, nameof(RegistrarDiagnostico));

        var statusAnterior = Status;
        DiagnosticoId = Guid.NewGuid();
        Pecas = pecas;
        Servicos = servicos;
        TempoEstimadoHoras = tempoEstimadoHoras;
        Observacoes = observacoes;
        Status = StatusExecucao.DiagnosticoConcluido;

        return new HistoricoEntry(statusAnterior, Status, DateTimeOffset.UtcNow, origem);
    }

    public HistoricoEntry IniciarReparo(string origem = "api")
    {
        if (Status != StatusExecucao.AguardandoReparo)
            throw new TransicaoInvalidaException(Status, nameof(IniciarReparo));

        var statusAnterior = Status;
        ExecucaoId = Guid.NewGuid();
        IniciadaEm = DateTimeOffset.UtcNow;
        Status = StatusExecucao.EmReparo;

        return new HistoricoEntry(statusAnterior, Status, DateTimeOffset.UtcNow, origem);
    }

    public HistoricoEntry Finalizar(decimal tempoRealHoras, string origem = "api")
    {
        if (Status != StatusExecucao.EmReparo)
            throw new TransicaoInvalidaException(Status, nameof(Finalizar));

        var statusAnterior = Status;
        TempoRealHoras = tempoRealHoras;
        FinalizadaEm = DateTimeOffset.UtcNow;
        Status = StatusExecucao.Finalizado;

        return new HistoricoEntry(statusAnterior, Status, DateTimeOffset.UtcNow, origem);
    }

    public HistoricoEntry Cancelar(string origem = "api")
    {
        if (Status is StatusExecucao.Finalizado or StatusExecucao.Cancelado)
            throw new TransicaoInvalidaException(Status, nameof(Cancelar));

        var statusAnterior = Status;
        Status = StatusExecucao.Cancelado;

        return new HistoricoEntry(statusAnterior, Status, DateTimeOffset.UtcNow, origem);
    }
}
