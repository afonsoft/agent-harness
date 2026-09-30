# SPEC-20260929-chat-webcli-docs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `chat-webcli-docs` |
| Type | `Docs` |
| Stack | `Markdown (docs/ en-us + pt-br)` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `docs/devin-20260929-chat-webcli-docs` |
| Ticket | [#397](https://github.com/afonsoft/agent-harness/issues/397) — epic [#395](https://github.com/afonsoft/agent-harness/issues/395) |
| Status | `Done` — entregue neste PR |

## 1. User Story

**As a** Harness operator reading the install/API docs
**I want** the new env vars (`HARNESS_WEB_CLI_AGENT_ENABLED`, `HARNESS_CHAT_*`)
and the `/api/local/chat/*` endpoint family documented
**So that** the reference docs agree with what shipped in PRs #391/#393.

**Problem context:**

PRs #391 (Web CLI Agent toggle + FinOps liveness) and #393 (provider chat)
shipped new configuration surface that never reached the reference docs:

- `docs/installation.md` env table (lines ~64-74) lists `HARNESS_PORT`,
  `HARNESS_DATA_DIR`, `HARNESS_URL`, `HARNESS_API_KEY`, `HARNESS_SKILLS_REPO`,
  `HARNESS_RAG_*`, `HARNESS_TERMINAL_ENABLED` — but not
  `HARNESS_WEB_CLI_AGENT_ENABLED`, `HARNESS_CHAT_TOOLS_ENABLED`,
  `HARNESS_CHAT_SEARCH_BACKEND`, `HARNESS_CHAT_SEARCH_URL`,
  `HARNESS_CHAT_SEARCH_API_KEY` (the aliases registered in
  `RuntimeConfigurationService` lines 79-96).
- `docs/api.md` documents the AI Chat thread/agent endpoints but not the
  `local/chat/*` family (providers CRUD, `providers/{id}/models`,
  conversations CRUD, `conversations/{id}/messages` SSE, `.../stop`,
  `images/{fileName}`) mapped in `Program.cs` lines ~1792-1960.
- `features.md`/`features.pt-br.md` already cover the feature narrative — the
  gap is only the env/API reference pages.

## 2. Scope

**In scope:**

- `docs/installation.md` + `docs/installation.pt-br.md`: add the 5 new
  `HARNESS_*` env vars to the env-var table with default/effect.
- `docs/api.md` + `docs/api.pt-br.md`: document the `/api/local/chat/*`
  endpoint family (same terse style as neighboring entries; auth = cookie or
  `X-Api-Key` like the rest of `/api`).

**Out of scope:**

- `features.md`/`features.pt-br.md` — already updated.
- New doc pages; restructuring api.md.
- Anything beyond docs (no code changes).

## 3. Technical Context

**Files to read before implementing:**

- `docs/installation.md` (env table ~lines 60-76) and its pt-br mirror.
- `docs/api.md` (AI Chat section ~line 416) and its pt-br mirror.
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
  lines 79-105 — canonical alias/default list.
- `src/Taskboard.Server/Program.cs` ~1792-1960 — the chat endpoint map.

**Files to modify:**

```text
docs/installation.md       [mod]
docs/installation.pt-br.md [mod]
docs/api.md                [mod]
docs/api.pt-br.md          [mod]
```

## 4. Requirements

### RF-001: Env vars no installation.md
- **Description:** The env-var table must list, with default and one-line
  effect: `HARNESS_WEB_CLI_AGENT_ENABLED` (`true`), `HARNESS_CHAT_TOOLS_ENABLED`
  (`true`), `HARNESS_CHAT_SEARCH_BACKEND` (`none`), `HARNESS_CHAT_SEARCH_URL`,
  `HARNESS_CHAT_SEARCH_API_KEY`. Both en-us and pt-br files.

### RF-002: Endpoints /api/local/chat/* no api.md
- **Description:** One compact entry covering:
  `GET|POST /api/local/chat/providers`, `PUT|DELETE .../providers/{id}`,
  `GET .../providers/{id}/models`, `GET|POST /api/local/chat/conversations`,
  `GET|PATCH|DELETE .../conversations/{id}`,
  `POST .../conversations/{id}/messages` (SSE stream),
  `POST .../conversations/{id}/stop`, `GET .../images/{fileName}` — noting
  masked-key DTO (`HasApiKey`/`KeyHint`) and SSE framing. Both en-us and pt-br.

### RF-003: Consistência
- **Description:** Values/defaults in docs must match the catalog source of
  truth (`RuntimeConfigurationService`). No other doc sections touched.

## 5. API Contract

N/A — documentation only (documents existing endpoints).

## 6. Acceptance Criteria

- [ ] **Dado** a tabela de env vars **quando** se busca
  `HARNESS_WEB_CLI_AGENT_ENABLED` **então** encontra entrada com default `true`.
- [ ] **Dado** `api.md` **quando** se busca `local/chat` **então** encontra a
  família de endpoints documentada.
- [ ] **Dado** pt-br mirrors **quando** comparados **então** cobrem os mesmos
  itens.

## 7. Task Plan

- [ ] **T1:** ler as 4 docs + fontes de verdade (catalog + Program.cs).
- [ ] **T2:** editar `installation.md` + `api.md` (en-us).
- [ ] **T3:** espelhar nos pt-br.
- [ ] **T4:** revisar diff (markdown lint manual — headers/tables válidas).
- [ ] **T5:** `Status = Done`, PR a partir de `docs/devin-20260929-chat-webcli-docs`.

## 8. Organization Guardrails

- Branch `docs/devin-20260929-chat-webcli-docs`; nunca push em `main`.
- Docs bilíngues (en-us + pt-br), mesmo tamanho/estrutura.
- Sem alteração de código.

## 9. Definition of Done

- [ ] 5 env vars documentadas (en + pt-br).
- [ ] Família `/api/local/chat/*` documentada (en + pt-br).
- [ ] SPEC `Status: Done`; PR aberto.
