# Execução Service — Design

Data: 2026-09-13

## Contexto

O Execução Service é um dos quatro microsserviços da Fase 4 do Tech Challenge (arquitetura de saga coreografada com eventos SNS/SQS entre Cadastros, OS, Billing e Execução). Ele é responsável pela fila de execução, pelo diagnóstico do veículo e pelo controle do reparo (início e finalização), usando DynamoDB como banco de dados NoSQL.

Este documento cobre o design completo do serviço, do bootstrap à entrega, servindo de base para o plano de implementação.

## Escopo

Cobre as seis semanas de trabalho do serviço: bootstrap e infraestrutura, API REST e máquina de estados, mensageria, testes end-to-end (locais e da saga completa entre os quatro serviços), qualidade (cobertura, SonarCloud) e deploy.

Fora do escopo deste repositório: os manifestos Kubernetes do cluster compartilhado (vivem em `oficina-infra-k8s`) e a implementação dos outros três serviços.

## Stack

.NET 8+ (mirror do `oficina-mecanica`, que já está em `net10.0`), Clean Architecture em quatro camadas, DynamoDB via AWS SDK, SQS/SNS para mensageria assíncrona, Terraform para infraestrutura, Docker/GitHub Actions para build e CI.

## Estrutura do repositório

```
oficina-execucao-service/
├── src/
│   ├── OficinaExecucao.API/
│   ├── OficinaExecucao.Application/
│   ├── OficinaExecucao.Domain/
│   └── OficinaExecucao.Infrastructure/
├── tests/
│   ├── OficinaExecucao.Tests.Unit/
│   └── OficinaExecucao.Tests.Integration/
├── infra/
│   ├── bootstrap/
│   └── terraform/
├── scripts/
│   ├── demo-happy-path.sh
│   └── demo-rollback.sh
├── docs/
│   ├── access-patterns.md
│   ├── openapi.yaml
│   └── adrs/
├── .github/workflows/ci.yml
├── docker-compose.yml
└── Dockerfile
```

Convenções (nomes de projeto, pacotes NuGet de observabilidade, layout de testes) seguem o `oficina-mecanica`, que já validou esse padrão na Fase 3: `net10.0`, `Nullable`/`ImplicitUsings` habilitados, Serilog + `Serilog.Formatting.Compact`, `NewRelic.Agent`, `prometheus-net.AspNetCore`, `Scalar.AspNetCore` para a UI do OpenAPI, `AspNetCore.Authentication.ApiKey` + `Microsoft.AspNetCore.Authentication.JwtBearer` para autenticação. Testes: xUnit, FluentAssertions, Moq, coverage via coverlet.

## Domínio e máquina de estados

Aggregate root `Execucao`, chave pela OS (`osId`). Estados (`StatusExecucao`):

```
AguardandoDiagnostico → EmDiagnostico → DiagnosticoConcluido → AguardandoReparo → EmReparo → Finalizado
                                                                                              Cancelado (a qualquer momento)
```

As transições são validadas dentro do próprio domínio (não na camada de aplicação ou na API), lançando uma exceção de domínio quando a transição solicitada não é permitida a partir do estado atual. Cada mudança de estado gera um registro de histórico append-only com a origem da mudança (`evento`, `api` ou `sistema`), conforme o schema `HistoricoExecucao` do contrato OpenAPI.

O diagnóstico gera um `diagnosticoId` (UUID) e a entrada em reparo gera um `execucaoId` (UUID), ambos necessários porque os eventos publicados (`diagnostico.concluido`, `execucao.iniciada`, `execucao.finalizada`) carregam esses IDs conforme o catálogo de eventos.

## Modelagem DynamoDB

Single table design, tabela `oficina-execucao-{env}`:

```
PK (partition key): "OS#{osId}"
SK (sort key):      "STATUS" | "HISTORICO#{timestamp}" | "DIAGNOSTICO"

Registros:
- OS#{id} + STATUS         → estado atual da execução
- OS#{id} + HISTORICO#...  → mudanças de estado (append only)
- OS#{id} + DIAGNOSTICO    → resultado do diagnóstico (peças/serviços)

GSI: status-index
- PK: status
- SK: adicionadoEm
→ permite listar "todas as OS em um dado status" (usado pelo endpoint GET /fila)
```

Idempotência de eventos consumidos: gravar `HISTORICO#{eventId}` antes de processar e checar existência antes de aplicar o efeito, evitando processar o mesmo evento duas vezes (SQS entrega pelo menos uma vez).

### Acesso ao DynamoDB

