# SPEC-20261001-chat-skills-slash-commands: skills globais e slash commands no chat

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Chat Skills + Slash Commands |
| Product / System | agent-harness |
| Module / Bounded Context | Application + Presentation |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261001-chat-ux` |
| Technical owner | afonsoft |
| Status | Draft (pending human approval) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20261001-chat-capability-registry, SPEC-20261001-chat-ux-compact |

---

## 1. Executive Summary

### Problem

`SkillDiscoveryService` já varre `~/.claude/skills`, `~/.devin/skills`,
`~/.cursor/skills`, `~/.opencode/skills` e `skills/` — mas esse catálogo
só alimenta a tela de Agent Skills. O chat não pode usar nenhuma skill:
nem o modelo (via tool) nem o usuário (via slash command). aaPanel faz
exatamente isso com o `SkillManager` global + `tools/skill.py`; OpenCode
e Open WebUI oferecem `/` para invocar prompts/skills na caixa de texto.

### Objective

Duas superfícies:

1. **Skill tool** — o modelo pode carregar uma skill (`use_skill`) e
   seguir suas instruções; o catálogo de skills habilitadas é anunciado
   no system prompt.
2. **Slash commands** — `/` no composer abre uma paleta listando skills
   habilitadas + comandos builtin (`/new`, `/agent`, `/tools`,
   `/help`); selecionar `/skill-name` injeta a skill no turno do usuário.

### Expected outcome

- `/composio-cli …` no composer injeta o SKILL.md da skill +
  argumentos do usuário como mensagem do turno, com chip visual
  "skill: composio-cli".
- O modelo pode chamar `use_skill("composio-cli")` mid-conversation.
- Skills desabilitadas (capability registry) não aparecem em nenhuma
  das duas superfícies.

### Out of scope

- Execução de scripts dentro da skill (o modelo usa `shell_exec`/`run_cli`
  com os paths da skill se necessário — a tool retorna paths, não roda
  nada).
- Edição/instalação de skills (já coberto pela tela Agent Skills).
- Autocomplete de comandos arbitrários do shell.

---

## 2. Agent Role

> .NET + Blazor engineer — tool nova, system prompt, paleta de composer
> com teclado/virtual keyboard.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Conteúdo de SKILL.md é instrução não-confiável: vai ao contexto como
  mensagem de tool/usuário, nunca ao system prompt bruto.
- Paths retornados por `use_skill` são somente leitura para o modelo;
  `write_file`/`shell_exec` continuam confinados pelo jail/gateway.
- Slash palette não intercepta atalhos de texto (`/` no meio de texto,
  dentro de code span, não dispara).

---

## 4. Product Context

### Technical context

- `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` —
  `DiscoverAsync` retorna `SkillDto` (name, description, path, source).
- `ChatToolContext` — precisa carregar o workspace; skills vivem fora
  dele, então `use_skill` lê o arquivo server-side e devolve o conteúdo
  (não o path aberto).
- Composer: `ProviderChat.razor` textarea + `AiChat.razor` composer —
  a paleta é componente compartilhado `SlashCommandPalette.razor`.
- aaPanel `skills.py`: estado disabled num JSON persistente — o nosso
  equivalente é `Taskboard:Chat:Capabilities:Disabled` (registry SPEC).

### Relevant files

- `src/Taskboard.Integrations/Chat/Tools/SkillTool.cs` (novo)
- `src/Taskboard.Blazor/Components/Chat/SlashCommandPalette.razor` (novo)
- `src/Taskboard.Blazor/Components/Chat/ProviderChat.razor`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Client/wwwroot/js/taskboard.js` (keydown do composer)

---

## 5. Functional Requirements

### FR-001: `use_skill` tool

```json
{ "name": "use_skill",
  "parameters": { "name": "string", "task_hint": "string?" } }
```

Execução:

1. `ISkillDiscoveryService` → match por `name` (case-insensitive) entre
   skills habilitadas.
2. Lê `SKILL.md` (máx 64KB), retorna JSON:
   `{ "name", "source", "directory", "instructions": "<body>",
      "resources": ["scripts/x.sh", …] }`.
3. `resources` lista arquivos dentro do diretório da skill (o modelo pode
   pedir `read_file` neles **se** o jail permitir — senão devolvemos só
   o conteúdo do SKILL.md).

Miss/habilitada-off → `Refused` com lista dos nomes válidos
(truncada a 50).

### FR-002: Anúncio no system prompt

`ChatService` acrescenta uma seção compacta ao system prompt quando
`Skills:Enabled`:

