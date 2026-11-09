#!/usr/bin/env bash
set -euo pipefail

# Monta o Secret oficina-execucao-secrets (config do serviço) e atualiza a chave
# NEW_RELIC_LICENSE_KEY no Secret compartilhado oficina-secrets (mesma convenção
# que o oficina-cadastros-service usa para o New Relic).
#
# Uso: ./scripts/populate-secret.sh [homolog|prod]
# Requer no ambiente: AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN
# (credenciais da sessão AWS Academy atual — repassadas ao pod porque o node do
# EKS não tem permissão IAM para DynamoDB/SQS/SNS nesta conta, ver docs/ARCHITECTURE.md)
# e, opcionalmente, NEW_RELIC_LICENSE_KEY.

ENV=${1:-homolog}

: "${AWS_ACCESS_KEY_ID:?Exporte AWS_ACCESS_KEY_ID antes de rodar este script}"
: "${AWS_SECRET_ACCESS_KEY:?Exporte AWS_SECRET_ACCESS_KEY antes de rodar este script}"
: "${AWS_SESSION_TOKEN:?Exporte AWS_SESSION_TOKEN antes de rodar este script}"

echo "Populando Secrets K8s do Execução para env=$ENV"

TABLE_NAME=$(aws ssm get-parameter --name "/oficina/$ENV/execucao/table-name" --query Parameter.Value --output text)
QUEUE_URL=$(aws ssm get-parameter --name "/oficina/$ENV/execucao/queue-url" --query Parameter.Value --output text)
TOPIC_DIAGNOSTICO=$(aws ssm get-parameter --name "/oficina/$ENV/messaging/topics/diagnostico.concluido/arn" --query Parameter.Value --output text)
TOPIC_EXECUCAO_INICIADA=$(aws ssm get-parameter --name "/oficina/$ENV/messaging/topics/execucao.iniciada/arn" --query Parameter.Value --output text)
TOPIC_EXECUCAO_FINALIZADA=$(aws ssm get-parameter --name "/oficina/$ENV/messaging/topics/execucao.finalizada/arn" --query Parameter.Value --output text)

JWT_SECRET=$(openssl rand -hex 32)
INTERNAL_API_KEY=$(openssl rand -hex 32)

kubectl create secret generic oficina-execucao-secrets \
  --namespace oficina \
  --from-literal=DynamoDb__TableName="$TABLE_NAME" \
  --from-literal=Sqs__QueueUrl="$QUEUE_URL" \
  --from-literal=Sns__TopicArns__diagnostico.concluido="$TOPIC_DIAGNOSTICO" \
  --from-literal=Sns__TopicArns__execucao.iniciada="$TOPIC_EXECUCAO_INICIADA" \
  --from-literal=Sns__TopicArns__execucao.finalizada="$TOPIC_EXECUCAO_FINALIZADA" \
  --from-literal=Jwt__SecretKey="$JWT_SECRET" \
  --from-literal=InternalApi__ApiKey="$INTERNAL_API_KEY" \
  --from-literal=AWS_ACCESS_KEY_ID="$AWS_ACCESS_KEY_ID" \
  --from-literal=AWS_SECRET_ACCESS_KEY="$AWS_SECRET_ACCESS_KEY" \
  --from-literal=AWS_SESSION_TOKEN="$AWS_SESSION_TOKEN" \
  --dry-run=client -o yaml | kubectl apply -f -

echo "Secret 'oficina-execucao-secrets' criado/atualizado no namespace 'oficina'."

# oficina-secrets é compartilhado entre serviços (o oficina-infra-k8s também cria/atualiza
# esse mesmo Secret, com as chaves ConnectionStrings__DefaultConnection e Jwt__SecretKey).
# Por isso aqui só fazemos patch da chave NEW_RELIC_LICENSE_KEY (nunca um create/apply do
# objeto inteiro, que substituiria o Secret e apagaria as chaves do outro repo) e só quando
# a variável estiver de fato exportada (caso contrário um create/apply gravaria um valor
# vazio por cima de uma licença válida já configurada por outro serviço).
if [[ -n "${NEW_RELIC_LICENSE_KEY:-}" ]]; then
  kubectl -n oficina patch secret oficina-secrets --type merge \
    -p "{\"stringData\":{\"NEW_RELIC_LICENSE_KEY\":\"$NEW_RELIC_LICENSE_KEY\"}}" 2>/dev/null \
  || kubectl -n oficina create secret generic oficina-secrets \
       --from-literal=NEW_RELIC_LICENSE_KEY="$NEW_RELIC_LICENSE_KEY"
  echo "Secret 'oficina-secrets' atualizado (NEW_RELIC_LICENSE_KEY)."
else
  echo "NEW_RELIC_LICENSE_KEY nao exportada: mantendo o Secret compartilhado 'oficina-secrets' como esta (nao sobrescrevendo)."
fi
