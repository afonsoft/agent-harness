# SPEC-20260910: Instalador do CLI `taskctl` em `/usr/local/bin` com alias `.bashrc`

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | CLI Installer `install-cli.sh` |
| Product / System | agent-harness |
| Module / Bounded Context | CLI / DevEx |
| Change type | Enhancement |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-install-cli-sh` |
| Technical owner | afonsoft |
| Status | `Done` (status corrigido — implementado e issues fechadas; reconciliado em 2026-09-17) |
| Date | 2026-09-10 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O `install.sh` principal cobre instalação completa: clone, build, `.NET tool` em `~/.taskboard/bin`, wrappers e skills. Não há um instalador leve que compile o CLI e o disponibilize diretamente em `/usr/local/bin` com um alias no shell, facilitando o uso rápido sem .NET tool manifest ou repositório clonado.

### Objective

Criar um script `install-cli.sh` (ou opção `--cli-only` no `install.sh`) que:

1. Compile o projeto `src/Taskboard.Cli`.
2. Publique o apphost para a plataforma atual em um diretório oculto (`~/.taskctl`).
3. Crie um link ou wrapper em `/usr/local/bin/taskctl`.
4. Adicione `alias taskctl="/usr/local/bin/taskctl"` no `~/.bashrc` de forma idempotente.

### Expected outcome

- Usuários com .NET 10 SDK conseguem `taskctl --help` em uma nova sessão de bash.
- Não polui `/usr/local/bin` com DLLs e dependências (ficam em `~/.taskctl`).
- Reinstalação atualiza o binário sem duplicar alias.

### Out of scope

- Substituir o `install.sh` principal.
- Instalar skills, servidor ou MCP.
- Suporte a empacotamento NuGet global tool (já coberto por `install.sh`).

---

## 2. Agent Role

> Shell/DevEx engineer focado em portabilidade Linux/macOS e integração com shell do usuário.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não modificar `.github/workflows` sem aprovação humana.
- Não executar `install-cli.sh` em produção durante testes sem dry-run.
- Não adicionar dependências externas além do .NET SDK.

---

## 4. Product Context

### Functional context

O `taskctl` é o CLI do Taskboard, escrito com `Spectre.Console.Cli`. O projeto já é `PackAsTool`, mas também pode ser publicado como executável via `dotnet publish -r <RID>`.

### Technical context

- `src/Taskboard.Cli/Taskboard.Cli.csproj` com `<OutputType>Exe</OutputType>` e `<AssemblyName>taskctl</AssemblyName>`.
- `dotnet publish -r <RID> --self-contained false` gera o apphost `taskctl` e DLLs de suporte.
- `~/.bashrc` é o ponto de entrada para aliases no Bash.

### Relevant files

- `install.sh`
- `src/Taskboard.Cli/Taskboard.Cli.csproj`
- `docs/installation.md`
- `.specs/SPEC-003-cli.md`

---

## 5. Task Definition

### Main task

Criar e documentar o instalador `install-cli.sh` que compila e expõe o `taskctl` em `/usr/local/bin` com alias `.bashrc`.

### Subtasks

1. Verificar .NET SDK 10.
2. Resolver o `RuntimeIdentifier` (RID) da plataforma.
3. Publicar `src/Taskboard.Cli` em `~/.taskctl`.
4. Criar `/usr/local/bin` se necessário.
5. Criar o wrapper ou symlink em `/usr/local/bin/taskctl`.
6. Adicionar alias idempotente em `~/.bashrc`.
7. Testar `taskctl --help` em shell novo.

### Do not do

- Não alterar o `Taskboard.Cli.csproj` sem justificar.
- Não instalar nada fora de `~/.taskctl` e `/usr/local/bin`.
- Não gerar binários self-contained (manter dependência do runtime compartilhado).

---

## 6. Functional Requirements

### FR-001: Verificação do .NET SDK

O script deve:

- Checar `command -v dotnet`.
- Validar major version == 10.
- Sair com mensagem clara se .NET 10 não estiver instalado.

### FR-002: Detecção de RID

Função `get_rid()` que retorne um dos valores suportados com base em `uname -s` e `uname -m`:

- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

Para Windows, o script pode retornar erro no primeiro momento ou usar `win-x64` se suportado. **O escopo inicial é Linux/macOS.**

### FR-003: Publicação do CLI

```bash
dotnet publish "src/Taskboard.Cli" \
  -c Release \
  -r "$(get_rid)" \
  --self-contained false \
  -o "$HOME/.taskctl" \
  /p:PublishSingleFile=false
