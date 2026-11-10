#!/usr/bin/env bash
# Funcoes compartilhadas pelos scripts de demo. Uso: source "$(dirname "$0")/lib/demo-lib.sh"
# Pre-requisitos do host: Docker e curl (sem jq, uuidgen ou AWS CLI).

# Git Bash no Windows converte argumentos que parecem caminhos (ex.: /000000000000/fila)
export MSYS_NO_PATHCONV=1

DEMO_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$DEMO_LIB_DIR/../.." && pwd)"
COMPOSE_FILE="demo/docker-compose.demo.yml"

export API_URL="${API_URL:-http://localhost:${API_PORT:-5000}}"
export WIREMOCK_URL="http://localhost:8080"
export API_KEY="execucao-internal-api-key-local-dev"
export AWS_DEFAULT_REGION="us-east-1"

VERIF_QUEUE_URL="http://localhost:4566/000000000000/oficina-execucao-verificacao-queue"

# Os caminhos do compose sao relativos a raiz do repositorio
cd "$REPO_ROOT"

_C_AMARELO=$'\033[1;33m'
_C_VERDE=$'\033[1;32m'
_C_VERMELHO=$'\033[1;31m'
_C_CIANO=$'\033[0;36m'
_C_RESET=$'\033[0m'
_PASSO=0

step() {
  _PASSO=$((_PASSO + 1))
  printf '\n%s▶ %d. %s%s\n' "$_C_AMARELO" "$_PASSO" "$*" "$_C_RESET"
}

ok() { printf '%s✅ %s%s\n' "$_C_VERDE" "$*" "$_C_RESET"; }

fail() {
  printf '%s❌ %s%s\n' "$_C_VERMELHO" "$*" "$_C_RESET" >&2
  exit 1
}

info() { printf '%s   %s%s\n' "$_C_CIANO" "$*" "$_C_RESET"; }

banner_sucesso() {
  local titulo="$1"
  printf '\n%s============================================================\n' "$_C_VERDE"
  printf '  🎉 %s\n' "$titulo"
  printf '============================================================%s\n' "$_C_RESET"
}

gen_uuid() {
  local u=""
  if [ -r /proc/sys/kernel/random/uuid ]; then
    u="$(cat /proc/sys/kernel/random/uuid)"
  elif command -v uuidgen >/dev/null 2>&1; then
    u="$(uuidgen)"
  elif command -v powershell.exe >/dev/null 2>&1; then
    u="$(powershell.exe -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r')"
  elif command -v powershell >/dev/null 2>&1; then
    u="$(powershell -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r')"
  fi
  [ -n "$u" ] || fail "Nao foi possivel gerar UUID (sem /proc, uuidgen ou powershell)"
  printf '%s\n' "$u" | tr 'A-F' 'a-f'
}

aws_local() {
  docker compose -f "$COMPOSE_FILE" exec -T localstack awslocal "$@"
}

topic_arn() { printf 'arn:aws:sns:us-east-1:000000000000:%s\n' "$1"; }

# publicar_evento <topico-nome> <eventType> <data-json> [eventId]
publicar_evento() {
  local topico="$1" event_type="$2" data_json="$3" event_id="${4:-}"
  [ -n "$event_id" ] || event_id="$(gen_uuid)"
  local envelope
  envelope="$(printf '{"eventId":"%s","eventType":"%s","eventVersion":"1.0","occurredAt":"%s","correlationId":"%s","producer":"demo-script","data":%s}' \
    "$event_id" "$event_type" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(gen_uuid)" "$data_json")"
  aws_local sns publish --topic-arn "$(topic_arn "$topico")" --message "$envelope" >/dev/null \
    || fail "Falha ao publicar $event_type em $topico"
}

# api <METHOD> <path> [body-json]: imprime o corpo; retorna nao-zero em HTTP >= 400
api() {
  local method="$1" path="$2" body="${3:-}"
  local args=(-s -w '\n%{http_code}' -X "$method" -H "X-Internal-Api-Key: $API_KEY")
  if [ -n "$body" ]; then
    args+=(-H 'Content-Type: application/json' -d "$body")
  fi
  local resp code corpo
  resp="$(curl "${args[@]}" "$API_URL$path")" || return 1
  code="${resp##*$'\n'}"
  corpo=""
  if [ "$resp" != "$code" ]; then corpo="${resp%$'\n'*}"; fi
  if [ -n "$corpo" ]; then printf '%s\n' "$corpo"; fi
  case "$code" in
    [0-9][0-9][0-9]) [ "$code" -lt 400 ] ;;
    *) return 1 ;;
  esac
}

# json_field <campo>: le JSON do stdin e imprime o valor string do campo
json_field() {
  # grep -o separa as ocorrencias; head pega a PRIMEIRA (o sed guloso pegaria a ultima)
  grep -o '"'"$1"'"[[:space:]]*:[[:space:]]*"[^"]*"' | head -n 1 | sed 's/^[^:]*:[[:space:]]*"\(.*\)"$/\1/' || true
}

exec_status() {
  api GET "/execucao/$1" 2>/dev/null | json_field status || true
}

# expect_status <osId> <esperado>: poll de 1 s por ate 40 s
expect_status() {
  local os_id="$1" esperado="$2" atual="" i
  for i in $(seq 1 40); do
    atual="$(exec_status "$os_id")"
    if [ "$atual" = "$esperado" ]; then
      ok "Status da execucao: $esperado"
      return 0
    fi
    sleep 1
  done
  fail "Timeout aguardando status '$esperado' (ultimo status: '${atual:-<nenhum>}')"
}

# esperar_evento <eventType> <osId>: imprime o JSON da mensagem da fila de verificacao
esperar_evento() {
  local event_type="$1" os_id="$2"
  local fim=$((SECONDS + 40)) linhas handle corpo
  local re_tipo='"eventType"[[:space:]]*:[[:space:]]*"'"$event_type"'"'
  local re_os='"osId"[[:space:]]*:[[:space:]]*"'"$os_id"'"'
  while [ "$SECONDS" -lt "$fim" ]; do
    linhas="$(aws_local sqs receive-message --queue-url "$VERIF_QUEUE_URL" \
      --max-number-of-messages 10 --wait-time-seconds 2 \
      --query 'Messages[].[ReceiptHandle,Body]' --output text 2>/dev/null | tr -d '\r')" || linhas=""
    if [ -n "$linhas" ] && [ "$linhas" != "None" ]; then
      while IFS=$'\t' read -r handle corpo; do
        [ -n "$handle" ] || continue
        if [[ "$corpo" =~ $re_tipo ]] && [[ "$corpo" =~ $re_os ]]; then
          aws_local sqs delete-message --queue-url "$VERIF_QUEUE_URL" --receipt-handle "$handle" >/dev/null
          printf '%s\n' "$corpo"
          return 0
        fi
        # nao e nossa: devolve a fila imediatamente
        aws_local sqs change-message-visibility --queue-url "$VERIF_QUEUE_URL" \
          --receipt-handle "$handle" --visibility-timeout 0 >/dev/null 2>&1 || true
      done <<< "$linhas"
    fi
    sleep 1
  done
  fail "Timeout aguardando evento $event_type para a OS $os_id"
}

verificar_ambiente() {
  if ! docker compose -f "$COMPOSE_FILE" ps --status running --services 2>/dev/null | grep -q '^localstack$'; then
    fail "Ambiente fora do ar. Rode: docker compose -f demo/docker-compose.demo.yml up -d --build"
  fi
}

verificar_ambiente
