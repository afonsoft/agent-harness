# SPEC-20261015-chat-preview-panel: App Preview tab + element picker

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Preview tab — iframe app preview via loopback proxy + pick-element→quote |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Server (proxy) + Blazor (tab + overlay) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261015-chat-preview-panel` |
| Status | `Draft` |
| Depends on | SPEC-20261011-chat-workspace-panel (tab strip + panel) |
| Reference | Devin Preview (element picker quote box, wake-loads, pin on resize); `/vscode/` loopback proxy precedent |

## 1. Executive Summary

### Problem

The agent can launch the app it is building, but the user can't see it —
they'd need to know the port and leave the chat. Devin's Preview shows the
running app with an element picker whose quote lands back in the composer.

### Solution

1. **Loopback preview proxy**: `GET /preview/{port}/{**path}` → proxies to
   `127.0.0.1:port` (loopback only, authenticated) — same pattern as
   `/vscode/`. Serving the app under our origin makes the iframe
   same-origin → picker overlay works.
2. **Preview tab** (workspace panel): URL bar (`/preview/<port>` defaulting
   to announced port), iframe, reload, open-external, and **Pick element**
   toggle — an overlay script injected into the iframe highlights hovered
   elements; clicking sends `{selector, tag, text snippet, bounding}` to the
   parent via `postMessage` → lands as a **quote chip** in the composer
   (sent with the next message as context, not as a message itself).
3. **Run announces port**: `register_preview` chat tool (or
   `POST conversations/{id}/preview {url}`) lets the run/user pin the app
   URL; the tab shows it as default target and the header gets a small
   "app live" dot.

### Scope

In scope: proxy, tab, picker, quote-to-composer, port announce. Out of
scope: recording, multi-port selector UI beyond a numeric input, remote
(non-loopback) targets, interactive click-through into the app (that's the
Browser tab spec), sandbox port forwarding.

## 2. Requisitos

### Funcionais

- RF-001 `MapGet("/preview/{port:int}/{**path}")` streams
  `http://127.0.0.1:{port}/{path}` (headers forwarded, hop-by-hop stripped,
  `X-Frame-Options`/CSP `frame-ancestors` stripped, absolute `Location`
  rewritten through the proxy); port range 1024–65535; loopback literal
  only; auth required like `/vscode/`.
- RF-002 `POST /api/chat/conversations/{id}/preview {url}` stores
  `Conversation.PreviewUrl` (or `/preview/` path); `DELETE` clears;
  `register_preview` `IChatTool` `{url}` calls the same setter so the agent
  announces its own server.
- RF-003 `ChatPreviewTab.razor`: URL input + reload + open-external + pick
  toggle; iframe `src` = stored URL else `/preview/<last-used-port>` else
  placeholder state ("nenhum app anunciado — peça ao agente ou informe a
  porta").
- RF-004 Picker: parent injects overlay script into the same-origin iframe
  (`document.elementFromPoint`, outline + label); click captures
  `{cssSelector, tagName, id, classes, text≤120, html≤400, pageUrl}` →
  `postMessage` to parent → composer gains a removable quote chip;
  sending attaches it to the message payload (`quote` field → rendered as
  a small card + included in the model prompt as context block).
- RF-005 Quote chip displays `tag#id.class — "text…"` + page title; editing
  the composer keeps it; ⌫ removes.
- RF-006 "App live" indicator: header dot when `PreviewUrl` set and a
  lightweight probe `HEAD /preview/...` 200s (poll ≤30s while tab open).
- RF-007 Errors: proxy 502 → in-tab banner "app não respondeu na porta
  {port}" (never surfaces upstream stack); offline port → grey dot.

### Não-funcionais

- SSRF guard: host parsed and forced to `127.0.0.1`/`localhost` only — the
  port int is the only variable (no `host` param at all).
- Overlay script adds no listeners when picker off; `position:fixed`
  overlay never mutates app DOM beyond a single `<div id=__pick>` it
  removes on exit.
- i18n en-US/pt-BR/es.

## 3. Arquitetura

```mermaid
flowchart LR
  Agent -->|register_preview url| Conv[Conversation.PreviewUrl]
  Tab[ChatPreviewTab] --> Proxy[GET /preview/{port} → 127.0.0.1]
  Proxy --> App[agent's app]
  Tab -->|pick toggle| Overlay[injected picker overlay]
  Overlay -->|postMessage element| Composer[quote chip → next message ctx]
```

- Proxy handler mirrors `VscodeProxyEndpoints` conventions (streaming,
  no buffering of large bodies, `X-Forwarded-*` sane).
- Picker overlay is a JS module `chat-preview-picker.js` + Blazor interop;
  same-origin guaranteed by the proxy — never injected into arbitrary
  external pages.

## 4. Fases

- **P1** — proxy + Preview tab + announce (tool + endpoint).
- **P2** — element picker + quote chip + live dot.

## 5. Testes

- Unit: proxy allowlist (rejects port <1024, non-int, host param absent);
  Location rewrite; PreviewUrl set/clear; `register_preview` tool result
  shape.
- Integration: spin a `dotnet`/static server on :5xxx → `GET
  /preview/5xxx/` 200s; quoted element reaches the message payload.
- UI guards: placeholder state, picker toggle aria, chip removal.
- E2E (later, recording): serve demo app → pick button → chip → send →
  model references the element.

## 6. Open questions

1. Store multiple preview URLs per conversation? Proposed: one current +
   history list (`Conversation.PreviewUrls` capped 10) — UI shows current,
   dropdown for history.
2. Should the proxy rewrite HTML asset URLs (like a full site proxy)?
   Proposed: no — apps usually work under any base path when served at
   `/preview/<port>/`; revisit only for apps with absolute-root assets.
