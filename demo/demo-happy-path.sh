#!/usr/bin/env bash
set -euo pipefail

# Demo da saga completa (caminho feliz) do Execucao Service.
# OS e Billing sao simulados (Wiremock + eventos publicados pelo script).
# Pre-requisito: ambiente no ar e provisionado (demo/demo-setup.sh).
DEMO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$DEMO_DIR/lib/demo-lib.sh"

step "Abrir OS (Wiremock simulando o OS Service)"
resp="$(curl -s -f -X POST "$WIREMOCK_URL/ordens-servico" -H 'Content-Type: application/json' -d '{}')" \
  || fail "Wiremock nao respondeu em $WIREMOCK_URL/ordens-servico"
OS_ID="$(printf '%s' "$resp" | json_field id)"
[ -n "$OS_ID" ] || fail "Resposta do Wiremock sem id: $resp"
info "$resp"
ok "OS aberta: $OS_ID"

step "OS Service publica os.criada"
publicar_evento os-criada os.criada "{\"osId\":\"$OS_ID\"}"
ok "Evento os.criada publicado"

step "Execucao Service processa os.criada"
expect_status "$OS_ID" AguardandoDiagnostico

step "Registrar diagnostico (API real)"
PECA_ID="$(gen_uuid)"
SERVICO_ID="$(gen_uuid)"
api POST "/execucao/$OS_ID/diagnostico" \
  "{\"pecas\":[{\"pecaId\":\"$PECA_ID\",\"quantidade\":2}],\"servicos\":[{\"servicoId\":\"$SERVICO_ID\",\"quantidade\":1}],\"tempoEstimadoHoras\":3.5,\"observacoes\":\"Troca de pastilhas\"}" >/dev/null \
  || fail "Falha ao registrar diagnostico"
ok "Diagnostico registrado (HTTP 204)"

step "Verificar diagnostico.concluido publicado de verdade"
evento="$(esperar_evento diagnostico.concluido "$OS_ID")"
info "$evento"
ok "diagnostico.concluido recebido na fila de verificacao"

step "Billing Service aprova o orcamento (Wiremock)"
ORCAMENTO_ID="$(gen_uuid)"
curl -s -f -o /dev/null -X POST "$WIREMOCK_URL/orcamentos/$ORCAMENTO_ID/aprovar" \
  || fail "Wiremock nao aprovou o orcamento $ORCAMENTO_ID"
ok "Orcamento $ORCAMENTO_ID aprovado"

step "Billing Service publica orcamento.aprovado"
publicar_evento orcamento-aprovado orcamento.aprovado "{\"osId\":\"$OS_ID\",\"orcamentoId\":\"$ORCAMENTO_ID\"}"
ok "Evento orcamento.aprovado publicado"

step "Execucao Service processa orcamento.aprovado"
expect_status "$OS_ID" AguardandoReparo

step "Iniciar reparo (API real)"
api POST "/execucao/$OS_ID/iniciar-reparo" >/dev/null || fail "Falha ao iniciar reparo"
ok "Reparo iniciado (HTTP 204)"
evento="$(esperar_evento execucao.iniciada "$OS_ID")"
info "$evento"
ok "execucao.iniciada recebido na fila de verificacao"

step "Finalizar execucao (API real)"
api POST "/execucao/$OS_ID/finalizar" '{"tempoRealHoras":3}' >/dev/null || fail "Falha ao finalizar"
ok "Execucao finalizada (HTTP 204)"
evento="$(esperar_evento execucao.finalizada "$OS_ID")"
info "$evento"
ok "execucao.finalizada recebido na fila de verificacao"

step "Resumo: historico de transicoes"
historico="$(api GET "/execucao/$OS_ID/historico")" || fail "Falha ao consultar historico"
info "$historico"
novos="$(printf '%s' "$historico" | { grep -o '"statusNovo"[[:space:]]*:[[:space:]]*"[^"]*"' || true; } | sed 's/.*:[[:space:]]*"\(.*\)"/\1/')"
[ -n "$novos" ] || fail "Historico vazio"
cadeia=""
while IFS= read -r s; do
  [ -n "$s" ] || continue
  cadeia="${cadeia:+$cadeia → }$s"
done <<< "$novos"
ok "$cadeia"
[ "${cadeia##* → }" = "Finalizado" ] || fail "Historico nao termina em Finalizado"

banner_sucesso "Saga completa (caminho feliz)"
