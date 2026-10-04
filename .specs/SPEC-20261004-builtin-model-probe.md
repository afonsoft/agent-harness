# SPEC-20261004 — Model probe unificado nos builtins

## Contexto

Dois mecanismos de probe de modelos coexistiam sem compartilhar declarações:

- `AgentCliModels.Probes` — probes tipados com formato (Lines/TabSeparated/
  DevinModelsList) para OpenCode, Antigravity e Devin; alimenta o catálogo
  do chat (`AiChatCatalogService`), o diálogo Models e o snapshot background.
- `AgentCliSpec.ModelListArgs` (SPEC-20261007 RF-003) — args lines-only em
  OpenCode e Grok, consumidos só pelo endpoint
  `GET /api/agents/builtin/{agentType}/models`.

Resultado: **Grok declarava `models` no spec mas nunca era sondado** —
`ListAvailableAsync(AgentType.Grok)` retornava `[]` e o catálogo/diálogo
mostravam só a tabela estática. A duplicação do OpenCode (duas declarações
idênticas) deixava o descompasso estrutural.

## Requisitos

- **RF-001 Declaração única** — `AgentCliModels.ModelListProbe(type)`
  resolve: tabela `Probes` tipada (formatos especiais) vence; senão
  `AgentCliSpec.ModelListArgs` gera um probe `Lines`. `null` quando nenhum
  existe. Efeito: Grok passa a ser sondado em todos os consumidores
  (`/models/available`, catálogo `ai/catalog`, `CliProbeSnapshotService`
  refresh, endpoint builtin) sem nenhuma outra mudança.
- **RF-002 Dedup** — a entrada OpenCode de `Probes` (idêntica ao
  `ModelListArgs` do spec) é removida; cobertura vem da derivação.
- **RF-003 Somente comandos documentados** — `ModelListArgs` só é declarado
  onde existe comando headless de listagem documentado/verificado. Verificado
  2026-10-04: `codex` não tem subcomando de listagem (openai/codex#23279),
  `claude` só expõe `/model` interativo → ambos ficam na tabela curated.
  Set documentado atual: OpenCode `models`, Grok `models`, agy `models`
  (TabSeparated), devin `models list` (DevinModelsList).

## Non-goals

- Novos formatos de parser; novos comandos de CLI; probing de definições
  custom (já coberto por RF-008 do SPEC-20261007).