```
Available skills (invoke via use_skill or user /command):
- composio-cli — operate the Composio CLI…
- graphify — input to knowledge graph…
```

Máx. 40 skills, descrição truncada em 120 chars. Desabilitadas/sem
descrição não são listadas.

### FR-003: Slash palette (UX)

- Digitar `/` como **primeiro char** do composer abre a paleta
  (`role="listbox"`) filtrando por texto: skills habilitadas
  (`/skill-name`) primeiro, depois builtins.
- Builtins mínimos: `/new` (nova conversa), `/agent` (alterna para
  Agent mode ou inicia delegação), `/tools` (abre popover de
  capabilities), `/clear` (limpa composer), `/help` (lista comandos).
- Navegação: `↑`/`↓`, `Enter`/`Tab` seleciona, `Esc` fecha. No mobile
  funciona por toque; o teclado virtual não fecha a paleta.
- `/skill-name` selecionado → o composer vira `/skill-name ` + cursor;
  no envio, a mensagem é transformada em:

  ```
  [skill chip: composio-cli]
  <conteúdo SKILL.md injetado como bloco de contexto>
  <texto do usuário após o comando>
  ```

  O chip é metadata do turno (persistido com a mensagem como
  `SkillInvocation` — `skill:` prefixo no conteúdo salvo, sem schema
  novo).

### FR-004: Comandos builtin executam ação local

`/new`, `/agent`, `/tools`, `/clear`, `/help` não vão ao modelo —
executam a ação UI correspondente e consomem o turno.

---

## 6. Business Rules

- `Taskboard:Chat:Skills:Enabled=false` → `use_skill` ausente, paleta
  omite skills (builtins permanecem).
- Skill desabilitada no registry = inexistente para chat.
- SKILL.md >64KB → truncado com aviso `"(truncated)"`.
- Injeção: conteúdo da skill nunca promove privilégio — instruções
  pedindo tools desabilitadas falham normalmente no loop.

---

## 7. Expected Architecture

```
composer "/" ──► SlashCommandPalette ──► ISkillDiscoveryService
   │                                        │ enabled filter (registry)
   ▼                                        ▼
send "/x args" ──► turno com skill injetada ──► ChatService
                        ▲                        │ use_skill tool
                        └────────────────────────┘
```

---

## 8. Edge Cases

- `/` no meio do texto ou escapado `\//` → não abre paleta.
- Skill removida do disco entre catálogo e invocação → `use_skill`
  refused "skill not found" gracioso.
- Paleta aberta + blur → fecha sem seleção.
- Dois diretórios com mesmo `name` (ex.: claude+devin) → paleta mostra
  `name (source)`; `use_skill` resolve pelo mais prioritário
  (ordem de `_sources`) e reporta ambiguidade no resultado.

---

## 9. Non-Functional Requirements

- Paleta abre em <50ms; filtro em memória (catálogo já carregado).
- `use_skill` não executa nada — leitura de arquivo ≤64KB.

---

## 10. Expected Tests

- `Dado_SkillHabilitada_Quando_UseSkill_Entao_RetornaInstrucoes`
- `Dado_SkillDesabilitada_Quando_UseSkill_Entao_RefusedComLista`
- `Dado_SlashNoInicio_Quando_Digita_Entao_PaletaListaSkillsHabilitadas`
- `Dado_SlashNoMeio_Quando_Digita_Entao_NaoAbre`
- `Dado_ComandoBuiltin_Quando_Enviado_Entao_ExecutaAcaoLocalSemModelo`

---

## 11. Acceptance Criteria

1. `/` no composer vazio abre paleta com skills + builtins.
2. `/skill` enviada injeta SKILL.md no turno com chip visível.
3. `use_skill` retorna instruções e é filtrado pelos toggles.
4. Skills off não aparecem na paleta nem no system prompt.

---

## 12. Implementation Plan

1. RED: testes da tool e da transformação do turno.
2. `SkillTool` + wiring + system prompt section.
3. `SlashCommandPalette` + integração nos dois composers.
4. GREEN + build.

---

## 13. Rollback Strategy

- `Skills:Enabled=false` ou revert — chat volta ao estado atual.

---

## Pending Questions

1. Builtins além dos 5 listados (`/model`, `/compact`, `/save`) nesta
   iteração?
2. `/skill` injeta conteúdo completo ou só confirma e deixa o modelo
   chamar `use_skill`? (proposta: injeta — um round-trip a menos)

---

## Human Approval Checklist

- [ ] Dupla superfície (tool + slash) aprovada.
- [ ] Injeção no turno do usuário aprovada.
- [ ] Catálogo no system prompt aprovado.
