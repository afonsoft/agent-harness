# SPEC-20260919-harness-security-permission-gateway

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-security-permission-gateway` |
| Type | `Feature` (Harness Security & Governance) |
| Stack | `.NET 10 / AST Command Classifier / Security Sandbox / C# 14` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-harness-security-permission-gateway` |
| Ticket | `[A DEFINIR]` |
| Status | `Draft` |

---

## 1. User Story

**As an** administrador de sistema e desenvolvedor
**I want** um Gateway de Permissões e Segurança em tempo de execução no Harness
**So that** ferramentas, scripts e comandos de shell executados por agentes autônomos sejam dinamicamente inspecionados, classificados por nível de risco e confinados estritamente ao diretório do worktree, bloqueando ou exigindo aprovação humana para operações perigosas antes que qualquer dano ocorra.

### Problem Context

Dar a um agente de IA acesso a ferramentas como `bash`, `write_file` ou execução de processos locais envolve riscos sérios de segurança:
1. **Comandos Destrutivos:** Um agente confuso ou sob alucinação pode emitir comandos catastróficos (ex: `rm -rf /`, deleção de branches remotas com `git push --force`, ou instalação de pacotes maliciosos).
2. **Escape de Diretório (Path Traversal):** Sem checagem estrita no dispatch de tools, o agente pode alterar arquivos de sistema (`/etc`, `~/.ssh`) em vez de se limitar à pasta do projeto.
3. **Vazamento de Segredos e Credenciais:** Tokens do GitHub, chaves de API e variáveis de ambiente do host podem ser ecoados no stdout ou comitados em repositórios públicos.

---

## 2. Scope

### In scope

- **Classificação Dinâmica de Comandos e Tools (`ICommandRiskClassifier`):**
  - Avaliação de risco em tempo de despacho (antes de invocar a tool ou processo).
  - Níveis de risco:
    - `Safe / ReadOnly`: Leitura de arquivos, `git status`, `ls`, compilação, testes.
    - `WorkspaceWrite`: Criação e edição de arquivos dentro do worktree designado.
    - `Dangerous / HighRisk`: Deleção recursiva (`rm -rf`), operações de rede externas não autorizadas, comandos com elevação (`sudo`), comandos que tocam fora do worktree.
- **Confinamento Estrito de Filesystem (Path Jail):**
  - Validação de qualquer caminho passado para `read`, `write`, `edit` ou comandos de terminal: caminhos fora de `worktreePath` são rejeitados imediatamente com `SECURITY_ACCESS_DENIED`.
- **Filtro e Mascaramento de Segredos:**
  - Varredura de stdout/stderr por padrões de chaves conhecidas (GitHub PATs, tokens Bearer, AWS keys) com substituição por `[REDACTED_SECRET]`.
- **Integração com Approval Gate (HITL):**
  - Comandos de risco alto pausam a execução e emitem requisição de aprovação para o Cockpit com timeout de 5 minutos (default: `Deny` se expirar).

### Out of scope

- Virtualização baseada em hardware ou hypervisors (KVM).
- Substituição de firewalls corporativos.

---

## 3. Technical Context

### Where the change happens

- **Contracts:** `Taskboard.Application.Contracts/Harness/Security/IPermissionGateway.cs`, `SecurityPolicyDto.cs`.
- **Integrations:** `Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs`, `PathJailValidator.cs`, `SecretScrubber.cs`.
- **Server:** Interceptor no pipeline de execução de ferramentas e processos.

### Files to read before implementing

- `src/Taskboard.Integrations/Execution/WithoutTaskboardEnv.cs`
- `src/Taskboard.Integrations/Workspace/WorkspaceService.cs`
- `src/Taskboard.Domain.Shared/ValueObjects/Sandbox.cs`

### Files to create or modify

```text
src/Taskboard.Domain.Shared/Harness/SecurityRiskLevel.cs             [new]
src/Taskboard.Application.Contracts/Harness/Security/IPermissionGateway.cs [new]
src/Taskboard.Application.Contracts/Harness/Security/ICommandRiskClassifier.cs [new]
src/Taskboard.Application.Contracts/Harness/Dtos/SecurityDtos.cs    [new]
src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs [new]
src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs    [new]
src/Taskboard.Integrations/Harness/Security/SecretScrubber.cs       [new]
tests/Taskboard.Tests.Unit/Harness/DynamicCommandClassifierTests.cs  [new]
tests/Taskboard.Tests.Unit/Harness/PathJailValidatorTests.cs         [new]
```

