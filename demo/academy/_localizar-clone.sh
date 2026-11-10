#!/usr/bin/env bash
# Funções para localizar o clone de outro serviço (usado por source nos scripts de demo/academy).
# localizar_clone <nome-da-pasta> <variavel> [subpasta-obrigatoria]
#   Se a variável de ambiente <variavel> já estiver definida, ela vale (override explícito).
#   Senão, sobe a árvore a partir deste arquivo procurando uma pasta irmã chamada <nome-da-pasta>.
#   Isso funciona também quando este repositório está num worktree aninhado (.claude/worktrees/...).
#   Define a variável e retorna 1 se não achar.
localizar_clone() {
  local nome="$1" var="$2" sub="${3:-.}" d
  if [ -n "${!var:-}" ]; then
    [ -d "${!var}/$sub" ] || return 1
    return 0
  fi
  d="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  while [ "$d" != "/" ] && [ "$d" != "." ]; do
    if [ -d "$d/$nome/$sub" ]; then printf -v "$var" '%s' "$d/$nome"; return 0; fi
    d="$(dirname "$d")"
  done
  return 1
}
