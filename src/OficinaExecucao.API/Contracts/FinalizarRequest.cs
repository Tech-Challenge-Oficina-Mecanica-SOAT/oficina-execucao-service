namespace OficinaExecucao.API.Contracts;

public sealed record FinalizarRequest
{
    public decimal? TempoRealHoras { get; init; }
}