Uso de `IAmazonDynamoDB` (SDK de baixo nível), não `DynamoDBContext`. Motivo: o single table design mistura formatos de item diferentes (`STATUS`, `HISTORICO#...`, `DIAGNOSTICO`) sob a mesma partition key, o que o object mapper do `DynamoDBContext` não modela bem — ele assume um formato de item consistente por classe/tabela. O acesso fica isolado atrás de um Repository (`IExecucaoRepository`) na camada de Application, com a implementação concreta na Infrastructure, para que o SDK não vaze para o domínio nem para os testes unitários (mockados via a interface do repository, não via `IAmazonDynamoDB` diretamente, nos testes de Application; os testes de Infrastructure mockam `IAmazonDynamoDB`).

Sem outbox pattern: o DynamoDB não tem outbox nativo. Publicação do SNS acontece diretamente no handler, depois da escrita no DynamoDB, aceitando o risco (raro) de inconsistência em caso de falha entre os dois passos. Mitigação: log estruturado de toda publicação (sucesso e falha) para permitir reconciliação manual se necessário.

## API REST

Contrato já publicado em `contratos/openapi/execucao.yaml` (fonte da verdade, copiado para `docs/openapi.yaml` neste repositório):

- `GET /fila` — lista OS na fila, filtrável por status (usa a GSI `status-index`)
- `POST /execucao/{osId}/diagnostico` — mecânico registra diagnóstico (peças, serviços, tempo estimado); publica `diagnostico.concluido`
- `POST /execucao/{osId}/iniciar-reparo` — publica `execucao.iniciada`
- `POST /execucao/{osId}/finalizar` — publica `execucao.finalizada`
- `GET /execucao/{osId}` — estado atual
- `GET /execucao/{osId}/historico` — histórico completo

Autenticação: JWT Bearer + API Key, mesmo esquema do `oficina-mecanica`, para manter consistência entre os quatro serviços da Fase 4.

Dependência síncrona: `GET /cadastros/pecas/{id}` para validar estoque de peças no momento do diagnóstico. Enquanto o Cadastros Service não estiver disponível, usar Wiremock local com o contrato `cadastros.yaml` publicado.

## Mensageria

### Convenções (do catálogo de eventos)

- Envelope padrão: `eventId`, `eventType`, `eventVersion`, `occurredAt` (UTC), `correlationId` (W3C traceparent), `producer`, `data`.
- Nome dos topics SNS: `oficina-{env}-{tópico-com-tracos}` (pontos do nome do evento viram traços, ex. `os.criada` → `oficina-homolog-os-criada`).
- Nome da queue SQS: `oficina-{env}-execucao-queue`; DLQ: `oficina-{env}-execucao-dlq`.
- Retry: padrão do SQS (backoff exponencial, até 14 dias); após 3 tentativas falhadas, mensagem vai para a DLQ.
- Idempotência: `eventId` processado deve ser reconhecível por pelo menos 7 dias (implementado como item `HISTORICO#{eventId}` na tabela).

### Consome (subscriptions na queue `oficina-{env}-execucao-queue`)

| Evento | Publisher | Efeito |
|---|---|---|
| `os.criada` | OS Service | adiciona OS na fila, status `AguardandoDiagnostico` |
| `os.cancelada` | OS Service | marca execução como `Cancelado` |
| `orcamento.aprovado` | Billing Service | tira da fila, marca `AguardandoReparo` |

### Publica

| Evento | Payload (`data`) | Efeito nos consumers |
|---|---|---|
| `diagnostico.concluido` | `osId`, `diagnosticoId`, `pecas[]`, `servicos[]`, `tempoEstimadoHoras`, `concluidoEm` | OS Service atualiza status; Billing Service gera orçamento |
| `execucao.iniciada` | `osId`, `execucaoId`, `iniciadaEm` | OS Service atualiza status para `EmReparo` |
| `execucao.finalizada` | `osId`, `execucaoId`, `tempoRealHoras`, `finalizadaEm` | OS Service atualiza status; Billing Service cria cobrança |

### Descoberta via Parameter Store

Lê (publicados por outros serviços):

```
/oficina/{env}/messaging/topics/os.criada/arn
/oficina/{env}/messaging/topics/os.cancelada/arn
/oficina/{env}/messaging/topics/orcamento.aprovado/arn
/oficina/{env}/services/cadastros/api-url
```

Publica (para outros serviços consumirem):

```
/oficina/{env}/messaging/topics/diagnostico.concluido/arn
/oficina/{env}/messaging/topics/execucao.iniciada/arn
/oficina/{env}/messaging/topics/execucao.finalizada/arn
/oficina/{env}/services/execucao/api-url
```

## Infraestrutura (Terraform)

`infra/bootstrap/`: bucket S3 de state (`oficina-execucao-service-terraform-state`) e tabela DynamoDB de lock (`oficina-execucao-service-lock`), seguindo o mesmo padrão do `oficina-infra-db` — backend remoto próprio deste repositório, sem compartilhar bucket/tabela com outros serviços.

`infra/terraform/envs/{homolog,prod}/`: composição por ambiente.

