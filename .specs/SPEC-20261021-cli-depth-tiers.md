# SPEC-20261021-cli-depth-tiers: cobertura e profundidade por CLI

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | CLI depth tiers — expandir catálogo e tiers de integração por CLI |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Domain.Shared (`AgentCliMap`, registries) + Integrations (hooks, collectors, scanners) + Application + Blazor (Agents admin) |
| Change type | Feature (rolling catalog) |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/cli-depth-tiers` |
| Status | `Draft` |
| Depends on | `AgentCliKind`, `AgentCliResumeSupport`, `AgentHookInstaller`, `ProviderUsageCollector`s, session scanners |
| Reference | Orca `docs/agents/supported` (~30 pickers, tiers: status hooks / session-history / usage / hot-swap / automations); README "Compared to Orca" gap ⑩ |

## 1. Executive Summary

### Problem

O Harness suporta 13 CLIs com integração assimétrica: hooks instalados só
em claude/codex, usage collectors só claude/codex/copilot, session-history
em 6 — enquanto Orca documenta ~30 pickers com tiers por agente. Sem uma
matriz explícita, cada CLI novo repete a análise "o que eu suporto" do
zero e o usuário não sabe o que esperar.

### Solution

`CliCapabilityMatrix` — declaração por CLI dos tiers suportados
(`launch|status-hooks|session-history|usage|hot-swap|automations|
permissions|resume`) + wizard de onboarding de CLI novo: implementar um
tier = preencher a célula da matriz + adapter mínimo. P1 formaliza a
matriz + fecha tiers baratos dos CLIs existentes; P2+ adiciona CLIs
novos por demanda (goose, amp, cursor, opencode-hooks…).

### Scope

In scope: matriz + endpoints `/api/agents/{cli}/capabilities` + fechar
gaps documentados dos CLIs atuais + onboarding template. Out of scope:
suportar os 30 CLIs de uma vez (matriz torna incremental), CLIs sem PTY
estável, emuladores/wsl exóticos.

## 2. Requisitos

### Funcionais

- RF-001 `CliCapabilityMatrix` (Domain.Shared): por `AgentCliKind` →
  `CliTiers {Launch, StatusHooks, SessionHistory, Usage, HotSwap,
  Automations, PermissionModes, Resume}` — cada tier `Supported|Planned|
  Unsupported|Unknown` + `Note`.
- RF-002 `GET /api/agents/{cli}/capabilities` → matriz do CLI + gaps;
  `GET /api/agents/capabilities` → matriz completa (usada na admin page
  "CLI coverage" — badge por tier).
- RF-003 Status-hooks tier: `AgentHookInstaller` estendido para CLIs com
  convenção de hook conhecida (opencode, cursor — verificar env/config
  de hooks de cada um); sem convenção → `Planned` com `Note`.
- RF-004 Session-history tier: scanner JSONL/transcript por CLI —
  reusa `ISessionScanner` com adapter por formato; alvo P1: +
  opencode+cursor+roo (formato documentado no adapter).
- RF-005 Usage tier: collectors para os providers restantes com endpoint
  conhecido (gemini quota api, codex já feito) ou `local_estimate` como
  tier mínimo (já existe — matriz expõe).
- RF-006 Hot-swap tier: marca `Planned` até SPEC hot-swap — matriz declara
  quais CLIs têm home-env conhecida (`CLAUDE_CONFIG_DIR`, `CODEX_HOME`,
  `GEMINI_CLI_HOME`, `XDG_CONFIG_HOME`-based).
- RF-007 Onboarding: `docs/cli-onboarding.md` + template `CliAdapter`
  checklist — implementar tier = célula da matriz + teste de fixture;
  admin page linka o doc por CLI `Unsupported`.

### Não-funcionais

- Matriz é data em Domain.Shared (não reflection) — descoberta real por
  adapter registered (`ICliCapabilityProbe` opcional por tier).
- Cada tier novo vem com teste de fixture (mesmo padrão dos scanners/
  collectors).
- CLI sem tier nenhum além de `launch` continua lançável — matriz é
  declarativa, não gate.

## 3. Arquitetura

```mermaid
flowchart TB
  M[CliCapabilityMatrix Domain.Shared] --> EP[/api/agents/capabilities]
  M --> Admin[Agents admin page]
  Hooks[AgentHookInstaller] -->|StatusHooks| M
  Scanners[ISessionScanner] -->|SessionHistory| M
  Collectors[usage collectors] -->|Usage| M
  Hot[account manager] -->|HotSwap| M
```

- Cada tier reporta-se na matriz via adapter; a matriz é a fonte pública
  (UI + tool `agent_capabilities`).

## 4. Fases

- **P1** — matriz + endpoints + admin "CLI coverage" + session-history
  para opencode/cursor + hooks para opencode.
- **P2** — usage tier extra (gemini/local_estimate exposto) + onboarding
  doc + `agent_capabilities` tool.
- **P3+** — CLIs novos por demanda (goose, amp, auggie…) — uma linha na
  matriz + adapters por tier.

## 5. Testes

- Unit: matriz completa por CLI enum; tier `Planned` carrega `Note`;
  endpoint devolve shape estável.
- Unit: novo scanner parseia fixture do formato alvo; hooks installer
  escreve convenção correta por CLI.
- Integration: `/api/agents/capabilities` lista todos os `AgentCliKind`;
  admin page renderiza badges.
- Integration: hook install em CLI alvo → arquivo de hook aparece na home
  fake; sessions do novo scanner aparecem no index.

## 6. Open questions

1. Matriz deve ser gerada por reflection dos adapters registrados (fonte
   de verdade = DI) ou declarativa (fonte de verdade = matriz, adapter é
   implementação)? Proposta: declarativa + teste que garante adapter
   registrado para cada `Supported`.
2. CLI novo sem repo público de formato de sessão — `Unknown` até
   pesquisa? Proposta: `Unknown` + issue template "cli research" que a
   matriz linka.
3. Tiers `PermissionModes`/`Automations` são flags por CLI ou globais?
   Proposta: por CLI (automations depende do CLI aceitar arg de prompt
   não-interativo) — matriz é o lugar de declarar.
