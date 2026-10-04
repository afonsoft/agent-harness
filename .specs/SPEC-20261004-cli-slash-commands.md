# SPEC-20261004 — Slash commands/skills por CLI no composer

## Contexto

A paleta de slash do composer (SPEC-20261001-chat-skills-slash-commands) já
lista builtins e skills globais (`~/.agents/skills`, `~/.claude/skills`, …),
mas ignora os comandos que **cada CLI** expõe: `/plan` e `~/.claude/commands`
do Claude Code, prompts do Codex, commands do OpenCode, e os comandos que o
agente anuncia via ACP `available_commands_update`. Este SPEC descobre esses
comandos e skills **por CLI** e os expõe na paleta com badge da origem.

## Requisitos

- **RF-001 Discovery por CLI** — `CliCommandDiscoveryService` varre os
  diretórios de comandos de cada `AgentCliKind` (markdown recursivo,
  nome = path relativo `/`-join sem extensão, ex. `ops/deploy`) e mescla as
  skills do próprio CLI (`*/SKILL.md` via `FrontmatterReader`,
  `kind="skill"`). Dedup case-insensitive com comando ganhando de skill.
  Layouts:

  | CLI | Commands | Skills |
  |---|---|---|
  | claude | `~/.claude/commands` | `~/.claude/skills` |
  | codex | `~/.codex/prompts` | `~/.codex/skills` |
  | opencode | `~/.config/opencode/commands`, `~/.opencode/commands` | `~/.config/opencode/skills`, `~/.opencode/skills` |
  | devin | `~/.devin/commands`, `~/.config/devin/commands` | `~/.devin/skills`, `~/.config/devin/skills` |
  | antigravity (agy) | `~/.gemini/antigravity-cli/commands` | `~/.gemini/antigravity-cli/skills` |
  | kimi | `~/.kimi-code/commands` | `~/.kimi-code/skills` |

  Frontmatter YAML parseado localmente (`description`, `argument-hint`).
  Limites: 200 entradas/dir e 256 KiB/arquivo.

- **RF-002 Endpoints** — `GET /api/cli-commands?cli=` (lista
  `{name, description, kind, source}`) e `GET /api/cli-commands/{cli}/{**name}`
  (detalhe `{…, body, argumentHint}`; `{**name}` porque comandos aninham por
  diretório). `cli` aceita nome de `AgentCliKind` ou de `AgentType`
  (mapeado via `AgentCliMap.CliKindFor`); desconhecido → lista vazia.
  `TaskboardClient.GetCliCommandsAsync`/`GetCliCommandDetailAsync`.

- **RF-003 Envio** — `ResolveSlashContentAsync` (AiChat e ProviderChat):
  builtin → cli → skill. Comando com body é injetado via
  `SlashCommandComposer.ComposeCliCommand`: chip `[command:name]`
  (`[skill:name]` quando `kind="skill"`) + bloco `<command name= source=>`
  com `$ARGUMENTS` substituído pelo resto do input (ou sufixo
  `Arguments: <rest>` quando o body não tem placeholder). Sem body — ou
  404 no detalhe (removido entre listar e enviar, ou anunciado via ACP) —
  vai **verbatim** para a sessão do agente, que resolve o comando nativo.

- **RF-004 Paleta** — `SlashItem` ganha `Origin` (badge `text-bg-info` com o
  nome da CLI; `skill` mantém badge secundário; builtin sem badge). Ordem:
  builtin → cli → skill, match exato primeiro (`.Take(12)`). AiChat recarrega
  os itens da CLI ao mudar `_cfgCli` (select, default inicial, hidratação de
  conversa, restore de thread); ProviderChat quando `AgentContext.AgentCli`
  muda. Eventos `commands` (ACP `available_commands_update`) mesclam na
  paleta com envio verbatim; reset ao trocar de thread.

## Decisões

- **Serviço separado de `ISkillDiscoveryService`** — os fontes registrados de
  skills não cobrem `~/.config/*/skills` nem `~/.codex/skills`; um scanner
  dedicado por CLI evita re-registrar fontes globais que vazariam na skill
  list global.
- **`argument-hint` parseado localmente** — `FrontmatterReader` só lê
  name/description/tools; o hint do Claude commands é lido por um parser
  mínimo no serviço.
- **Sem resposta free-text** — comandos ACP com `input.hint` recebem args do
  sufixo `/cmd args`; nenhum modal novo.

## Non-goals

- Edição/execução de comandos fora do composer; palette em views que não seja
  o composer (terminal já tem autocomplete do shell).
- Comandos de CLIs custom (`custom:<id>`) — mapeiam para a `AgentType` quando
  definida; ids custom puros retornam lista vazia.
