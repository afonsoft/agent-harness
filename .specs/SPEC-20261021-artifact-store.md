# SPEC-20261021-artifact-store: armazenamento durável de artefatos grandes

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Artifact store — blobs de saída/entrega de workers |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Domain + EF + Application + Server (endpoints) + Integrations (dispatch payloads) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/artifact-store` |
| Status | `Draft` |
| Depends on | `worker_done` contract (deliverables metadata), mailboxes, `Run`/`Task` |
| Reference | Orca `src/main/artifacts` (store+offload); LangChain `_blob_offload` (mesma ideia, substrate diferente); README "Compared to Orca" gap ⑥ |

## 1. Executive Summary

### Problem

`worker_done` carrega `deliverables[]` como **metadados** (paths dentro da
worktree) e relatórios grandes truncam no mailbox/board — logs de build,
diffs grandes, `coverage/`, screenshots e bundles não têm casa durável:
somem com `worktree delete` ou incham colunas TEXT do SQLite.

### Solution

`Artifact` entity + content-addressed blob store em disco
(`~/.agent-harness/artifacts/<sha256>`): workers e tools declaram
artefatos (`artifact_put` ou paths no `worker_done`), o server copia para
o store, e referências leves (`artifactId`, sha256, size, mime) trafegam
no contract/feed/UI em vez do conteúdo. Download/streaming por endpoint;
retenção por política; GC de órfãos.

### Scope

In scope: store local CAS + put/get/list + attach em `worker_done` +
tool `artifact_put` + UI download + GC. Out of scope: backend R2/S3
(o `cloud` module ganha adapter depois — interface `IBlobStore` já
desacopla), dedup cross-repo além de sha256, versionamento de artefato.

## 2. Requisitos

### Funcionais

- RF-001 `Artifact` entity: `Id`, `Sha256`, `FileName`, `MimeType`,
  `SizeBytes`, `RunId?`, `TaskId?`, `WorktreePath?`, `Kind(report|log|
  diff|coverage|screenshot|archive|other)`, `CreatedAt`, `ExpiresAtUtc?`.
- RF-002 `IBlobStore` (Integrations): `Put(stream) → sha256` (write tmp +
  atomic move, dedup por hash), `Open(sha256)`, `Delete(sha256)`,
  `Exists(sha256)`; impl `LocalBlobStore` sob
  `~/.agent-harness/artifacts/` (subdirs por 2-hex prefix).
- RF-003 `artifact_put` tool: `{path|contentBase64, kind, fileName?, runId}`
  — path relativo à worktree da task; >100MB rejeita
  `artifact_too_large`; retorna `{artifactId, sha256, size}`.
- RF-004 `worker_done`: `deliverables[]` aceita `{path}` → server copia
  para o store e devolve `artifactId` no resultado; reports >32KB são
  auto-offloaded (`reportArtifactId` + preview de 4KB no mailbox).
- RF-005 Endpoints `GET /api/artifacts/{id}` (metadata),
  `GET /api/artifacts/{id}/content` (stream + `Content-Type` +
  `Content-Disposition` sanitizado), `GET /api/artifacts?runId=`.
- RF-006 UI: `worker_done` card no board/run detail lista artefatos com
  tamanho/ícone por kind + botão download; diff grande renderiza via
  artifact em vez de inline.
- RF-007 Retenção: `Taskboard:Artifacts:RetentionDays` (default 30) +
  `MaxStoreMb` (default 2048); `ArtifactGcService` diário deleta
  expirados + LRU até caber no cap; artefatos ligados a run pinned são
  imunes.
- RF-008 Feed `ArtifactStored` (kind=artifact, badge paperclip, sem som)
  para kinds `report|coverage|screenshot`.

### Não-funcionais

- Streaming — conteúdo nunca materializado inteiro em memória
  (`FileStream` + `StreamCopyAsync` 64KB buffers).
- SHA-256 verificado no put (mismatch → `artifact_corrupt`); path
  traversal no `fileName` sanitizado (`Path.GetFileName` + whitelist
  charset).
- Put concorrente do mesmo sha → dedup (lock por hash no store).
- SQLite guarda só metadados; blob nunca entra em coluna.

## 3. Arquitetura

```mermaid
flowchart LR
  W[worker_done / artifact_put] --> S[ArtifactService]
  S --> B[IBlobStore<br/>~/.agent-harness/artifacts/sha256]
  S --> M[(Artifact metadata)]
  UI[board / run detail] -->|GET content| EP[/api/artifacts]
  EP --> B
  GC[ArtifactGcService] --> B & M
```

- `ArtifactService` (Application) orquestra metadata+store; `IBlobStore`
  fica em Contracts para futuro adapter cloud.
- `DelegationTask`/run ganham `ArtifactIds` por join-table implícita
  (`Artifact.RunId`/`TaskId`).

## 4. Fases

- **P1** — entity + `LocalBlobStore` + `artifact_put` + endpoints +
  auto-offload de reports grandes.
- **P2** — `deliverables[]`→artifact copy + UI list/download + GC +
  retention config.
- **P3** — `IBlobStore` adapter stub cloud + feed `ArtifactStored` +
  render de diff-via-artifact na UI.

## 5. Testes

- Unit: put dedup por sha; corrupt detection; fileName sanitizado;
  >100MB rejeita.
- Unit: GC respeita retenção + cap + pinned runs.
- Integration: `artifact_put` com arquivo real → GET content devolve
  bytes idênticos; `worker_done` com path → artifactId no resultado;
  report grande → preview + `reportArtifactId`.
- Integration: worktree deletada após put → artefato sobrevive no store.

## 6. Open questions

1. Compressão (gzip) no store? Proposta: não em P1 — size guard já capa;
   `ContentEncoding` opcional entra se store passar de 2GB em uso real.
2. Artefatos devem sobreviver `run delete`? Proposta: sim (retenção por
   política, não por run) — GC é o único removedor.
3. Upload de anexo humano pelo chat vira artifact? Proposta: reusa o mesmo
   store em P3 — chat attachments hoje ficam em disco solto; unificar
   reduz superfícies.