```

O apphost será `$HOME/.taskctl/taskctl` (Linux/macOS) e os demais artefatos ficam no mesmo diretório.

### FR-004: Instalação em `/usr/local/bin`

- Criar `/usr/local/bin` se não existir.
- Criar ou atualizar o wrapper `/usr/local/bin/taskctl` com:

```bash
#!/usr/bin/env bash
set -euo pipefail
exec "$HOME/.taskctl/taskctl" "$@"
```

- Dar permissão de execução (`chmod +x`).
- Se `/usr/local/bin/taskctl` já existir e não for um wrapper gerenciado, perguntar ou fazer backup (`taskctl.bak.$(date +%s)`).

### FR-005: Alias em `.bashrc`

- Linha a adicionar:

```bash
alias taskctl="/usr/local/bin/taskctl" # taskctl-ai
```

- Adição idempotente: verificar se a linha exata ou um comentário/grep por `# taskctl-ai` já existe.
- Se existir comentário antigo, substituir a linha ou não fazer nada.
- Aceitar `.zshrc` como extensão futura, mas o escopo é `.bashrc`.

### FR-006: Modo dry-run

Flag `--dry-run` imprime as ações sem executar `dotnet publish`, `mkdir` ou `chmod`.

### FR-007: Reinstalação e atualização

- Se `~/.taskctl` existir, sobrescrever a publicação.
- Se `/usr/local/bin/taskctl` existir e for o wrapper gerenciado, atualizar.
- Se o alias já existir, não duplicar.

---

## 7. Business Rules

- O script instala em `/usr/local/bin`, que pode requerer `sudo` se o usuário não tiver permissão de escrita.
- O alias aponta sempre para `/usr/local/bin/taskctl`.
- O binário publicado não inclui o runtime (self-contained false).
- Nenhuma credencial é gerada ou gravada; o CLI consome a API via `TASKBOARD_URL`.

---

## 8. Domain Modeling

N/A.

---

## 9. Expected Architecture

```text
~/.taskctl/
  taskctl              # apphost
  taskctl.dll          # assembly
  *.dll                # dependências
/usr/local/bin/
  taskctl              # wrapper script
~/.bashrc
  alias taskctl="/usr/local/bin/taskctl" # taskctl-ai
```

O `install-cli.sh` fica na raiz do repositório (junto de `install.sh`) ou em `scripts/install-cli.sh`.

---

## 10. API Contracts

N/A.

---

## 11. Application Contracts

N/A.

---

## 12. Persistence and Data

N/A.

---

## 13. Integrations

- .NET SDK 10.
- `~/.bashrc` do usuário.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| .NET não encontrado | `dotnet` ausente | erro e exit 1 |
| .NET versão errada | SDK 8 ou 9 | erro e exit 1 |
| `~/.bashrc` ausente | novo shell | criar? ou avisar e adicionar manual? (preferir: criar vazio) |
| Alias já existe | `alias taskctl="..."` | substituir a linha se `# taskctl-ai` não marcado, senão ignorar |
| `/usr/local/bin/taskctl` é um binário real | conflito de nome | backup e substituir com wrapper apenas se confirmado pelo `--force` |
| RID não suportado | `win-x64` | mensagem de plataforma não suportada no primeiro momento |

---

## 15. Few-Shot Examples

```bash
# Instalação normal
./install-cli.sh

# Simulação
./install-cli.sh --dry-run

# Uso após abrir novo shell
taskctl project list --json
```

```bash
# Trecho do wrapper gerado em /usr/local/bin/taskctl
#!/usr/bin/env bash
set -euo pipefail
exec "$HOME/.taskctl/taskctl" "$@"
```

---

## 16. Non-Functional Requirements

### Performance

