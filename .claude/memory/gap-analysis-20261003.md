# Gap Analysis — 20261003

- Repository: /home/ubuntu/repos/agent-harness | Branch: main | Commit: c8bb02f
- Phase reached: verdicts (gate pendente)
- Mode: analyze — foco do pedido: telas/mobile/a11y, implantação, processos, RAG/MCP, performance
- Método UI: `web-design-guidelines` modo 1 (estático; sem URL pública → Lighthouse não executável)
- Server de referência local: http://localhost:47823 (Release, Development env) + Chrome em ~330px (mobile)

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| `.specs/SPEC-*.md` | present | 227 files; últimos runs reconciliados (issues #421-424 fechadas) |
| `docs/` | present | bilíngue en/pt-br; `installation.md` cobre docker/compose |
| `.claude/memory/gap-analysis-*` | present | último run 2026-10-02, todas issues CLOSED |
| GitHub issues abertas | nenhuma | `gh issue list` — 0 abertas |
| CI | present | dotnet.yml (coverage ratchet 77%), codeql.yml, code-quality.yml (Sonar), dependabot |
| Docker | present | Dockerfile multi-stage (test gate verde), compose `harness`+`tests`+`validate` |
| gh auth | ok | devin-ai-integration[bot] |

## 2. AS-IS × TO-BE matrix

| Topic | AS-IS | TO-BE | Sources |
| --- | --- | --- | --- |
| Mobile shell | offcanvas-lg + hamburger funciona (verificado 330px) | responsivo | MainLayout.razor, live check |
| Topbar page title | "Board" em /finops, /cockpit, /jobs, /specs | título da página | MainLayout.razor:66-77 |
| lang / i18n | `<html lang="en">` + copy mista pt-BR/en + datas em formatos mistos | consistente | index.html:2, FinOps.razor:287 |
| A11y | NavMenu/AiChat/Settings bons; 12 páginas/diálogos com zero aria | guidelines | grep aria por arquivo |
| RAG | `Rag:Url/ApiKey/ServerName` + provisioning p/ CLIs + injeção ACP + status endpoint | — | Program.cs:3035+, McpProvisioningService.cs |
| MCP server próprio | 4 tools (issue history, GH comments, cloud status) | workbench p/ agentes | Taskboard.Mcp/Tools |
| Chat MCP client | implementado, `Enabled=false` default | — | Program.cs, Settings |
| Perf runtime | /health 11ms, index <1ms; static assets fingerprintados | — | curl |
| WASM bundle | _framework 47MB disco (~1.2MB br p/ runtime wasm; ICU segmentado ok) | budget? | publish output |
| Backup | .bak só p/ configs MCP provisionadas | dados em /data | grep backup |

## 3. Candidates and verdicts

| Key | Category | Verdict | Priority | Evidence |
| --- | --- | --- | --- | --- |
| GAP-impl-topbar-page-titles | implementation | CONFIRMADO | alta | MainLayout.razor:66-77 — `PageTitles` sem `finops`/`cockpit`/`jobs`/`specs` → topbar mostra "Board" (verificado ao vivo em /finops). Tab `<title>` atualiza ok (PageTitle component) — só o header falha. Fix: 4 linhas no array. |
| GAP-a11y-html-lang | implementation | CONFIRMADO | alta | index.html:2 `lang="en"`; copy majoritariamente pt-BR (AiChat titles, Settings) + mista ("Log out" en). Screen readers leem pt com fonologia en. Decisão de produto: `pt-BR` ou localizar tudo p/ en — precisa escolher. |
| GAP-a11y-skip-link | implementation | CONFIRMADO | média | sem skip-link nem `.visually-hidden` custom (grep site.css/Layout); offcanvas é o primeiro foco no mobile. |
| GAP-a11y-focus-ring-chat | implementation | CONFIRMADO | média | site.css:2165 `.ai-chat-select:focus{outline:none;box-shadow:none}` — zera o indicador de foco sem substituto (regra: nunca outline:none sem focus-visible). `.skill-card:focus` (626) tem box-shadow → ok. |
| GAP-a11y-keyboard-card | implementation | CONFIRMADO | média | Workflow.razor:90 `div[role=button] @onclick` sem `@onkeydown` nem tabindex — card de workflow inacessível por teclado. Padrão correto existe em Skills.razor:55. |
| GAP-a11y-aria-coverage | implementation | CONFIRMADO | média | 0 aria em: Agents, FinOps, Login, Prompts, Skills, Specs, CockpitRun, AgentInstallDialog, AgentModelConfigDialog, RunAgentDialog(1), VscodeEditor(1). Ex.: FinOps `24h/7 days/30 days/All` sem `aria-pressed`/group; dialogs sem `aria-labelledby`/`role=dialog`. |
| GAP-i18n-date-formats | implementation | CONFIRMADO | média | Formatos hardcoded mistos: `ToString("g")` (Settings:102, FinOps:245-250 — vira "9/3/2026 11:23 AM" pois WASM roda en-US), `dd/MM HH:mm` (Cockpit:76), `MM-dd HH:mm` (FinOps:287), `yyyy-MM-dd` (Agents:640). Sem `CultureInfo` explícita. |
| GAP-typography-ellipsis | documentation/copy | CONFIRMADO | baixa | `Loading ...` com 3 pontos em 8+ páginas (AiChat:75, Settings:18, Agents:42, Prompts:20, Cockpit:43, Workflow) — guideline exige `…`. Idem placeholders ("Run ID for telemetry..."). |
| GAP-theme-meta | implementation | CONFIRMADO | baixa | index.html sem `<meta name="theme-color">` e sem `color-scheme` → chrome mobile e form controls não tematizam. |
| GAP-impl-mobile-board-overflow | implementation | CONFIRMADO | baixa | Board em ~330px: legenda "Priority Level" vaza horizontal (live check); resto do shell ok. |
| GAP-ops-sqlite-backup | operation | CONFIRMADO | média | sem backup do volume `/data` (harness.sqlite + credenciais CLI): nenhum `VACUUM INTO`/backup em código ou docs (grep). Sugestão: `taskctl backup` + seção em installation.md + exemplo cron no compose. |
| GAP-impl-mcp-tool-surface | requirements | CONFIRMADO | média | Taskboard.Mcp expõe só 4 tools (get_issue_history, list/add_github_issue_comment, cloud_status); nenhuma tool de board/specs/jobs do próprio harness. Sugestão: list_issues, get_issue, move_issue, get_spec, jobs_status — paridade com `taskctl`/`manage-taskboard` skill. |
| GAP-impl-duplicate-apiclient | architecture | CONFIRMADO | baixa | `TaskboardApiClient` duplicado: Taskboard.Cli/Services/TaskboardApiClient.cs:18 e Taskboard.Mcp/Services/TaskboardApiClient.cs:29 — shrink: extrair p/ projeto compartilhado. |
| GAP-perf-getlatest-perissue | performance | CONFIRMADO | baixa | EfCoreAgentRunRepository.cs:64-73 — GroupBy+OrderBy+First + `Contains` (2 queries; tradução do First-em-grupo é frágil no SQLite). Alternativa: `GroupBy(IssueId).Select(Max(StartedAt))` + join, sempre traduzível. |
| GAP-perf-wasm-bundle | performance | CONFIRMADO | média | publish wwwroot 33MB, _framework 47MB c/ variantes; sem `RunAOTCompilation`/trimming props nos csproj. Já há br+gzip, ICU segmentado, sem satellites extras. Levers: avaliar AOT p/ páginas quentes (Terminal/xterm, chat) ou documentar budget. |
| GAP-ops-docker-pin | operation | CONFIRMADO | baixa | `FROM ...:10.0` tag mutável (Dockerfile); dependabot cobre nuget+actions, não docker. Sugestão: pin digest + ecosystem docker. |
| GAP-impl-chat-mcp-default-off | requirements | INCONCLUSIVO | — | `Taskboard:Chat:Mcp:Enabled=false` default + `Servers=[]` — feature (SPEC-20261001-chat-mcp-client) dark. Deliberado ou esquecido? Perguntar. |
| GAP-impl-rag-test-connection | implementation | CONFIRMADO | baixa | Settings edita Rag:Url/ApiKey sem "testar conexão" (existe `jira.TestConnectionAsync` p/ Jira, nada p/ RAG); mcp/status cobre provisioning dos CLIs, não valida o endpoint RAG. |
| GAP-health-endpoints | operation | REJEITADO | — | `/health` + Dockerfile HEALTHCHECK + compose healthcheck — coberto. |
| GAP-ci-coverage | tests | REJEITADO | — | ratchet COVERAGE_THRESHOLD 77%→80% em dotnet.yml — coberto. |
| GAP-sonar-artifacts | hygiene | REJEITADO | — | `.sonar_devin_auto_fix/` não tracked mais (`git ls-files` vazio); #410 fechada hoje. |
| GAP-static-cache | performance | REJEITADO | — | `no-cache` em /_framework só em Development; MapStaticAssets fingerprinta+immutable em prod; /framework-assets seta immutable manual (FrameworkAssetsEndpoints.cs:47). |
| GAP-virtualize-lists | performance | REJEITADO | — | KanbanBoard usa `<Virtualize>` (66); Specs/Jobs/Skills têm foreach mas listas pequenas/locais — aceitável. |
| GAP-mcp-config-backup | operation | REJEITADO | — | .bak + writes atômicos + 0600 já no McpProvisioningService. |

## 4. Prioridades (CONFIRMADO)

- **P1**: topbar-page-titles (bug visível em 4 rotas), html-lang (decisão de produto)
- **P2**: sqlite-backup, mcp-tool-surface, a11y-aria-coverage, i18n-date-formats, wasm-bundle
- **P3**: skip-link, focus-ring-chat, keyboard-card, mobile-board-overflow, theme-meta, ellipsis, duplicate-apiclient, getlatest-perissue, docker-pin, rag-test-connection
- **INCONCLUSIVO**: chat-mcp-default-off (produto)

## 5. Gate → SPECs + Issues (2026-10-03)

Gate APROVADO pelo usuário ("Aprovado" — todos os gaps confirmados).

Draft SPECs escritos (7 grupos de findings):
- `.specs/SPEC-20261003-a11y-baseline.md`
- `.specs/SPEC-20261003-i18n-consistency.md`
- `.specs/SPEC-20261003-mobile-polish.md`
- `.specs/SPEC-20261003-sqlite-backup.md`
- `.specs/SPEC-20261003-mcp-tool-surface.md`
- `.specs/SPEC-20261003-perf-pass.md`
- `.specs/SPEC-20261003-ops-hardening.md`

Issues criadas (status `backlog` — SPECs ainda Draft):
- Epic: **#433** gap-analysis-20261003
- Slices: **#434** a11y-baseline · **#435** i18n-consistency · **#436** mobile-polish · **#437** sqlite-backup · **#438** mcp-tool-surface · **#439** perf-pass · **#440** ops-hardening

## 6. Pendências

- SPECs Draft aguardando aprovação do usuário → flip `Status: Approved` + labels `backlog`→`todo` nas issues.
- Decisão de idioma do produto (pt-BR vs en) desbloqueia html-lang + date-formats (SPEC i18n assume pt-BR).
- `Taskboard:Chat:Mcp:Enabled=false` default — inconclusivo; sem spec até decisão.
- `.github/dependabot.yml` (docker ecosystem) — pendente de aprovação explícita (workflows protegidos).
- Lighthouse inaplicável (sem URL pública) — re-rodar quando houver deploy.
- Mobile audit cobriu ~330px CSS width via janela encolhida; device toolbar real (touch emulation) não exercitada.
- E2E do AI Code segue sem aprovação de gravação.
