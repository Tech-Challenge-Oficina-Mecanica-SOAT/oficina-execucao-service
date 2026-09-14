# Access Patterns — Execução Service (DynamoDB)

Single table: `oficina-execucao-{env}`.

| # | Padrão de acesso | Operação | Chave |
|---|---|---|---|
| 1 | Estado atual da execução de uma OS, incluindo os dados de diagnóstico (`pecas`, `servicos`, `tempoEstimadoHoras`, `observacoes`, gravados no próprio item `STATUS`) | GetItem | PK=`OS#{osId}`, SK=`STATUS` |
| 2 | Histórico completo de mudanças de status de uma OS | Query | PK=`OS#{osId}`, SK begins_with `HISTORICO#` |
| 3 | Verificar se um evento já foi processado (idempotência) | GetItem | PK=`OS#{osId}`, SK=`HISTORICO#EVT#{eventId}` |
| 4 | Listar a fila filtrada por status (`GET /fila`) | Query na GSI `status-index` | PK=`status`, ordenado por SK=`adicionadaEm` |

Notas:

- Os itens de histórico de domínio (padrão 2) usam `HISTORICO#{timestamp}`; os marcadores de idempotência de evento (padrão 3) usam o prefixo distinto `HISTORICO#EVT#{eventId}` para nunca colidir com um timestamp.
- A tabela usa `PAY_PER_REQUEST` (sem provisionamento de capacidade), consistente com o padrão usado em `oficina-infra-db`.
- As mudanças de estado gravam dois itens atomicamente via `TransactWriteItems` (o item `STATUS` e o novo item `HISTORICO#{timestamp}`), não `PutItem` isolados — evita inconsistência se o processo falhar entre as duas escritas.
