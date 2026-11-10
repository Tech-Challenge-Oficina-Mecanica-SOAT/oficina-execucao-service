#!/usr/bin/env bash
set -euo pipefail

# Provisiona tabela, filas e topicos no LocalStack, aquece SNS->SQS e espera a API.
# Pre-requisito: docker compose -f demo/docker-compose.demo.yml up -d --build

# Antes do source: a lib já verifica o ambiente do alvo ao ser carregada
if [ "${TARGET:-local}" = "eks" ]; then
  printf '\033[1;31m❌ %s\033[0m\n' "demo-setup.sh é só do alvo local (TARGET=local)." >&2
  exit 1
fi

source "$(dirname "${BASH_SOURCE[0]}")/lib/demo-lib.sh"
TABELA="oficina-execucao-demo"

step "Aguardando o LocalStack ficar pronto"
fim=$((SECONDS + 60))
pronto=false
while [ "$SECONDS" -lt "$fim" ]; do
  if aws_local dynamodb list-tables >/dev/null 2>&1; then pronto=true; break; fi
  sleep 2
done
[ "$pronto" = true ] || fail "LocalStack nao respondeu em 60 s. Veja: docker compose -f demo/docker-compose.demo.yml logs localstack"
ok "LocalStack pronto"

step "Criando tabela DynamoDB $TABELA"
if aws_local dynamodb describe-table --table-name "$TABELA" >/dev/null 2>&1; then
  info "Tabela ja existe"
else
  aws_local dynamodb create-table --table-name "$TABELA" --billing-mode PAY_PER_REQUEST \
    --attribute-definitions AttributeName=PK,AttributeType=S AttributeName=SK,AttributeType=S \
      AttributeName=status,AttributeType=S AttributeName=adicionadaEm,AttributeType=S \
    --key-schema AttributeName=PK,KeyType=HASH AttributeName=SK,KeyType=RANGE \
    --global-secondary-indexes 'IndexName=status-index,KeySchema=[{AttributeName=status,KeyType=HASH},{AttributeName=adicionadaEm,KeyType=RANGE}],Projection={ProjectionType=ALL}' \
    >/dev/null
  ok "Tabela criada"
fi

queue_arn() {
  aws_local sqs get-queue-attributes --queue-url "http://localhost:4566/000000000000/$1" \
    --attribute-names QueueArn --query 'Attributes.QueueArn' --output text | tr -d '\r'
}

step "Criando filas SQS"
aws_local sqs create-queue --queue-name oficina-execucao-dlq >/dev/null
DLQ_ARN="$(queue_arn oficina-execucao-dlq)"
# RedrivePolicy e uma string JSON dentro do JSON de atributos
REDRIVE_ESCAPADO="{\\\"deadLetterTargetArn\\\":\\\"$DLQ_ARN\\\",\\\"maxReceiveCount\\\":\\\"3\\\"}"
aws_local sqs create-queue --queue-name oficina-execucao-queue \
  --attributes "{\"RedrivePolicy\":\"$REDRIVE_ESCAPADO\"}" >/dev/null
aws_local sqs create-queue --queue-name oficina-execucao-verificacao-queue >/dev/null
QUEUE_ARN="$(queue_arn oficina-execucao-queue)"
VERIF_ARN="$(queue_arn oficina-execucao-verificacao-queue)"
ok "Filas prontas"

# create-topic e idempotente; subscribe com os mesmos parametros devolve a assinatura existente
assinar() {
  local topico="$1" fila_arn="$2" arn
  arn="$(aws_local sns create-topic --name "$topico" --query TopicArn --output text | tr -d '\r')"
  aws_local sns subscribe --topic-arn "$arn" --protocol sqs --notification-endpoint "$fila_arn" \
    --attributes RawMessageDelivery=true >/dev/null
}

step "Criando topicos SNS publicados (assinados pela fila de verificacao)"
for t in diagnostico-concluido execucao-iniciada execucao-finalizada; do
  assinar "$t" "$VERIF_ARN"
done
ok "Topicos publicados prontos"

step "Criando topicos SNS consumidos (assinados pela fila real)"
for t in os-criada os-cancelada orcamento-aprovado; do
  assinar "$t" "$QUEUE_ARN"
done
ok "Topicos consumidos prontos"

step "Aquecendo a entrega SNS -> SQS"
aws_local sns publish --topic-arn "$(topic_arn diagnostico-concluido)" --message '{"warmup":true}' >/dev/null
fim=$((SECONDS + 60))
aquecido=false
while [ "$SECONDS" -lt "$fim" ]; do
  linhas="$(aws_local sqs receive-message --queue-url "$VERIF_QUEUE_URL" --max-number-of-messages 10 \
    --wait-time-seconds 2 --query 'Messages[].[ReceiptHandle,Body]' --output text 2>/dev/null | tr -d '\r')" || linhas=""
  if [ -n "$linhas" ] && [ "$linhas" != "None" ]; then
    while IFS=$'\t' read -r handle corpo; do
      [ -n "$handle" ] || continue
      if [[ "$corpo" == *warmup* ]]; then
        aws_local sqs delete-message --queue-url "$VERIF_QUEUE_URL" --receipt-handle "$handle" >/dev/null
        aquecido=true
      fi
    done <<< "$linhas"
  fi
  if [ "$aquecido" = true ]; then break; fi
  sleep 1
done
[ "$aquecido" = true ] || fail "Entrega SNS -> SQS nao respondeu em 60 s"
ok "Entrega SNS -> SQS aquecida"

step "Aguardando a API ($API_URL/health/ready)"
fim=$((SECONDS + 60))
pronta=false
while [ "$SECONDS" -lt "$fim" ]; do
  codigo="$(curl -s -o /dev/null -w '%{http_code}' "$API_URL/health/ready" || true)"
  if [ "$codigo" = "200" ]; then pronta=true; break; fi
  sleep 1
done
[ "$pronta" = true ] || fail "API nao ficou pronta em 60 s. Veja: docker compose -f demo/docker-compose.demo.yml logs api"

ok "Ambiente pronto"
