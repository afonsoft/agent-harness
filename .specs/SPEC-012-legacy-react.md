# SPEC-012 (legacy): Frontend React original

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Frontend React original (reference) |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation |
| Change type | Reference |
| Repository | afonsoft/agent-harness |
| Suggested branch | — |
| Technical owner | afonsoft |
| Status | Deprecated (referência) |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

> **Status:** Este spec está obsoleto. A implementação atual usa **Blazor Server** conforme `SPEC-008-frontend.md`. Este spec é mantido apenas como **referência** para mapear comportamento 1:1 com o frontend original durante a migração.

### Stack (legado)

- React 18 + TypeScript, **Vite** (`vite.config.ts`, `tsconfig.json`).
- `web/src/`: `App.tsx`, `main.tsx`, `api.ts`, `types.ts`, `storage.ts`, `i18n.tsx`, `actors.ts`, `aiChatState.ts`, `taskConversations.ts`, `taskFilters.ts`, `issueBoardStatuses.ts`, `issueRoute.ts`, `labels.ts`, `revisionPolling.mjs`, `embeddedHost.mjs`, `workflowStore.ts`, `styles.css`.
- 38 componentes em `web/src/components/`.
- `web/public/`: fontes (Inter), favicon, logos. `index.html` entry.

### Comportamento (legado)

- **Board view**: colunas por status; cards por `sort_order`; markdown GFM + mermaid read-only; HTML comments ocultos; raw HTML desabilitado.
- **SSE realtime**: `revisionPolling.mjs` + `/api/events` → atualiza board; reconnect faz full refresh.
- **AI chat**: `aiChatState.ts` consome `/api/local/ai/threads/:id/events`.
- **Embedded host**: `embeddedHost.mjs` injeta painel no Codex (sidebar + iframe).
- **i18n**: `i18n.tsx` + `workflowI18n.ts`.
- **Storage local**: `storage.ts` → `/api/client-storage`.

### Build

```bash
npm run build:web   # Vite → sirvo estático pelo backend
npm run dev         # Vite dev server
```

---

## 2. Agent Role

> Este spec não deve ser usado para novas implementações. Usar `SPEC-008-frontend.md` para Blazor.

---

## 3. Agent Autonomy Level

N/A (referência)

---

## 4. Product Context

### Legacy context

O frontend React original foi a base para mapeamento de funcionalidades durante a migração para .NET. Após a migração, foi substituido por Blazor Server.

### Relevant stack (legado)

- React 18/19
- Vite
- TypeScript
- Tailwind CSS (?)
- Markdown rendering
- SSE client

---

## 5. Task Definition

Este spec é apenas **referência**. Não implementar nada novo aqui.

---

## 6. Functional Requirements

Legacy. Não usar.

---

## 7. Business Rules

Legacy. Não usar.

---

## 8. Domain Modeling

Legacy. Não usar.

---

## 9. Expected Architecture

Legacy. Não usar.

---

## 10. API Contracts

Legacy. Não usar.

---

## 11. Application Contracts

Legacy. Não usar.

---

## 12. Persistence and Data

Legacy. Não usar.

---

## 13. Integrations

Legacy. Não usar.

---

## 14. Edge Cases and Error Scenarios

Legacy. Não usar.

---

## 15. Few-Shot Examples

Legacy. Não usar.

---

## 16. Non-Functional Requirements

Legacy. Não usar.

---

## 17. Mandatory Guardrails

Legacy. Não usar.

---

## 18. Expected Tests

Legacy. Não usar.

---

## 19. Acceptance Criteria

- [x] Mantido como referência apenas.
- [x] Não deve ser usado para novas implementações.

---

## 20. Implementation Plan

Este spec é apenas **referência**. Ver `SPEC-008-frontend.md` para implementação atual.

---

## 21. Rollback Strategy

N/A

---

## 22. Risks and Mitigations

N/A

---

## 23. Definition of Done

- [x] Marcado como deprecated.
- [x] Referência para mapeamento durante migração.

---

## 24. Key Reminder

> **Este spec está obsoleto.** Usar `SPEC-008-frontend.md` para a implementação atual de frontend com Blazor Server.