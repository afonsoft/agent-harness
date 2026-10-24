# SPEC-20261024-agent-session-scanner-hermeticity — Session scanner home override for hermetic tests

| Campo | Valor |
| --- | --- |
| Status | **Approved** |
| Owner | @afonsoft |
| Ticket | GAP-tests-agent-session-scanner-hermeticity |
| Área | `src/Taskboard.Integrations/Agents/AgentSessionScanner.cs`, `tests/Taskboard.Tests.Integration/` |

## Contexto

`AgentSessionScanner` varre `~/.claude/projects/**.jsonl` do **home real do processo**
(`Environment.SpecialFolder.UserProfile`, linha 34 + root em 90-94) sem nenhum override
de configuração. Em hosts de desenvolvimento com `~/.claude` populado, os testes de
integração `DashboardEndpointsTests.GetSessions*` (`/api/agents/sessions`) varrem
centenas de arquivos e estouram o timeout do HttpClient — observado localmente como
4 falhas de 4–12 min cada (host com ~194 MB / 684 jsonl). No CI (home vazio) a suíte
passa, mascarando o problema.

## Evidência (gap-analysis 2026-10-24)

- AS-IS: `AgentSessionScanner.cs:34` — `this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))`; `TestWebApplicationFactory` já injeta `Taskboard:HomeDir` fake para outros componentes, mas o scanner não o lê.
- TO-BE: convenção do repo — testes de integração herméticos, sem dependência do home do desenvolvedor (mesmo padrão já aplicado a `ChatMemoryStore`/`ChatTodoStore` via `environment.GetDataDir()`).
- Diferença: falha intermitente/environment-dependent na suíte de integração.

## Requisito

`AgentSessionScanner` deve aceitar a raiz do home via `IConfiguration`/`Taskboard:HomeDir`
(mesma chave já usada pelos testes) com fallback para `UserProfile` quando ausente.
Atualizar os testes de integração para apontar o home fake; cobrir com teste unitário
que o scanner honra o override.

## Critérios de Aceite

- [ ] `dotnet test tests/Taskboard.Tests.Integration --filter GetSessions` passa em host com `~/.claude` populado (>100 MB) sem tocar o home real.
- [ ] Sem `Taskboard:HomeDir` configurado, o scanner mantém o comportamento atual (`UserProfile`).
- [ ] Teste unitário: scanner com home fake lê apenas os jsonl do fake.

## Fora de escopo

Caching/paginação do scan (melhoria separada); outros consumers de `UserProfile` fora
do caminho testado.