`infra/terraform/modules/`:
- tabela DynamoDB (`oficina-execucao-{env}`) com GSI `status-index`
- IAM policy anexada ao LabRole permitindo Read/Write na tabela
- queue SQS + DLQ + subscriptions aos três topics consumidos
- topics SNS publicados (`diagnostico.concluido`, `execucao.iniciada`, `execucao.finalizada`)
- publicação dos parâmetros no Parameter Store (ARNs dos topics próprios e API URL)

## CI/CD

`.github/workflows/ci.yml`, dois jobs:

1. **Build & Test** — roda em PR e push para `main`: `dotnet restore`/`build`/`test` da solução.
2. **Build & Push Docker Image** — só em push para `main`: build e push da imagem para o GHCR.

Sem job de deploy automático para o cluster Kubernetes neste repositório (diferente do job de deploy-via-Kind-efêmero que o `oficina-mecanica` usa como demo). O deploy real no cluster compartilhado é responsabilidade de quem mantém `oficina-infra-k8s` na Fase 4, que consome a imagem publicada aqui.

## Testes

- **Unitários** (`OficinaExecucao.Tests.Unit`): xUnit + FluentAssertions + Moq, mockando `IExecucaoRepository` (camada Application) e `IAmazonDynamoDB` (camada Infrastructure).
- **Integração** (`OficinaExecucao.Tests.Integration`): Docker Compose com `amazon/dynamodb-local`, cobrindo o fluxo completo local (adicionar à fila via evento simulado, diagnóstico publicando SNS, reparo completo).
- **BDD** (SpecFlow/Reqnroll): cenários incluindo "OS criada é adicionada à fila", "Diagnóstico publica evento com peças e serviços", "Reparo finalizado publica execucao.finalizada".
- **Cobertura**: meta de 80%, verificada via SonarCloud.

## Scripts E2E da saga completa

Transversal deste serviço: os scripts de demonstração da saga completa entre os quatro serviços, em bash + curl + jq (roda em qualquer ambiente, inclusive CI).

- `demo-happy-path.sh`: cria cliente e veículo (Cadastros), abre OS (OS Service), registra diagnóstico (Execução), aprova orçamento (Billing), inicia e finaliza reparo (Execução), confirma pagamento (Billing, com webhook fake se o Mercado Pago sandbox não estiver disponível), verifica OS concluída.
- `demo-rollback.sh`: mesmo fluxo até o diagnóstico, mas recusa o orçamento; verifica que a OS foi cancelada e que a fila de execução foi liberada.

Saída colorida (`✅`/`❌`), fallback com mensagem clara se algum serviço estiver fora do ar, README próprio explicando dependências e como rodar. Ideal rodar em CI como smoke test de regressão contra o ambiente de homolog.

## Cronograma e restrição de calendário

Seis semanas de trabalho, com um período de indisponibilidade de 11/10 a 05/11 que cobre parte das semanas 5 e 6 (integração final e entrega). Ajuste necessário:

- Semanas 1 a 4 do plano concluídas antes de 10/10 (bootstrap, REST, máquina de estados, mensageria, início dos scripts E2E com mocks).
- Retorno em 05/11 para concluir a saga E2E completa contra os serviços reais, testes BDD, SonarCloud e deploy.

Esse ajuste exige alinhamento com o restante do grupo antes do início: os outros serviços precisam estar prontos (ou minimamente mockáveis) quando o trabalho retomar em novembro, já que a saga E2E depende diretamente deles.

## Riscos e paliativos

Quando um serviço dependente não estiver pronto:

1. Wiremock local expondo o contrato OpenAPI do serviço dependente.
2. Script publicador fake simulando o evento SNS esperado.
3. Docker Compose local para desenvolvimento isolado.
4. Contratos OpenAPI e de eventos já publicados desde o início permitem construir os mocks acima sem esperar a implementação real dos outros serviços.

## Checklist de entrega

- [ ] Repositório com branch protection
- [ ] Backend remoto de Terraform provisionado (`infra/bootstrap`)
- [ ] Tabela DynamoDB provisionada via Terraform, com GSI `status-index`
- [ ] REST completo (fila, diagnóstico, iniciar, finalizar, estado, histórico)
- [ ] Máquina de estados validada no domínio, com testes cobrindo transições inválidas
- [ ] Consome os três eventos (`os.criada`, `os.cancelada`, `orcamento.aprovado`) com idempotência
- [ ] Publica os três eventos (`diagnostico.concluido`, `execucao.iniciada`, `execucao.finalizada`)
- [ ] Cobertura de testes ≥80%, SonarCloud passando
- [ ] BDD com 3+ cenários
- [ ] Deploy K8s (imagem publicada no GHCR, consumida por `oficina-infra-k8s`)
- [ ] New Relic instrumentado
- [ ] `demo-happy-path.sh` funcionando ponta a ponta
- [ ] `demo-rollback.sh` funcionando
- [ ] Documentação dos scripts E2E
