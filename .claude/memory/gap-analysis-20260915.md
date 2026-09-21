# Gap Analysis — 20260915

- Repository: /home/ubuntu/repos/agent-harness | Branch: main | Commit: fef0a20
- Phase reached: issues (specs Approved + merged via PR #84; execution handoff pending user)
- Mode: analyze

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| .specs/ | present | 40 files |
| docs/ | present | bilingual, contains stale Blazor Server refs |
| docs/architecture/ | present | |
| .claude/CONTEXT.md | present | |
| CLAUDE.md / AGENTS.md / README.md | present | stale UI stack entries |
| tests / linters / CI | present | 196 unit + 47 integration green; CodeQL + SonarCloud green |
| gh auth + remote | ok | afonsoft/agent-harness |
| Deployment | present | docker container `taskboard` @ fef0a20 verified |

## 2. AS-IS × TO-BE matrix

| Topic | AS-IS | TO-BE | Sources |
| --- | --- | --- | --- |
| API authorization | Only `github`/`agents` groups + a few endpoints carry RequireAuthorization; `/api` root group anonymous | "every API endpoint keeps RequireAuthorization" | SPEC-20260915-blazor-wasm-migration guardrails; Program.cs:299,559,603,944 |
| Docs/UI stack | "Blazor Server" in CLAUDE.md, AGENTS.md, README.md, docs/installation*.md, docs/technologies*.md | WASM client + RCL + server static host | src/Taskboard.Client/, Taskboard.Blazor.csproj |
| Non-Docker hosting | `dotnet exec` on build output in Production → `/` 404, `/framework-assets/*` 404 (WebRootFileProvider lacks static assets outside Development) | install.sh-driven installs must serve the SPA | install.sh:267,283; reproduced via `dotnet exec` test |
| RepositoryCombobox tests | Logic inline in .razor; zero unit coverage | "filter/validation helper unit tests ... keep logic in a testable static/pure helper" | SPEC-20260915-repo-search-combobox T5 |
| Skills sync scope | Installs into container's `/root` home dirs | User intent may be host CLI dirs | deployment topology — user decision |
| skills-lock.json | Exists at repo root; referenced only by docs/memory; new sync ignores it | Convention: skills pinned by SHA-256 lockfile | CLAUDE.md/.claude/MEMORY.md refs; no code reader |
| SPEC statuses | 3 new SPECs at `Implemented` post-merge | convention `Done (merged via PR #N)` | SPEC-20260914-stale-spec-status precedent |

## 3. Candidates and verdicts

| Key | Category | Verdict | Priority | Spec | Issue | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| GAP-security-api-anonymous-surface | security | CONFIRMADO | high | .specs/SPEC-20260915-api-authorization-hardening.md | #82 | anon curls: GET /api/tasks 200, POST /api/projects 201, GET /api/local/jira-connection 200, GET /api/settings 200 (returns gitHubToken when configured — WhenWritingNull only hides null). Program.cs api group + projects/tasks/attachments/local groups lack RequireAuthorization |
| GAP-operation-nonpublish-hosting | operation | CONFIRMADO | high | .specs/SPEC-20260915-wasm-post-migration-hardening.md | #83 | `dotnet exec bin/Release/.../Taskboard.Server.dll` in Production: `/` 404, framework-assets 404 (reproduced). install.sh:283 execs build output directly |
| GAP-documentation-blazor-server-stale | documentation | CONFIRMADO | medium | .specs/SPEC-20260915-wasm-post-migration-hardening.md | #83 | CLAUDE.md:18, AGENTS.md:18, README.md:14/29/52, docs/installation(.pt-br).md:9, docs/technologies*.md:14; also missing Taskboard.Client in structure docs + SPEC statuses Implemented→Done |
| GAP-tests-repository-combobox | tests | CONFIRMADO | medium | .specs/SPEC-20260915-wasm-post-migration-hardening.md | #83 | no test file references RepositoryCombobox; no bUnit in test csproj; filter/keyboard logic inline in Components/Shared/RepositoryCombobox.razor |
| GAP-requirements-skills-lock-not-honored | requirements | INCONCLUSIVO | — | — | — | skills-lock.json not read by any code (grep src/ install.sh); sync installs upstream HEAD. Intended or oversight? user decision |
| GAP-operation-skills-sync-container-home | operation | INCONCLUSIVO | — | — | — | sync writes to container /root/.claude/skills etc., not host CLIs; matches SPEC literally but may not match deployment intent |
| redundant-UseStaticFiles | implementation | REJEITADO | — | — | — | app.UseStaticFiles() (Program.cs:1061) harmless alongside MapStaticAssets; server wwwroot empty |
| missing-favicon | implementation | REJEITADO | — | — | — | not spec'd; cosmetic |
| api-key-auth-scheme | requirements | REJEITADO | — | — | — | KnowledgeHub's aft_* scheme not in Taskboard scope |
| legacy-framework-assets-filename-route | implementation | REJEITADO | — | — | — | hardened boot.js only uses stem/ext + defaultUri |
| stale-spec-statuses | documentation | folded into GAP-documentation-blazor-server-stale | — | — | — | same fix class as SPEC-20260914-stale-spec-status (Done) |

## 4. Approval gate

- Decision: approved — both SPECs `Approved` by user, merged via PR #84 (commit 9ddcf48)
- Inconclusive gaps deferred by user: skills-lock honoring, container-home skills target

## 5. Issues

- Epic: gap-analysis-20260915 → https://github.com/afonsoft/agent-harness/issues/81
- Slices: S1 api-authorization-hardening → #82; S2 wasm-post-migration-hardening → #83

## 6. Orchestrator handoff

- Pending user decision — S1 (#82) is the urgent security fix; S2 (#83) can follow
