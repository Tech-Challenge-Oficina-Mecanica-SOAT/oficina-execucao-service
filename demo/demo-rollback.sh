#!/usr/bin/env bash
set -euo pipefail

# Demo da compensacao da Saga (rollback): orcamento recusado cancela a OS.
# Prova tambem a idempotencia: o mesmo os.cancelada entregue 3 vezes gera 1 transicao.
# OS e Billing sao simulados pelo script (Wiremock no alvo local, ids gerados pelo script no alvo eks).
# Pre-requisito (alvo local): ambiente no ar e provisionado (demo/demo-setup.sh).
DEMO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$DEMO_DIR/lib/demo-lib.sh"

# fila_contem <osId>: sucesso se GET /fila lista a OS
fila_contem() {
  local fila
  fila="$(api GET "/fila")" || fail "Falha ao consultar a fila"
  [[ "$fila" == *"$1"* ]]
}

step "Abrir OS ($SIM_OS_ROTULO)"
abrir_os_simulada

step "OS Service publica os.criada${PUB_SUFIXO}"
publicar_evento os-criada os.criada "{\"osId\":\"$OS_ID\"}"
ok "Evento os.criada $PUB_VERBO"

step "Execucao Service processa os.criada"
expect_status "$OS_ID" AguardandoDiagnostico

step "Confirmar que a OS esta na fila de trabalho"
fila_contem "$OS_ID" || fail "OS $OS_ID deveria estar na fila (GET /fila) antes do cancelamento"
ok "OS presente na fila"

step "Registrar diagnostico (API real)"
PECA_ID="$(gen_uuid)"
SERVICO_ID="$(gen_uuid)"
api POST "/execucao/$OS_ID/diagnostico" \
  "{\"pecas\":[{\"pecaId\":\"$PECA_ID\",\"quantidade\":2}],\"servicos\":[{\"servicoId\":\"$SERVICO_ID\",\"quantidade\":1}],\"tempoEstimadoHoras\":3.5,\"observacoes\":\"Troca de pastilhas\"}" >/dev/null \
  || fail "Falha ao registrar diagnostico"
ok "Diagnostico registrado HTTP 2xx"
evento="$(esperar_evento diagnostico.concluido "$OS_ID")"
info "$evento"
ok "diagnostico.concluido recebido na fila de verificacao (consumido para nao deixar lixo na fila)"
expect_status "$OS_ID" DiagnosticoConcluido

step "Billing Service: o cliente recusa o orcamento"
info "$PUB_INFO_RECUSA"
info "A API do Execucao nao consome orcamento.recusado: ela reage ao cancelamento da OS."

step "OS Service publica os.cancelada 3 vezes com o MESMO eventId (entrega duplicada)${PUB_SUFIXO}"
EVENT_ID="$(gen_uuid)"
info "eventId: $EVENT_ID"
for i in 1 2 3; do
  publicar_evento os-cancelada os.cancelada "{\"osId\":\"$OS_ID\",\"motivo\":\"orcamento_recusado\"}" "$EVENT_ID"
  info "Entrega $i $PUB_VERBO_F"
done
ok "3 entregas de os.cancelada $PUB_VERBO_PL"

step "Execucao Service compensa: status Cancelado"
expect_status "$OS_ID" Cancelado
info "Aguardando as entregas duplicadas serem consumidas..."
sleep 3

step "Idempotencia: exatamente 1 transicao para Cancelado"
historico="$(api GET "/execucao/$OS_ID/historico")" || fail "Falha ao consultar historico"
total="$(printf '%s' "$historico" | { grep -o '"statusNovo"[[:space:]]*:[[:space:]]*"Cancelado"' || true; } | wc -l | tr -d '[:space:]')"
[ "$total" = "1" ] || fail "Esperada 1 transicao para Cancelado, encontradas $total"
ok "Idempotência: 3 entregas, 1 transição"

step "Fila liberada: a OS cancelada saiu da fila"
if fila_contem "$OS_ID"; then
  fail "OS $OS_ID ainda aparece em GET /fila apos o cancelamento"
fi
ok "OS ausente da fila"

step "Resumo: historico de transicoes"
info "$historico"
novos="$(printf '%s' "$historico" | { grep -o '"statusNovo"[[:space:]]*:[[:space:]]*"[^"]*"' || true; } | sed 's/.*:[[:space:]]*"\(.*\)"/\1/')"
[ -n "$novos" ] || fail "Historico vazio"
cadeia=""
while IFS= read -r s; do
  [ -n "$s" ] || continue
  cadeia="${cadeia:+$cadeia → }$s"
done <<< "$novos"
ESPERADO="AguardandoDiagnostico → DiagnosticoConcluido → Cancelado"
[ "$cadeia" = "$ESPERADO" ] || fail "Cadeia de status inesperada. Esperado: $ESPERADO | Obtido: $cadeia"
ok "$cadeia"

banner_sucesso "$BANNER_ROLLBACK"
