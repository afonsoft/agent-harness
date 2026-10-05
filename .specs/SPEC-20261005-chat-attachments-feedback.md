# SPEC-20261005-chat-attachments-feedback: Composer attachments, message feedback & deliverables

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | File/image attachments in chat + per-message feedback + deliverables card |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Domain + Application(.Contracts) + EntityFrameworkCore + Server + Blazor WASM |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261008-chat-attachments` |
| Status | `Approved` — aprovado por afonsoft (2026-10-05) |
| Depends on | SPEC-20261005-chat-background-resume |
| Reference | COMPARISON-20261005-deepseek-harness §1/#10–#12 (`attachment`/`file-upload`/`read_image`, `feedback/message-*`, `present` + `workspace-changes`) |

## 1. Executive Summary

### Problem

Three polish gaps vs deepseek-harness:

1. **Composer is text-only** — no way to hand the model a file/screenshot
   ("analyse este CSV", "o que tem nessa imagem?"). They ship `file-upload`,
   an `attachment` store, and `read_image`; commands even declare whether
   they accept attachments.
2. **No per-message feedback** — they log `feedback/message-put|delete`
   (rating + note + category, editable, log-only). Feedback drives quality
   review and the finops/UX loop; ours has nothing.
3. **No deliverables surface** — they end a turn with a "files changed +
   declared deliverables" card (`present` tool + workspace diff). Our
   delegation worktrees show diffs, but a plain chat that wrote files
   leaves no trace of *what* changed.

### Solution

1. **Attachments**: `ChatAttachment` entity (conversationId, messageId,
   fileName, contentType, byteSize, storagePath, sha256). Upload flow —
   `POST /api/local/chat/conversations/{id}/attachments` (multipart) →
   staging row → `POST /messages {content, attachmentIds:[…]}` binds them;
   orphan staging rows >24h swept by retention. Wire: file bytes go through
   `ChatAttachmentStore` (`~/.agent-harness/attachments/`, same pattern as
   `ChatImageStore`); transcript gets an attachment descriptor line +
   `read_file`/`read_image` can open `attach://{id}`. `read_image` gains
   the same `attach://` scheme; images render inline in the message bubble;
   non-images show a chip (name+size, click → download). Limits:
   `Chat:Attachments:MaxBytes` (8 MB), `MaxPerMessage` (5), allowed MIME
   allowlist (images/*, text/*, pdf, csv, json, md).
2. **Message feedback**: `ChatMessageFeedback` (messageId unique, rating
   `positive|negative`, category `FeedbackCategory` enum —
   `wrong|unhelpful|unsafe|slow|cost|other`, note ≤ 1000 chars, versioned
   CAS). `PUT /messages/{id}/feedback` upserts; `DELETE` clears. Assistant
   bubbles get 👍/👎 + optional note popover; sidebar filter flag "com
   feedback negativo" (P2). Feedback is **never** in the wire transcript.
3. **Deliverables**: `workspace-changes` equivalent — at run end the
   executor diffs the conversation workspace (git-aware when inside a
   worktree/repo, else whole-file snapshots captured around file-tool
   edits — reuse `FileSystemTools` edit hooks) → `chat.deliverables` SSE
   event `{changed:[{path, added, removed, source}]}` + persisted
   `ChatRunDeliverable` rows rendered as a card under the assistant message
   (path, +/− counts, click → read-only diff viewer). A `present` tool
   (non-mutating) lets the model declare key files deliberately.

### Scope

In scope: attachment upload/bind/download/`attach://` + inline render +
`read_image` scheme; feedback entity/endpoints/UI; deliverables diff card +
`present` tool; tests.

Out of scope: drag-drop paste-rich composer beyond file picker + paste
image (include paste-image in P1), attachment edit/replace, feedback
analytics page, remote storage backends, virus scanning (local-first app;
MIME allowlist + size cap only), video/audio types.

## 2. Requisitos

### Funcionais

- RF-001 `ChatAttachment` table + `attachments` endpoint group
  (`POST` multipart, `GET {id}/download`, `DELETE` staged). Staged → bound
  on send; orphan sweep.
- RF-002 `POST /messages` accepts `attachmentIds`; message row stores the
  bound set (FK or json); wire transcript includes one descriptor line per
  attachment `[attachment id={id} name={n} type={mime} bytes={b} — read via attach://{id}]`.
- RF-003 `attach://{id}` scheme resolvable by `read_file` (bytes→text for
  text/* and pdf-extractable text P2; raw descriptor otherwise) and
  `read_image` (image/* only, feeds the provider image path).
- RF-004 UI: paperclip button + paste-image → preview strip on the
  composer; chips on the bubble (image thumbnail inline); size/type
  rejection with toast.
- RF-005 `ChatMessageFeedback` + `PUT/DELETE /messages/{id}/feedback`;
  assistant bubbles show 👍/👎 state; 👎 opens note+category popover;
  CAS `Version` guards concurrent edits (409).
- RF-006 Feedback index page filter: conversation list gains `hasNegativeFeedback`
  chip in the filter row (P2).
- RF-007 `chat.deliverables` event + `ChatRunDeliverable` rows +
  changed-files card per completed run (git diff when repo, else tracked
  file-tool edits); `present` tool declares files (capability
  `tool:present`, default on).
- RF-008 Config: `Taskboard:Chat:Attachments:{Enabled,MaxBytes,
  MaxPerMessage,AllowedMime}` — master `Enabled` default true.

### Não-funcionais

- RNF-001 Attachment bytes stay out of `ChatMessage.Content` — transcript
  carries descriptors only; `attach://` resolves lazily per run.
- RNF-002 Upload validates MIME against allowlist by sniffed bytes, not the
  declared header.
- RNF-003 Feedback is log-only (never sent to the provider).
- RNF-004 Storage under `~/.agent-harness/attachments/`; sha256 dedup per
  conversation optional (store once).

## 3. Arquitetura

- **Domain**: `ChatAttachment`, `ChatMessageFeedback`,
  `ChatRunDeliverable` (+ EF configs + migration).
- **Application**: `ChatAttachmentStore` (hash-addressed files), descriptor
  builder in `BuildTranscriptAsync`, `DeliverablesService` (run-start vs
  run-end workspace snapshot diff).
- **Integrations**: `attach://` resolution in `FileSystemTools` +
  `read_image`; `PresentTool`.
- **Server**: attachments group, feedback endpoints, deliverables on run
  DTO/SSE.
- **Client**: composer strip + chips + feedback buttons + card.

## 4. Fases

- **P1** — attachments end-to-end (upload → wire → `attach://` → UI).
- **P2** — feedback + deliverables + `present` + negative-feedback filter.

## 5. Testes

- Unit: store put/get/hash; descriptor text; scheme resolution limits;
  feedback upsert/CAS/delete; deliverables diff on file-tool edits.
- Integration: upload → send → run sees descriptor → `read_image` resolves;
  feedback CRUD on assistant message; deliverables event after a
  `write_file` run.
- Blazor guard: composer strip, bubble chips, 👍/👎 toggling, card render.

## 6. Open questions

1. PDF text extraction in P1 — pull `UglyToad.PdfPig` or defer `attach://`
   pdf to P2 raw-bytes? Proposed: defer (text/* + images P1).
2. Attachments in forked conversations — copy rows or share storage?
   Proposed: share storage path (append-only), copy row references.
3. Feedback on tool messages too? Proposed: assistant messages only.
