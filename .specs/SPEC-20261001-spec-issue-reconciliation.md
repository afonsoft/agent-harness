# SPEC-20261001-spec-issue-reconciliation: reconciliação de status SPECs × issues

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | SPEC status + GitHub issue reconciliation |
| Product / System | agent-harness |
| Module / Bounded Context | `.specs/`, GitHub issues |
| Change type | Docs / housekeeping |
| Repository | afonsoft/agent-harness |
| Suggested branch | `docs/devin-20261001-spec-reconciliation` |
| Technical owner | afonsoft |
| Status | Done — entregue via [PR #426](https://github.com/afonsoft/agent-harness/pull/426) (merged 2026-10-02) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Ticket / Gap | GAP-documentation-spec-status-drift + GAP-operation-stale-issues |
| Related SPECs | SPEC-20261001-chat-*, SPEC-20260930-sonar-*, SPEC-20260923-terminal-tabs-keyed-render |

---

## 1. Executive Summary

### Problem

Gap-analysis 2026-10-01 cruzou status de SPEC × evidência de implementação:

**SPECs implementadas mas com status Draft/stale (~50 arquivos):**

| SPEC | Evidência de implementação | Status atual |
|---|---|---|
| `20261001-chat-agent-delegation` | `RunAgentTool.cs` + delegation id único (B-07) | Draft |
| `20261001-chat-capability-registry` | `IChatCapabilityRegistry.cs`, `ChatCapabilityRules.cs` | Draft |
| `20261001-chat-mcp-client` | `ChatMcpClientManager.cs`, `IMcpClientManager.cs` | Draft |
| `20261001-chat-skills-slash-commands` | `SlashCommandPalette.razor` | Draft |
| `20261001-chat-ux-compact` | `chat-mini-terminal` em ProviderChat | Draft |
| `20261001-chat-default-mode` | fallback Chat→Agent em `AiChat.razor:606` | Draft |
| `20260930-settings-tabs` | `nav-tabs` 5 abas + `?tab=` deep-link em Settings.razor | Draft |
| `20260923-terminal-tabs-keyed-render` | PR #337 merged 2026-09-23 | Implemented (aguardando merge) |
| ~40 SPECs `20260930-sonar-*` | entregues via PR #415 (merged) | Approved |

**SPECs corretamente não-Done (pendência real, não drift):**

| SPEC | Lacuna verificada |
|---|---|
| `20260930-mobile-responsive-ui` | Parcial: viewport `interactive-widget` ✓, mas `inputmode`=0 ocorrências, `modal-fullscreen` <576px ausente, overlay `?` de atalhos ausente — manter Draft e executar |
| `20260915-wasm-post-migration-hardening`, `20260915-api-authorization-hardening`, `20260921-cockpit-live-logs-explorer-diff`, SPECs 001–015 `Implemented` | Status legado — reconciliar ou confirmar Done |

**Issues abertas com fix já mergeado:**

| Issue | Fix |
|---|---|
| #412 orphaned PTY | resolvido no PR #415 |
| #413 EF log flood | resolvido no PR #415 |
| #414 ObjectDisposedException | resolvido no PR #415 |
| #410 Sonar backlog | S7637 feito; backlog residual ~293 smells pós-exclusão docs (1332→~293) — atualizar contagem |

### Objective

`.specs/` reflete a realidade; issues fechadas quando entregues; pendências
reais identificadas para execução futura.

### Out of scope

- Implementar `mobile-responsive-ui` (SPEC própria já existe — Draft aguardando aprovação/execução).

---

## 4. Requirements

### RF-001 — Status drift → Done

Para cada SPEC com evidência completa de implementação + merge: atualizar
`Status` para `` `Done` — entregue via PR #NNN (merged YYYY-MM-DD) `` e
fechar o checklist de DoD. SPECs sonar-* podem receber nota em lote.

### RF-002 — Status legado "Implemented"

SPECs com `Status: Implemented` cujo merge é confirmado → `Done`. Sem
confirmação → manter e registrar pendência na seção Open Questions.

### RF-003 — Issues

- Fechar #412, #413, #414 com comentário citando PR #415 + commits.
- Comentar #410 com o estado residual real: gate ERROR causado por new-code
  findings do #418 (SPEC-20261001-sonar-new-code-cleanup), backlog residual
  ~293 smells fora de docs/, S7637 resolvido.
- NÃO fechar #410 — backlog global continua aberto.

### RF-004 — mobile-responsive-ui

Manter `Draft`; adicionar nota de auditoria na SPEC listando os ACs já
satisfeitos (viewport meta) vs pendentes (inputmode, modal fullscreen,
overlay `?`, auditoria overflow-x), para a próxima execução.

## 6. Acceptance Criteria

- AC-01: `grep -L Done .specs/SPEC-*` só retorna SPECs genuinamente
  pendentes (mobile-responsive-ui e não-verificadas registradas).
- AC-02: Issues #412/#413/#414 fechadas; #410 atualizada.
- AC-03: `scripts/tests/check-spec-status.test.sh` verde.

## 8. Task Plan

- [x] T1 — RF-001 (drift → Done, ~50 SPECs)
- [x] T2 — RF-002 (Implemented → Done)
- [x] T3 — RF-003 (issues)
- [x] T4 — RF-004 (nota mobile-responsive) + validação

## 9. Definition of Done

- [x] Status SPECs consistente com código mergeado.
- [x] Nenhuma issue fechada sem link para o fix.

## Execution Notes (2026-10-02, slice #423)

- 6 SPECs `chat-*` + `settings-tabs` → Done via PR #411 (merged 2026-09-30).
- `ai-chat-openwebui` → Done via PR #420; `terminal-tabs-keyed-render` → Done via PR #337; `api-authorization-hardening` → PR #85; `wasm-post-migration-hardening` → commit 4b43e36; `cockpit-live-logs-explorer-diff` → PR #296.
- 77 SPECs `20260930-sonar-*` → Done via PR #415 (exceto `s8970`, que permanece Deprecated/won't-fix).
- SPECs 000–015 (umbrella) → Done exceto `006-cloud` e `007-workflow-automation`, mantidos `Implemented` — pendência: verificar cobertura completa dos ACs umbrella antes de Done (sem PR de merge único rastreável).
- 3 SPECs `20260911-*` `Completed` → Done (sinônimo legado, reconciliado para AC-01).
- Issues: #412/#413/#414 fechadas citando PR #415; #410 atualizada com estado residual, permanece aberta.
- Permanecem `Approved` (em execução): `mobile-responsive-ui` (#424), `sonar-new-code-cleanup` (#422), esta SPEC (#423).