- Instalação < 60s em máquina com cache NuGet.
- Startup do `taskctl --help` < 500ms.

### Portability

- Suporte inicial: Linux x64/arm64, macOS x64/arm64.
- Bash 4.0+.

### Security

- Pode requerer `sudo` se `/usr/local/bin` não for gravável pelo usuário.
- Não expõe senhas.

---

## 17. Mandatory Guardrails

- Não executar `dotnet` como root.
- Não modificar `~/.bashrc` sem `--dry-run` ser testado primeiro.
- Não commitar `~/.taskctl` ou `/usr/local/bin`.

---

## 18. Expected Tests

### Shellcheck

```bash
shellcheck install-cli.sh
```

### Dry-run

```bash
./install-cli.sh --dry-run
```

### Instalação real

| Step | Validation |
|---|---|
| `ls -l /usr/local/bin/taskctl` | wrapper presente e executável |
| `grep taskctl-ai $HOME/.bashrc` | alias presente |
| new bash session: `taskctl --help` | prints help |

### Reinstalação

| Step | Validation |
|---|---|
| Run again | nenhum alias duplicado, binário atualizado |

---

## 19. Acceptance Criteria

- [ ] `install-cli.sh` (ou `--cli-only` no `install.sh`) existe e passa em `shellcheck`.
- [ ] Script verifica .NET 10 antes de publicar.
- [ ] Publicação gera apphost e DLLs em `~/.taskctl`.
- [ ] Wrapper `/usr/local/bin/taskctl` é criado/atualizado.
- [ ] Alias `taskctl` é adicionado em `~/.bashrc` sem duplicatas.
- [ ] `--dry-run` funciona sem alterar o sistema.
- [ ] `taskctl --help` funciona em nova sessão bash após `source ~/.bashrc`.

---

## 20. Implementation Plan

1. Criar `install-cli.sh` na raiz (ou `scripts/`).
2. Implementar `check_dotnet`, `get_rid`, `publish_cli`, `install_bin_wrapper`, `add_alias_bashrc`.
3. Suportar `--dry-run`.
4. Adicionar `shellcheck` no CI (sem modificar `.github/workflows` principal; pode ser feito via Makefile/just).
5. Testar em Linux x64.
6. Atualizar `docs/installation.md` e `docs/installation.pt-br.md` com a nova opção.

---

## 21. Rollback Strategy

- Remover `/usr/local/bin/taskctl`.
- Remover `~/.taskctl`.
- Remover a linha `alias taskctl="/usr/local/bin/taskctl" # taskctl-ai` do `~/.bashrc`.
- Fornecer um `uninstall-cli.sh` opcional.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Conflito com `taskctl` já existente em `/usr/local/bin` | Médio | Baixa | fazer backup antes de sobrescrever; exigir `--force` |
| `~/.bashrc` não recarregado em sessão atual | Médio | Alta | instruir `source ~/.bashrc` no final |
| RID mal detectado | Alto | Baixa | fallback para `linux-x64` com aviso |
| Publicação de apphost falha por `dotnet` | Médio | Baixa | checar .NET antes e mensagem clara |

---

## 23. Definition of Done

- [ ] SPEC revisado.
- [ ] `install-cli.sh` implementado.
- [ ] `shellcheck` limpo.
- [ ] Dry-run e instalação real testados.
- [ ] Docs de instalação atualizados.
- [ ] PR com exemplo de uso e captura de saída.

---

## 24. Key Reminder

> The SPEC is the contract. Scope limitado ao instalador CLI leve; não alterar `Taskboard.Cli.csproj` nem `install.sh` sem justificativa.

---

## Pending Questions

1. O script deve ficar na raiz como `install-cli.sh` ou ser uma opção `--cli-only` no `install.sh` existente?
2. Devemos suportar `.zshrc` no escopo inicial ou apenas `.bashrc`?
3. O alias deve apontar para o wrapper em `/usr/local/bin` ou diretamente para `~/.taskctl/taskctl`?

---

## Human Approval Checklist

- [ ] Comando e alias claros.
- [ ] Reinstalação idempotente.
- [ ] Riscos de conflito de nome documentados.
