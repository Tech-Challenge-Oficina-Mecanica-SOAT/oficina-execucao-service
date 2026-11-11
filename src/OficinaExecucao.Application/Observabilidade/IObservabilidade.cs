namespace OficinaExecucao.Application.Observabilidade;

/// <summary>
/// Fronteira fina para o APM (New Relic). A implementação real fica na Infrastructure;
/// <see cref="ObservabilidadeNula"/> é usada em testes e quando não há agente.
/// </summary>
public interface IObservabilidade
{
    /// <summary>Renomeia a transação web corrente (nome já sem prefixo de categoria).</summary>
    void NomearTransacaoWeb(string nome);

    void IgnorarTransacao();

    void AdicionarAtributo(string chave, string valor);

    /// <summary>Executa o processamento de uma mensagem dentro de uma transação de background.</summary>
    Task ProcessarMensagemAsync(ContextoMensagem contexto, Func<Task> processamento);
}
