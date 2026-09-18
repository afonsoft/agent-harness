# SPEC-20260918-skills-cache-permissions

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `skills-cache-permissions` |
| Type | `Bugfix` |
| Stack | `.NET 10 / filesystem / process runner` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-skills-cache-permissions` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** usuário do Harness
**I want** que instalar e sincronizar skills funcione mesmo quando o cache local foi criado por outro usuário (root)
**So that** o passo `install.sh` não quebre com "Access to the path ... is denied" e eu receba um diagnóstico claro com recuperação automática.

**Problem context:**
Log de produção:

```
Skills sync started — repository 'afonsoft/skills', 12 target(s).
Skills sync failed: Access to the path '/home/ubuntu/.taskboard/data/skills-cache/scripts/skills-sh-audit.py' is denied.
Step install-sh: Failed — Access to the path '~/.taskboard/data/skills-cache/scripts/skills-sh-audit.py' is denied.
```

Causa raiz confirmada: `~/.taskboard/data/skills-cache/` (e arquivos dentro) estava `root:root` mode `644` — criado por uma execução anterior como root (ex.: serviço rodando como root antes de virar `ubuntu`, ou `install.sh` manual com sudo). O app roda como `ubuntu` e não consegue ler/sobrescrever o clone do repo `afonsoft/skills` nem executar scripts dele.

Workaround operacional já aplicado: o cache root-owned foi movido para `skills-cache.root-stale`. Este SPEC cobre o **fix permanente no código**.

## 2. Scope

**In scope:**
- Detecção de cache inacessível no início de install/sync: se `skills-cache` existe mas não é readable/writable/executable pelo usuário do processo → caminho de recuperação.
- Recuperação: renomear o diretório para `skills-cache.inaccessible-{timestamp}` (rename dentro do mesmo filesystem funciona mesmo com conteúdo de outro dono, desde que o pai seja writable) e re-clonar limpo. Se nem o rename for possível → falhar com erro explícito "cache owned by another user; remove or chown {path}".
- Garantir que o clone e checkout subsequentes são criados pelo usuário do serviço (nunca sudo/root).
- Pré-verificação de permissões antes de cada step (`npx-add`, `install-sh`, `sync-copy`): falha rápida com mensagem apontando o path problemático em vez de exception genérica de IO no meio do passo.
- Passar para `install.sh` um ambiente controlado: HOME do usuário do serviço, sem `sudo`, cwd dentro do cache — e validar que scripts executáveis têm bit `+x` (reaplicar `chmod +x` após clone se necessário — clone preserva, mas cache legado pode não).
- Relatório por step já existente (`npx-add`, `install-sh`, `sync-copy`) — estender com o novo step implícito `cache-prepare` quando a recuperação rodar, registrando "recovered from inaccessible cache".

**Out of scope:**
- `chown`/`sudo` automatizado — o serviço não deve escalar privilégio; rename+reclone resolve.
- Suporte a cache compartilhado multiusuário — o cache é privado do serviço.
- Mudança no formato do manifesto `skills-lock.json` ou no conteúdo das skills.

## 3. Technical Context

**Files to read:**
- `src/Taskboard.Integrations/Skills/` — serviço de install/sync (clone, cache path, steps) e `ISkillsInstallRunner`
- `src/Taskboard.Application` ou `Integrations` — onde `~/.taskboard/data/skills-cache` é resolvido (provável `SkillCacheOptions`/config `Taskboard:Skills:*`)
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` — superfície dos erros
- Testes existentes de skills (fixtures de filesystem)

**Regras de filesystem (POSIX):**
- Renomear `dir/` exige write+execute **no pai**, não no dir — o pai `~/.taskboard/data` é `ubuntu`-owned, então rename sempre funciona.
- `git clone`/`git pull` dentro de dir de outro dono falha com permission denied — detectar antes com teste de acesso (`File.Create` probe ou `AccessCheck`/`stat`).
- `install.sh` executado via `Process.Start` herda uid do processo — nunca prefixar com `sudo`.

## 4. Functional Requirements

- **RF-001** Antes de qualquer step, `EnsureCacheAccessibleAsync` verifica: dir existe → testa read+write+execute (probe real de criar/deletar arquivo temp). Inacessível → tenta `Directory.Move(cache, cache + ".inaccessible-" + ts)`; sucesso → re-clone limpo; falha no move → step `cache-prepare` = Failed com mensagem acionável.
- **RF-002** Após (re)clone, garantir `*.sh` executáveis (`chmod +x` nos scripts do repo — idempotente).
- **RF-003** `install.sh` e `npx skills add` rodam com `HOME` = home do usuário do processo e cwd = cache dir; nunca `sudo`/elevação.
- **RF-004** Logs: step `cache-prepare` reporta `Recovered`/`Failed`; erros de permissão incluem o path absoluto e o uid esperado.
- **RF-005** Sync após install parcial com cache recém-recuperado funciona end-to-end (12 targets).
- **RF-006** Idempotente: segunda execução com cache saudável não re-clona (pull incremental como hoje).

## 5. API Contract

Sem mudança de contrato HTTP. Comportamento interno dos endpoints existentes de skills (`POST /api/local/skills/install`, `/sync`, `/verify`).

## 6. Acceptance Criteria

- **AC1** Dado cache root-owned, quando clico Install, então o sistema renomeia o cache, re-clona e `install-sh` conclui com sucesso — sem erro de permissão.
- **AC2** Dado cache inacessível e pai também inacessível (rename impossível), o step falha com mensagem indicando o path e a ação manual.
- **AC3** Dado cache saudável, install/sync se comportam como hoje (sem rename, pull incremental).
- **AC4** Scripts `.sh` do cache executam com bit `+x` garantido após clone.
- **AC5** Teste unitário simula dir sem permissão (fixture com chmod 000 ou owner simulado via abstraction de filesystem) e valida o caminho de recuperação.

## 7. Task Plan

- T1: `EnsureCacheAccessible` (probe + rename + reclone) no serviço de skills; unit tests com filesystem abstraction/temp dirs.
- T2: `chmod +x` pós-clone nos `.sh`; env controlado nos processos (`HOME`, cwd); teste do runner.
- T3: Report do step `cache-prepare` no resultado/log; integração no fluxo install+sync.
- T4: Deploy e validação end-to-end em produção (Install → Sync → Verify no Settings).

## 8. Organization Guardrails

- Nunca `sudo`/`chown` do processo — recuperação só via rename+reclone no filesystem do próprio usuário.
- `.inaccessible-*` stale dirs não são apagados automaticamente (podem conter dados do root) — log sugere limpeza manual.
- Não logar conteúdo de skills nem tokens; logs só com paths e status.

## 9. Definition of Done

- [x] Install/Sync com cache root-owned recupera e conclui.
- [x] Unit tests do caminho de recuperação; suites verdes.
- [ ] Validado em produção: Install → `install-sh` Succeeded → Sync → Verify.
- [x] docs/features (en/pt-br) mencionam auto-recovery do cache; SPEC → Done.