---

## 4. Requirements

### RF-001: Classificação Pré-Execução de Comandos Shell
- **Description:** Todo comando enviado para ferramentas de execução (ex: bash) deve ser analisado antes do fork do processo.
- **Rules:**
  - `git status`, `git diff`, `dotnet test`, `cat`, `grep`, `find` → `Safe`.
  - `dotnet build`, `dotnet restore`, `touch`, `mkdir` → `WorkspaceWrite`.
  - `rm -rf`, `sudo`, `chmod`, `chown`, `curl`, `wget`, `nc` → `Dangerous`.
- **Input → Output:** `ClassifyCommand("rm -rf src/")` → `SecurityEvaluation(RiskLevel.Dangerous, "Deleção recursiva detectada")`.

### RF-002: Enforcing do Sandbox de Caminho (Path Jail)
- **Description:** Qualquer tentativa de acessar ou escrever em caminhos absolutos ou relativos que resolvam fora da pasta do worktree deve ser abortada.
- **Rules:** Bloquear tentativas com `../..` ou symlinks apontando para fora do workspace.
- **Input → Output:** `ValidatePath("/etc/passwd", worktreeRoot)` → `SecurityException: Path escapes sandbox boundary`.

### RF-003: Sanitização de Logs e Prevenção de Vazamento
- **Description:** Todo stream de saída enviado para SignalR, banco de dados ou contexto do agente deve passar pelo `SecretScrubber`.
- **Rules:** Expressões regulares para detecção de tokens (ex: `ghp_[A-Za-z0-9_]{36}`, `sk-[A-Za-z0-9]{48}`) substituídas por `[REDACTED_SECRET]`.

### RF-004: Políticas de Sandbox Configuráveis
- **Description:** O usuário pode configurar o modo de segurança do run:
  - `Strict`: Requer aprovação para qualquer escrita ou comando não listado na allowlist.
  - `Standard`: Auto-aprova `Safe` e `WorkspaceWrite`; exige aprovação apenas para `Dangerous`.
  - `Autonomous`: Permite tudo dentro do worktree; bloqueia estritamente comandos com `sudo` ou que escapem do jail.

---

## 5. API Contract

```http
POST /api/harness/security/evaluate
Content-Type: application/json
{
  "toolName": "bash",
  "command": "rm -rf bin/ obj/",
  "worktreePath": "/home/ubuntu/.taskboard/worktrees/run_01j7abcde",
  "policy": "Standard"
}
→ 200 OK
{
  "allowed": true,
  "riskLevel": "WorkspaceWrite",
  "requiresApproval": false,
  "reason": "Deleção de pastas temporárias de build dentro do worktree permitida na política Standard"
}
```

---

## 6. Acceptance Criteria

- [ ] **Given** um comando malicioso como `rm -rf /`, **when** submetido à avaliação do gateway, **then** ele é classificado como `Dangerous` e bloqueado imediatamente.
- [ ] **Given** tentativa de escrita em arquivo fora do worktree (`/home/ubuntu/.ssh/authorized_keys`), **when** validada, **then** o gateway lança `SecurityAccessDeniedException`.
- [ ] **Given** uma saída de terminal contendo um token do GitHub, **when** processada pelo `SecretScrubber`, **then** a string do token é ocultada no log gravado e transmitido.

---

## 7. Task Plan

- [ ] **T1 — Contracts & Value Objects:** Definir interfaces, enums de risco e DTOs de segurança.
- [ ] **T2 — AST/Lexer Command Classifier:** Implementar parser de comandos bash separando binário, flags e argumentos.
- [ ] **T3 — Path Jail Validator:** Implementar validação canônica de caminhos com `Path.GetFullPath` e checagem de symlinks.
- [ ] **T4 — Secret Scrubber:** Implementar expressões regulares compiladas para sanitização de credenciais.
- [ ] **T5 — Unit Tests:** Suíte de testes agressiva com ataques de injection, path traversal e strings de tokens conhecidas.

---

## 8. Organization Guardrails

- Falhar fechado (*fail-closed*): se o classificador não conseguir determinar o risco de um comando complexo, deve tratá-lo por padrão como `Dangerous`.
- Nunca persistir ou logar o valor original do segredo detectado.

---

## 9. Definition of Done

- [ ] Gateway de segurança integrado ao ciclo de despacho de tools.
- [ ] Testes unitários com 100% de cobertura nos cenários de segurança crítica.
- [ ] Nenhuma brecha de path traversal ou command injection identificada nos testes.
