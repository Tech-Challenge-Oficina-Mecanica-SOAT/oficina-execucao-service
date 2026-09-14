namespace OficinaExecucao.API.Contracts;

public sealed record ItemServicoDto
{
    public required Guid ServicoId { get; init; }
    public required int Quantidade { get; init; }
}
