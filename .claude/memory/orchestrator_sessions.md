# Orchestrator Sessions

## Session — 2026-09-20 23:25

**Scope**: Reconciliação pós-rename (`taskboard-ai` → `agent-harness`), fechamento de Epics E13–E16, verificação final de main.
**Decisions**: Issues #170/#171 fechadas com evidência de merge; SPECs todos em status terminal (nenhum Draft); SONAR_PROJECT_KEY deixado como `afonsoft_taskboard-ai` (workflow protegido + projeto Sonar existente).
**Delivered**: PR #246 (rename docs), #247 (Issues link no menu), #248 (fix testes do repo-selector), 2 re-deploys do `taskboard-server`, branches limpas (só main).
**Remaining**: Nenhum gap pendente. Decisão do usuário pendente: recriar projeto SonarCloud como `afonsoft_agent-harness` ou manter key antiga.
**Lessons**: Rename de repo exige re-verificar testes que dependem de ordem alfabética de fixtures (SelectedRepositoryServiceTests quebrou); `strings -e l` para achar UTF-16 em assemblies .NET; deploy exige `rm -rf publish/wwwroot/_framework` antes do publish.

## Session — 2026-09-21 (sessão 4)

**Scope**: 3 SPECs aprovadas do roadmap AI Code — thread-config (#288), chat-ux (#289) e acp-v2-readiness (#282), em fila sequencial com branches stacked.
**Decisions**: tooltips CSS `::after` do rail colapsado substituídos por `title=` nativo (incluído no PR #292); #289 stacked na #288 (depende do catálogo ACP-first); #282 stacked na #289 (mesmo AcpSessionClient); ACP v2 atrás de `IAcpDialect` por conexão — v1 default invisível, v2 opt-in via `Taskboard:Acp:MaxProtocolVersion=2`; `ITurnTracker` por dialeto para o lifecycle de turno divergente (response vs state_update idle).
**Delivered**: PR #292 (RF-002–006 thread-config + tooltips), PR #293 (chat-ux: renderers, fila, medidor, fork/retry, quick-switch), PR #294 (acp-v2: dialetos, negociação, parser v2, batch NDJSON, +47 testes). Unit 997/997; integration 257/259 (1 falha pré-existente de template-length reproduzida na base + 1 flake MCP).
**Remaining**: Os 3 PRs aguardam review/merge (ordem #292→#293→#294, stacked). Falha pré-existente `AgentRunEndpointsTests.Dado_TemplateMuitoLongo` continua aberta — endpoint não valida tamanho do template.
**Lessons**: NDJSON exige JSON compacto por linha — `JsonElement.GetRawText()` preserva pretty-print e quebra o framing (fake server inicial); `Channel<T>` + `WaitForAsync` sequencial consome itens não-matching — acumular respostas num único wait quando se espera N mensagens; flake MCP depende de carga paralela do xUnit.
