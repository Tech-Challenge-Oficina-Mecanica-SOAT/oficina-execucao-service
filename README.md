# oficina-execucao-service

Execução Service: gerencia a fila de execução, o diagnóstico e o reparo de Ordens de Serviço (OS), persistindo estado em DynamoDB. Faz parte dos microsserviços do Tech Challenge Fase 4.

## Build e testes

```
dotnet build OficinaExecucao.slnx
dotnet test OficinaExecucao.slnx
```

## Testes

Além dos testes de unidade e de integração (mockados), o projeto `OficinaExecucao.Tests.E2E` roda testes end-to-end reais contra um container local do LocalStack (emulador de AWS via Docker: DynamoDB + SQS + SNS) — nada é mockado. Requer Docker rodando localmente.

Rodar só a suíte E2E:

```
dotnet test tests/OficinaExecucao.Tests.E2E
```

Rodar tudo exceto a suíte E2E (o que o CI executa, sem precisar de Docker):

```
dotnet test OficinaExecucao.slnx --filter "FullyQualifiedName!~Tests.E2E"
```

Testes de comportamento (BDD, [Reqnroll](https://reqnroll.net/)) cobrem o caminho feliz da fila de execução, do diagnóstico e da finalização do reparo, batendo nos endpoints REST reais com um repositório e um publicador de eventos fakes (os cenários estão em `tests/OficinaExecucao.Tests.BDD/Features`):

```
dotnet test tests/OficinaExecucao.Tests.BDD
```

A cobertura de linha combinada de `OficinaExecucao.Tests.Unit` + `OficinaExecucao.Tests.Integration` (mínimo 80%, verificado no CI) é calculada com:

```
bash scripts/coverage.sh
```

O CI também roda análise estática e quality gate no SonarCloud a cada PR e push em `main`, quando o repositório tiver `SONAR_TOKEN`/`SONAR_PROJECT_KEY`/`SONAR_ORGANIZATION` configurados — sem essas credenciais, o CI builda e testa normalmente e só pula a análise.

## Rodando localmente com Docker

```
docker build -t oficina-execucao-service:local .
docker run -d -p 5000:5000 oficina-execucao-service:local
```

Depois de subir o container:

- Health check (liveness): `GET http://localhost:5000/health/live`
- Health check (readiness, confere conexão com o DynamoDB): `GET http://localhost:5000/health/ready`
- Documentação interativa (Scalar UI): `http://localhost:5000/scalar/v1`

### Demos da Saga

Dois scripts reproduzem a Saga da OS na sua máquina (só Docker e curl): o caminho feliz até a execução finalizada e a compensação com cancelamento idempotente. API, SNS, SQS e DynamoDB são reais (LocalStack); OS Service e Billing são simulados.

Veja o passo a passo em [demo/README.md](demo/README.md).

## Endpoints da API

Além de `/health/live` e `/health/ready`, o serviço expõe:

- `GET /fila` — lista a fila de execução, opcionalmente filtrada por `status`
- `POST /execucao/{osId}/diagnostico` — registra o diagnóstico do mecânico
- `POST /execucao/{osId}/iniciar-reparo` — inicia o reparo
- `POST /execucao/{osId}/finalizar` — finaliza o reparo
- `GET /execucao/{osId}` — estado atual da execução
- `GET /execucao/{osId}/historico` — histórico de mudanças de estado

Todos exigem autenticação: um JWT Bearer válido (`Authorization: Bearer <token>`) ou uma API Key interna (`X-Internal-Api-Key: <chave>`, configurada em `InternalApi:ApiKey`). Em desenvolvimento local, as chaves de `appsettings.json` (`Jwt:SecretKey`, `InternalApi:ApiKey`) já vêm preenchidas com valores de exemplo — não use em produção sem trocá-las via secret real.

## Infraestrutura (Terraform)

A infraestrutura é aplicada em duas etapas, nesta ordem:

1. **`infra/bootstrap`** — provisiona o bucket S3 de state e a tabela DynamoDB de lock usados pelo backend remoto de `infra/terraform/envs/*` (ver `backend.tf` em cada ambiente). Precisa ser aplicado primeiro:

   ```
   cd infra/bootstrap
   terraform init
   terraform apply
   ```

2. **`infra/terraform/envs/homolog`** (ou `envs/prod`) — provisiona a tabela DynamoDB usada em tempo de execução pelo serviço:

   ```
   cd infra/terraform/envs/homolog
   terraform init
   terraform apply
   ```

Ambas as etapas exigem credenciais reais da AWS Academy exportadas no ambiente (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`). Este repositório não gerencia essas credenciais: exporte-as você mesmo antes de rodar `terraform apply`.

**Atenção ao aplicar `infra/bootstrap` pela primeira vez:** contas AWS Academy podem ter uma SCP que nega leitura de object-lock em buckets S3, o que quebra o `apply` do bucket de state logo depois de criado. Ver [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) para o diagnóstico e o contorno (`terraform untaint` + `terraform apply -refresh=false`) antes de tentar de novo.

### Mensageria (bloqueado parcialmente)

O módulo `execucao-messaging` cria a fila SQS, a DLQ e os 3 tópicos SNS que este serviço publica — essa parte pode ser aplicada junto com o restante (`infra/terraform/envs/homolog` ou `envs/prod`).

As 3 assinaturas SQS⟵SNS que consomem eventos de outros serviços (`os.criada`, `os.cancelada`, `orcamento.aprovado`) dependem de parâmetros no Parameter Store que só existem depois que o OS Service e o Billing Service publicarem os próprios tópicos SNS (a Semana 3 de cada um deles). Enquanto isso não acontecer, o `terraform apply` deste ambiente falha especificamente nessa parte — o restante (tabela DynamoDB, fila, DLQ, tópicos publicados) aplica normalmente. Assim que os outros serviços publicarem, rode `terraform apply` de novo aqui para completar as assinaturas. As assinaturas usam `raw_message_delivery = true` — o corpo da mensagem SQS é o envelope do evento diretamente, sem o wrapper de notificação do SNS. Os outros serviços do grupo precisam manter essa mesma convenção nas próprias assinaturas.

**Limitação conhecida:** como o SNS não garante ordem de entrega entre tópicos diferentes, se `os.cancelada` chegar antes de `os.criada` ter sido processado para a mesma OS, o cancelamento é ignorado silenciosamente (logado como warning) e a OS acaba criada depois sem nunca ser cancelada. Uma correção real (marcador de "tombstone" ou retry limitado) fica para a Semana 4.

## Deploy (Kubernetes)

O serviço roda no cluster EKS compartilhado do grupo (`oficina-infra-k8s`), namespace `oficina`. Manifestos em `k8s/`, scripts em `scripts/deploy.sh` e `scripts/populate-secret.sh`. Passo a passo completo em [docs/deploy.md](docs/deploy.md).

## Arquitetura

Decisões de design e descobertas feitas durante a implementação (incompatibilidades do AWS Academy, etc.) em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
