# SPEC-20261101-taskctl-skill-cli-sync — Skill/SPEC docs vs. real taskctl surface

| Campo | Valor |
| --- | --- |
| Status | **In Progress** |
| Owner | @afonsoft |
| Ticket | #685 |
| Área | Docs, Agent Skills, SPECs |

## Contexto

Após SPEC-20260918-projects-removal, a superfície real do `taskctl` (11 comandos)
e do `Taskboard.Mcp` (12 tools) divergiu da documentação consumida por agentes:

- `.claude/skills/manage-taskboard/SKILL.md` e `references/cli.md` documentam
  `cloud:login --url HTTPS_ORIGIN --actor-name NAME` com prompt `Shared key:` —
  o comando real é `cloud:login <url>` (argumento posicional; `--url` é a opção
  global de base da API; não há `--actor-name` nem prompt de senha).
- A skill declara que o MCP expõe "4 tools" — o servidor expõe **12**
  (`get_issue_history`, `list_github_issue_comments`, `add_github_issue_comment`,
  `cloud_status`, `list_issues`, `get_issue`, `move_issue`, `list_specs`,
  `get_spec`, `list_jobs`, `list_runs`, `get_run_status`).
- Os comandos `backup`, `restore`, `doctor` e `run` existem no CLI mas não
  aparecem na skill.
- `.specs/SPEC-003-cli.md` ainda descreve `project:*`, `issue:*`, `comment:*`,
  `attachment:*` — removidos por SPEC-20260918-projects-removal.

## Evidência

- AS-IS: `taskctl --help` lista `context:current`, `ghissue:*` (3), `cloud:*`
  (3), `backup`, `restore`, `doctor`, `run`; `grep -c McpServerTool` em
  `src/Taskboard.Mcp/Tools/TaskboardTools.cs` = 12 tools.
- TO-BE: skill + references descrevem exatamente essa superfície; SPEC-003
  marcado como superseded.

## Requisito

1. Sincronizar `.claude/skills/manage-taskboard/SKILL.md` com a superfície real
   (assinatura correta de `cloud:login`, tabela de 12 MCP tools, seção com
   `backup`/`restore`/`doctor`/`run`).
2. Sincronizar `references/cli.md` da skill com os mesmos comandos/opções.
3. Anotar `.specs/SPEC-003-cli.md` como superseded por
   SPEC-20260918-projects-removal (banner no topo; conteúdo histórico preservado).
4. Após merge, reinstalar a skill nos diretórios de agentes via `install.sh`.

## Critérios de Aceite

- [ ] `SKILL.md` lista os 11 comandos reais e as 12 MCP tools.
- [ ] `references/cli.md` reflete assinaturas e opções reais.
- [ ] SPEC-003-cli.md contém banner `Superseded` apontando para SPEC-20260918.
- [ ] Cópia instalada em `~/.claude/skills/manage-taskboard` idêntica à do repo.

## Fora de escopo

- Mudanças de comportamento no CLI/MCP (docs-only).
- Reescrita histórica de SPECs antigas além do banner de superseded.
