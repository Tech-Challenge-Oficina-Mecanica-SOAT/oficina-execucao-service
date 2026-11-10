# Observabilidade do Execução Service

O serviço roda com o agente .NET do New Relic (injetado pelo init container, ver `docs/deploy.md`) e escreve logs JSON com Serilog. Este documento descreve o que está instrumentado e como consultar.

## O que está instrumentado

### Requisições HTTP (API)

- **Nome da transação:** o template da rota, nunca o `osId` concreto. Exemplos: `Web/GET /execucao/{osId}`, `Web/POST /execucao/{osId}/diagnostico`, `Web/GET /fila`. Isso evita uma métrica por ordem de serviço.
- **Health checks ignorados:** `/health/live` e `/health/ready` não geram transações nem traces.
- **Atributos customizados:** `correlationId` (trace id W3C da requisição, o mesmo que aparece nos logs) em toda requisição, e `osId` nas rotas que têm `osId`.
- **Logs:** `correlationId` e `osId` entram no escopo de log, então aparecem como campos nos logs JSON de tudo que é registrado durante a requisição.

### Consumo de mensagens (SQS)

Cada mensagem processada pelo consumidor vira uma transação de background chamada `Message/SQS/<eventType>` (por exemplo `Message/SQS/os.criada`; no New Relic o nome completo aparece com o prefixo `OtherTransaction/`).

- **Atributos:** `osId`, `eventType`, `eventId` e `correlationId`.
- **`correlationId`:** vem do envelope do evento. O contrato do grupo define esse campo como o `traceparent` W3C da requisição que originou o evento. No log e no atributo usamos apenas o trace id (32 hex); se o valor não for um traceparent, ele é usado como veio. Se o envelope não tiver `correlationId`, a mensagem é processada normalmente, só sem esse campo.
- **Eventos publicados durante o consumo** reaproveitam o `correlationId` recebido, para que a cadeia de eventos mantenha a mesma correlação.

## Como consultar no New Relic

Transações de uma OS, por nome:

```sql
SELECT count(*) FROM Transaction WHERE osId = '<id>' FACET name SINCE 30 minutes ago
```

Tempo médio por tipo de transação (HTTP e mensagens):

```sql
SELECT average(duration), count(*) FROM Transaction FACET name SINCE 1 hour ago
```

Somente o consumo de mensagens:

```sql
SELECT count(*) FROM Transaction WHERE name LIKE '%Message/SQS/%' FACET eventType SINCE 1 hour ago
```

Logs de uma mesma cadeia de eventos:

```sql
SELECT * FROM Log WHERE correlationId = '<traceid>' SINCE 1 hour ago
```

Para seguir um fluxo entre serviços, pegue o `correlationId` (trace id) de uma requisição ou evento e consulte `Transaction` e `Log` com esse valor nos serviços envolvidos.

## Limites e honestidade sobre o tracing distribuído

A correlação entre serviços através de SNS/SQS é garantida por `correlationId` e `osId`, presentes nos logs e nos atributos de transação de todos os serviços. Ao consumir, o serviço também tenta continuar a trace de origem entregando o `traceparent` do envelope ao agente (`AcceptDistributedTraceHeaders` com transporte `Queue`). Se a UI do New Relic vai exibir esses spans ligados em um único trace depende do contexto de trace que o produtor enviou e de o produtor também estar instrumentado; quando não ligar, use as consultas acima.
