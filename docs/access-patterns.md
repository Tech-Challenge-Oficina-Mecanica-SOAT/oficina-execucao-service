# Access Patterns — Execução Service (DynamoDB)

Single table: `oficina-execucao-{env}`.

| # | Padrão de acesso | Operação | Chave |
|---|---|---|---|
| 1 | Estado atual da execução de uma OS | GetItem | PK=`OS#{osId}`, SK=`STATUS` |
| 2 | Diagnóstico registrado de uma OS | GetItem | PK=`OS#{osId}`, SK=`DIAGNOSTICO` |
| 3 | Histórico completo de mudanças de status de uma OS | Query | PK=`OS#{osId}`, SK begins_with `HISTORICO#` |
| 4 | Verificar se um evento já foi processado (idempotência) | GetItem | PK=`OS#{osId}`, SK=`HISTORICO#EVT#{eventId}` |
| 5 | Listar a fila filtrada por status (`GET /fila`) | Query na GSI `status-index` | PK=`status`, ordenado por SK=`adicionadaEm` |

Notas:

- Os itens de histórico de domínio (padrão 3) usam `HISTORICO#{timestamp}`; os marcadores de idempotência de evento (padrão 4) usam o prefixo distinto `HISTORICO#EVT#{eventId}` para nunca colidir com um timestamp.
- A tabela usa `PAY_PER_REQUEST` (sem provisionamento de capacidade), consistente com o padrão usado em `oficina-infra-db`.
- Nenhuma dessas operações precisa de transação multi-item: cada escrita é sempre um único `PutItem`/`UpdateItem`.
