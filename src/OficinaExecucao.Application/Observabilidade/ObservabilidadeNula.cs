namespace OficinaExecucao.Application.Observabilidade;

public sealed class ObservabilidadeNula : IObservabilidade
{
    public void NomearTransacaoWeb(string nome) { }

    public void IgnorarTransacao() { }

    public void AdicionarAtributo(string chave, string valor) { }

    public Task ProcessarMensagemAsync(ContextoMensagem contexto, Func<Task> processamento) => processamento();
}
