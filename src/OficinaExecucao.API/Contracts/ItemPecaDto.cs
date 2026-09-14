namespace OficinaExecucao.API.Contracts;

public sealed record ItemPecaDto
{
    public required Guid PecaId { get; init; }
    public required int Quantidade { get; init; }
}
