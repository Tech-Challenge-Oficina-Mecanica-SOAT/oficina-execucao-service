#!/usr/bin/env bash
# Deploy do Billing no AWS Academy sem alterar o repositório do Billing (ver kustomization.yaml).
# Requer: credenciais da sessão exportadas (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN),
# o secret ghcr-pull-secret no namespace oficina e o clone do oficina-billing-service ao lado deste
# repositório (pasta irmã; o kustomization.yaml aponta para ele por caminho relativo).
# Uso: bash demo/academy/billing/deploy-billing-academy.sh
set -euo pipefail
export AWS_DEFAULT_REGION=${AWS_DEFAULT_REGION:-us-east-1} MSYS_NO_PATHCONV=1
ENV=homolog; NS=oficina
ssm() { aws ssm get-parameter --name "$1" --query Parameter.Value --output text; }
DB_ENDPOINT=$(ssm /oficina/$ENV/db/endpoint); DB_PORT=$(ssm /oficina/$ENV/db/port); DB_USER=$(ssm /oficina/$ENV/db/username)
DB_PASSWORD=$(aws secretsmanager get-secret-value --secret-id oficina/$ENV/db-password --query SecretString --output text)
kubectl create secret generic billing-academy-secrets -n $NS \
  --from-literal=ConnectionStrings__BillingDb="Host=$DB_ENDPOINT;Port=$DB_PORT;Database=oficina_billing;Username=$DB_USER;Password=$DB_PASSWORD;Trust Server Certificate=true" \
  --from-literal=AWS_ACCESS_KEY_ID="${AWS_ACCESS_KEY_ID:?}" --from-literal=AWS_SECRET_ACCESS_KEY="${AWS_SECRET_ACCESS_KEY:?}" \
  --from-literal=AWS_SESSION_TOKEN="${AWS_SESSION_TOKEN:?}" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
echo "Secret billing-academy-secrets aplicado."
# Licença do New Relic: reaproveita a do Secret compartilhado oficina-secrets (se existir)
NR=$(kubectl get secret oficina-secrets -n $NS -o jsonpath='{.data.NEW_RELIC_LICENSE_KEY}' 2>/dev/null | base64 -d || true)
if [ -n "$NR" ]; then
  kubectl create secret generic newrelic-license -n $NS --from-literal=license-key="$NR" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  echo "Secret newrelic-license aplicado."
fi
cd "$(dirname "${BASH_SOURCE[0]}")"
kubectl apply -k .
kubectl rollout restart deployment/oficina-billing-service -n $NS
kubectl rollout status deployment/cadastros-proxy -n $NS --timeout=3m
kubectl rollout status deployment/oficina-billing-service -n $NS --timeout=5m
echo "Billing no ar (interno): http://oficina-billing-service.$NS.svc.cluster.local"
