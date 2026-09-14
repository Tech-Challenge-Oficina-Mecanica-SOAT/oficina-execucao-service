# oficina-execucao-service

Execução Service: gerencia a fila de execução, o diagnóstico e o reparo de Ordens de Serviço (OS), persistindo estado em DynamoDB. Faz parte dos microsserviços do Tech Challenge Fase 4.

## Build e testes

```
dotnet build OficinaExecucao.slnx
dotnet test OficinaExecucao.slnx
```

## Rodando localmente com Docker

```
docker build -t oficina-execucao-service:local .
docker run -d -p 5000:5000 oficina-execucao-service:local
```

Depois de subir o container:

- Health check: `GET http://localhost:5000/health`
- Documentação interativa (Scalar UI): `http://localhost:5000/scalar/v1`

## Endpoints da API

Além do `/health`, o serviço expõe:

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

## Arquitetura

Decisões de design e descobertas feitas durante a implementação (incompatibilidades do AWS Academy, etc.) em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
