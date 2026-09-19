# SPEC-20260919-harness-verification-loop

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-verification-loop` |
| Type | `Feature` (Harness Engine & Quality Gates) |
| Stack | `.NET 10 / CLI Process Runner / dotnet CLI / xUnit / Coverage Ratchet / C# 14` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-harness-verification-loop` |
| Ticket | [#164 — E9](https://github.com/afonsoft/taskboard-ai/issues/164) |
| Status | `Done` |

---

## 1. User Story

**As an** orquestrador de engenharia de software autônomo (Harness Engine)
**I want** executar uma bateria determinística de auto-verificação (compilação, formatação, testes unitários, testes de integração e checagem de cobertura) no worktree do agente
**So that** quaisquer quebras ou regressões sejam automaticamente detectadas e retroalimentadas ao agente em um loop fechado de correção (red-green-refactor) antes de submeter o código para revisão humana.

### Problem Context

Quando um agente de IA gera código, a taxa de sucesso imediato sem pequenos erros de compilação, tipos ou testes quebrados raramente é 100%.
No fluxo atual do `taskboard-ai`:
1. **Falso positivo de conclusão:** O agente CLI encerra o processo com exit code 0 e a issue é movida para Review, mesmo que o código não compile ou testes estejam falhando.
2. **Ciclo manual lento:** O desenvolvedor humano precisa abrir a máquina, rodar `dotnet build`/`dotnet test`, copiar as mensagens de erro do terminal, colar de volta no prompt do agente e esperar nova execução.
3. **Regressão de cobertura:** O agente frequentemente adiciona código sem testes, derrubando a cobertura global abaixo do `COVERAGE_THRESHOLD` do repositório (ratchet de qualidade violado).

---

## 2. Scope

### In scope

- **Motor de Verificação Automatizado (`IAutomatedVerificationEngine`):**
  - Execução sequencial de passos configuráveis no diretório do worktree:
    1. **Format/Lint:** `dotnet format --verify-no-changes` (opcional com auto-fix).
    2. **Build:** `dotnet build <solution> --configuration Release -p:TreatWarningsAsErrors=true`.
    3. **Test:** `dotnet test <solution> --no-build --configuration Release --collect:"XPlat Code Coverage"`.
    4. **Coverage Gate:** Avaliação contra o ratchet configurado (ex: ≥ 66.26%).
- **Loop Fechado de Auto-Correção (Feedback Loop):**
  - Se algum passo falhar, o harness captura o `stderr`, os erros de compilação formatados (`CSxxxx`) e as falhas de asserção dos testes (`Expected... Actual...`).
  - O harness empacota essas falhas em um prompt estruturado de correção:
    *"A compilação/teste falhou com os seguintes erros no seu worktree. Corrija o código preservando a arquitetura..."*
  - O agente é re-invocado para corrigir os arquivos alterados (máximo de 2 iterações por padrão).
- **Relatório Estruturado de Verificação (`VerificationReport`):**
  - Sumário de compilação, contagem de testes passados/falhados, percentual de cobertura aferido e status final (`Passed`, `Failed`, `EscalatedToHuman`).

### Out of scope

- Testes de performance ou benchmarks em tempo de execução.
- Suporte a stacks não baseadas em .NET nesta primeira fase (foco inicial nas soluções do próprio repositório .NET 10).

---

## 3. Technical Context

### Where the change happens

- **Contracts:** `Taskboard.Application.Contracts/Harness/IVerificationEngine.cs`, `VerificationReportDto.cs`.
- **Integrations:** `Taskboard.Integrations/Harness/Verification/DotNetVerificationEngine.cs`, `CompilerErrorParser.cs`, `TestFailureParser.cs`.
- **Domain:** `Taskboard.Domain/Entities/Harness/VerificationReport.cs`.

### Files to read before implementing

- `CLAUDE.md` (seções Build, Tests, Hard Rules e Coverage Ratchet)
- `.specs/SPEC-20260919-coverage-gate-ratchet.md`
- `src/Taskboard.Integrations/Execution/IProcessTreeSignaler.cs`

### Files to create or modify

```text
src/Taskboard.Domain.Shared/Harness/VerificationStatus.cs             [new]
src/Taskboard.Domain/Entities/Harness/VerificationReport.cs         [new]
src/Taskboard.Application.Contracts/Harness/IVerificationEngine.cs  [new]
src/Taskboard.Application.Contracts/Harness/Dtos/VerificationDtos.cs [new]
src/Taskboard.Integrations/Harness/Verification/DotNetVerificationEngine.cs [new]
src/Taskboard.Integrations/Harness/Verification/CompilerErrorParser.cs     [new]
src/Taskboard.Integrations/Harness/Verification/TestFailureParser.cs      [new]
src/Taskboard.Integrations/Harness/Verification/CoverageCalculator.cs      [new]
tests/Taskboard.Tests.Unit/Harness/DotNetVerificationEngineTests.cs       [new]
```

---

## 4. Requirements

### RF-001: Execução de Build com Warnings as Errors
- **Description:** O motor deve executar a compilação no worktree isolado garantindo que qualquer warning quebre o build.
- **Rules:** Se o build falhar, deve parsear a saída e extrair arquivo, linha, coluna, código de erro e mensagem descritiva.
- **Input → Output:** `RunBuildAsync(worktreePath)` → `BuildResult(IsSuccess, ErrorsList)`.

### RF-002: Execução de Testes com Coleta de Cobertura
- **Description:** Executar a suíte de testes xUnit do repositório no worktree.
- **Rules:** Capturar nome dos testes com falha, mensagem da asserção, stack trace e o arquivo cobertura `coverage.cobertura.xml`.
- **Input → Output:** `RunTestsAsync(worktreePath)` → `TestRunResult(Total, Passed, Failed, FailuresList, LineCoveragePercent)`.

### RF-003: Validação do Gate de Cobertura (Ratchet Rule)
- **Description:** Comparar a cobertura de linhas resultante com o `COVERAGE_THRESHOLD` vigente do projeto.
- **Rules:** Se `LineCoveragePercent < Threshold`, a verificação é marcada como falha com motivo `COVERAGE_BELOW_THRESHOLD`.

### RF-004: Síntese de Prompt de Auto-Correção
- **Description:** Formatar os erros encontrados em markdown enxuto para ser reinjetado no contexto do agente.
- **Input → Output:** Lista de erros → Prompt:
  ```markdown
  ## ❌ Verificação Falhou (Tentativa 1/2)

  ### Erros de Compilação:
  - `src/Taskboard.Server/Program.cs(42,10)`: error CS0246: The type or namespace name 'X' could not be found.

  ### Testes Falhando:
  - `Taskboard.Tests.Unit.HarnessTests.Dado_Quando_Entao`: Shouldly.ShouldAssertException: Expected true but was false.

  Por favor, corrija estes arquivos diretamente no seu workspace.
  ```

### RF-005: Limite de Iterações e Escalonamento
- **Description:** O ciclo de auto-correção não pode exceder `MaxRetries` (default: 2 iterações).
- **Rules:** Se após 2 correções os testes ainda não passarem, o run é pausado com status `EscalatedToHuman` e as evidências completas são salvas para o desenvolvedor analisar.

---

## 5. API Contract

```http
POST /api/harness/verification/run
Content-Type: application/json
{
  "worktreePath": "/home/ubuntu/.taskboard/worktrees/run_01j7abcde",
  "solutionFile": "Taskboard.sln",
  "minCoverageThreshold": 66.26,
  "enforceFormat": false
}
→ 200 OK
{
  "isSuccess": false,
  "status": "BuildFailed",
  "compilationErrors": [
    {
      "file": "src/Taskboard.Server/Program.cs",
      "line": 42,
      "column": 10,
      "errorCode": "CS0246",
      "message": "The type or namespace name 'X' could not be found"
    }
  ],
  "testSummary": null,
  "coveragePercent": 0.0,
  "feedbackPrompt": "## ❌ Verificação Falhou..."
}
```

---

## 6. Acceptance Criteria

- [x] **Given** um worktree com código com erro de sintaxe, **when** o motor executa `RunBuildAsync`, **then** retorna `isSuccess = false` com a lista estruturada de erros e linhas.
- [x] **Given** um build com warnings em projeto com `TreatWarningsAsErrors`, **when** o build é executado, **then** a verificação falha e reporta os warnings como erros impeditivos.
- [x] **Given** código com testes falhando, **when** `RunTestsAsync` é executado, **then** o relatório contém os nomes dos testes que falharam e os detalhes da asserção Shouldly.
- [x] **Given** testes passando mas cobertura abaixo do ratchet, **when** verificado, **then** falha com status `CoverageRegression`.
- [x] **Given** 2 tentativas de correção com falha contínua, **when** a terceira iteração falha, **then** o motor não re-invoca o agente e escala para intervenção humana.

**Edge cases:**

| Scenario | Input | Expected behavior |
|---|---|---|
| Timeout durante execução de testes | Teste com loop infinito | Timeout de 120s interrompe o processo e reporta `TestTimeoutException`. |
| Arquivo de cobertura não gerado | Falha no collector XML | O relatório reporta aviso e usa o resultado dos testes sem quebrar o motor. |

---

## 7. Task Plan

- [x] **T1 — Contracts & DTOs:** Criar `IVerificationEngine`, `VerificationReportDto`, `CompilationErrorDto` e enums.
- [x] **T2 — Parsers de Compilação e Testes:** Implementar `CompilerErrorParser` (regex em stdout do `dotnet build`) e parser de trx/xml de testes.
- [x] **T3 — Engine Implementation:** Implementar `DotNetVerificationEngine` orquestrando build, test e cálculo de cobertura.
- [x] **T4 — Integration no Agent Loop:** Conectar o motor ao `PipelineEngine` para acionar a auto-correção quando o estágio de verificação falhar.
- [x] **T5 — Unit Tests:** Testes unitários com saídas mockadas de `dotnet build` e `dotnet test` cobrindo cenários de sucesso, erro e warning.

---

## 8. Organization Guardrails

- O motor de verificação nunca deve alterar código automaticamente (exceto se flag explícita de `dotnet format` estiver ligada).
- A política de ratchet nunca pode ser relaxada em tempo de execução; o threshold configurado no repositório é soberano.

---

## 9. Definition of Done

- [x] Motor capaz de rodar build e test de forma isolada no worktree.
- [x] Parsers de erro de compilação e teste validados com testes unitários.
- [x] Loop de feedback de até 2 iterações operacional.
