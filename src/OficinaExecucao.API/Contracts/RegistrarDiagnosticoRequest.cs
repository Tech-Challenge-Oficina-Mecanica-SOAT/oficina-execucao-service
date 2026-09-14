namespace OficinaExecucao.API.Contracts;

public sealed record RegistrarDiagnosticoRequest
{
    public required IReadOnlyList<ItemPecaDto> Pecas { get; init; }
    public required IReadOnlyList<ItemServicoDto> Servicos { get; init; }
    public decimal? TempoEstimadoHoras { get; init; }
    public string? Observacoes { get; init; }
}
