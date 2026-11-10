#!/usr/bin/env bash
# Variante do scripts/deploy-k8s.sh do oficina-os-service para a demo no AWS Academy.
# Diferenças em relação ao original (que NÃO é alterado): Service interno (ClusterIP) em vez de NLB
# internet-facing, e nenhuma escrita no SSM. Mesmos manifests, mesmo Secret.
# As credenciais da sessão (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN) são sempre
# injetadas no Secret (o Academy não tem IRSA). NEW_RELIC_LICENSE_KEY é opcional e nunca é impressa.
# Requer envsubst. Uso: [OS_REPO=<clone do oficina-os-service>] [IMAGE=<imagem>] bash demo/academy/os/deploy-os-academy.sh [homolog]
# Sem OS_REPO, procura a pasta irmã oficina-os-service subindo a árvore a partir deste script.
set -euo pipefail
export MSYS_NO_PATHCONV=1
source "$(dirname "${BASH_SOURCE[0]}")/../_localizar-clone.sh"
localizar_clone oficina-os-service OS_REPO k8s || { echo "Clone do oficina-os-service não encontrado. Defina OS_REPO=/caminho/oficina-os-service." >&2; exit 1; }
ENVIRONMENT=${1:-homolog}
NS=oficina
IMAGE=${IMAGE:-ghcr.io/tech-challenge-oficina-mecanica-soat/oficina-os-service:latest}
export AWS_DEFAULT_REGION=${AWS_DEFAULT_REGION:-us-east-1}
ssm() { aws ssm get-parameter --name "$1" --query Parameter.Value --output text; }
cd "$OS_REPO"

CADASTROS_URL=$(ssm "/oficina/$ENVIRONMENT/services/cadastros/api-url")
DB_ENDPOINT=$(ssm "/oficina/$ENVIRONMENT/db/endpoint"); DB_PORT=$(ssm "/oficina/$ENVIRONMENT/db/port")
DB_USER=$(ssm "/oficina/$ENVIRONMENT/db/username")
DB_PASSWORD=$(aws secretsmanager get-secret-value --secret-id "oficina/$ENVIRONMENT/db-password" --query SecretString --output text)
CONNECTION_STRING="Host=$DB_ENDPOINT;Port=$DB_PORT;Database=oficina_os;Username=$DB_USER;Password=$DB_PASSWORD;Trust Server Certificate=true"
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
SECRET_ARGS=(
  --from-literal=ConnectionStrings__Default="$CONNECTION_STRING"
  --from-literal=NEW_RELIC_LICENSE_KEY="${NEW_RELIC_LICENSE_KEY:-}"
  --from-literal=AWS_ACCESS_KEY_ID="${AWS_ACCESS_KEY_ID:?}"
  --from-literal=AWS_SECRET_ACCESS_KEY="${AWS_SECRET_ACCESS_KEY:?}"
  --from-literal=AWS_SESSION_TOKEN="${AWS_SESSION_TOKEN:?}"
  --from-literal=Messaging__QueueUrl="https://sqs.${AWS_DEFAULT_REGION}.amazonaws.com/${ACCOUNT_ID}/oficina-${ENVIRONMENT}-os-queue"
)
for EVT in os.criada os.cancelada; do
  SECRET_ARGS+=(--from-literal="Messaging__TopicArns__$EVT=$(ssm "/oficina/$ENVIRONMENT/messaging/topics/$EVT/arn")")
done
kubectl create secret generic os-service-secrets --namespace "$NS" "${SECRET_ARGS[@]}" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
echo "Secret os-service-secrets aplicado."

export ENVIRONMENT CADASTROS_URL IMAGE
envsubst '${ENVIRONMENT} ${CADASTROS_URL}' < k8s/configmap.yaml | kubectl apply -f -
envsubst '${IMAGE}' < k8s/deployment.yaml | kubectl apply -f -
kubectl apply -f k8s/hpa.yaml
# Service interno no lugar do NLB internet-facing do repositório
kubectl apply -f - <<YAML
apiVersion: v1
kind: Service
metadata:
  name: oficina-os-service
  namespace: $NS
  labels: {app.kubernetes.io/name: os-service, app.kubernetes.io/part-of: oficina-mecanica}
spec:
  type: ClusterIP
  selector: {app.kubernetes.io/name: os-service}
  ports: [{port: 80, targetPort: 5000, name: http}]
YAML
kubectl rollout restart deployment/oficina-os-service -n "$NS"
kubectl rollout status deployment/oficina-os-service -n "$NS" --timeout=5m
echo "OS Service no ar (interno): http://oficina-os-service.$NS.svc.cluster.local"
