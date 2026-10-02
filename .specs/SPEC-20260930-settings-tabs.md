# SPEC-20260930-settings-tabs: Settings reorganizada em abas

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Settings Tabs Reorganization |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20260930-settings-tabs` |
| Technical owner | afonsoft |
| Status | Done — entregue via [PR #411](https://github.com/afonsoft/agent-harness/pull/411) (merged 2026-09-30) |
| Date | 2026-09-30 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

`Settings.razor` é uma página única de ~1.500 linhas com 8 seções empilhadas
verticalmente (`Integrations`, `Agent Skills`, `RAG / Knowledge MCP`,
`Security`, `Agents`, `Chat`, `Features`, `Configuration`). Em telas pequenas
o usuário precisa rolar extensivamente para localizar uma configuração, sem
nenhum índice ou âncora de navegação. Em desktop a página também sofre de
fadiga de scroll.

### Objective

Reorganizar `Settings.razor` em abas (Bootstrap 5.3 nav-tabs) agrupando as
seções por domínio, com navegação por URL (`?tab=`) para deep-link e
restauração de contexto após refresh — priorizando usabilidade mobile
(tab strip com scroll horizontal, alvos de toque ≥44px).

### Expected outcome

- `/settings` exibe um tab strip rolável horizontalmente em telas estreitas.
- Cada aba carrega apenas seu grupo de seções; estado de formulário das
  outras abas permanece intacto enquanto a página não é recarregada.
- `?tab=<id>` seleciona a aba inicial; trocar de aba atualiza a URL via
  `NavigationManager` sem recarregar o circuito.
- Refresh e compartilhamento de link abrem diretamente na aba correta.
- Nenhum comportamento de salvamento, validação ou API é alterado.

### Out of scope

- Novas configurações ou endpoints.
- Persistência de "última aba" em `localStorage` (URL é a fonte de verdade).
- Reorganização visual interna dos formulários de cada seção (coberto por
  SPEC-20260930-mobile-responsive-ui).
- Wizards ou fluxos guiados.

---

## 2. Agent Role

> Frontend/Blazor engineer com foco em UX responsiva, Bootstrap 5.3 e
> acessibilidade (WAI-ARIA tabs pattern).

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não alterar contratos HTTP nem endpoints de settings.
- Não expor senhas, tokens ou connection strings no client.
- Não modificar `.github/workflows` sem aprovação humana.
- Não introduzir nova dependência de componentes (usar Bootstrap 5.3 +
  Blazor.Bootstrap já presentes).

---

## 4. Product Context

### Functional context

A página `/settings` é autenticada e concentra toda a configuração do
Harness: integrações (GitHub token, repo de skills), RAG MCP, troca de
senha, habilitação de agentes, providers de chat, feature flags e edição
de chaves de configuração.

### Technical context

- `src/Taskboard.Blazor/Components/Pages/Settings.razor` — ~1.512 linhas,
  8 seções `<h2 class="form-section-title">`, handlers `SaveAsync`,
  `SaveRagAsync`, `SaveSkillsRepoAsync`, `SaveConfigAsync`,
  `SaveCapabilityAsync`, `SaveChatSearchAsync`, `AddChatProviderAsync`,
  `ToggleAgent`, `ToggleFeatureAsync`, `ChangePasswordAsync`.
- Bootstrap 5.3 + Blazor.Bootstrap (MudBlazor foi substituído —
  SPEC-20260910-ui-login-settings-skills).
- Sidebar já usa `offcanvas-lg` (colapsa abaixo de 992px).
- `TaskboardClient` (typed client) já entrega as chamadas existentes.

### Relevant files

- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Blazor/Layout/MainLayout.razor`
- `src/Taskboard.Client/wwwroot/css/site.css`
- `tests/Taskboard.Tests.Unit/Blazor/SettingsFeaturesTests.cs`

---

## 5. Task Definition

### Main task

Dividir `Settings.razor` em abas navegáveis por URL, mantendo todo o
comportamento funcional existente.

### Subtasks

1. Extrair cada grupo de seções para markup de tab panes dentro do mesmo
   `.razor` (ou componentes filhos na mesma pasta se o arquivo exceder
   legibilidade aceitável).
2. Implementar tab strip Bootstrap (`nav nav-tabs` com `flex-nowrap
   overflow-x-auto` no mobile).
3. Ler/escrever o parâmetro `?tab=` via `NavigationManager`.
4. Aplicar WAI-ARIA tabs pattern (`role="tablist"`, `role="tab"`,
   `role="tabpanel"`, `aria-selected`, navegação por setas).
5. Adicionar testes source-level (convenção do repo para `.razor`).

### Do not do

- Não mudar nenhum handler, validação ou payload de salvamento.
- Não lazy-load dados por aba (página continua carregando tudo no
  `OnInitializedAsync` — dados já são um só pipeline).
- Não criar rotas separadas (`/settings/agents` etc.); apenas `?tab=`.

---

## 6. Functional Requirements

### FR-001: Agrupamento em abas

As 8 seções são agrupadas em 5 abas (ordem da esquerda para a direita):

| Tab id | Rótulo | Seções contidas |
|---|---|---|
| `general` | General | Features, Configuration |
| `integrations` | Integrations | Integrations (GitHub token, skills source repo), RAG / Knowledge MCP |
| `agents` | Agents | Agents (toggles), Agent Skills |
| `chat` | Chat | Chat (providers, capability defaults, web search), Chat Capabilities (tools/MCP/skills/delegation toggles — SPEC-20261001-chat-capability-registry) |
| `security` | Security | Change password |

- Rótulos em inglês (idioma atual da UI).
- Se o agrupamento proposto não for aprovado, ajustar conforme
  `Pending Questions` antes da implementação.

### FR-002: Deep-link via query string

- `OnInitialized`/`OnParametersSet` lê `?tab=`; valor inválido cai na
  primeira aba (`general`).
- Clique numa aba chama `NavigationManager.NavigateTo("/settings?tab=X",
  replace: false)` sem forçar reload — atualizar via state + URL.
- Botão voltar/avançar do browser navega entre abas visitadas.

### FR-003: Tab strip responsivo

- Desktop (`≥992px`): abas alinhadas à esquerda, largura natural.
- Mobile (`<576px`): `flex-nowrap overflow-x-auto` com scroll suave
  (`scroll-behavior: smooth`), scrollbar discreta, aba ativa sempre
  visível (`scrollIntoView({ inline: "nearest" })` ao ativar).
- Altura mínima do alvo de toque: 44px; `min-height` aplicado ao `.nav-link`
  somente em `(pointer: coarse)`.

### FR-004: Acessibilidade do tab pattern

- `role="tablist"` no `<ul>`, `role="tab"` + `aria-selected` +
  `aria-controls` em cada aba, `role="tabpanel"` + `aria-labelledby`
  em cada painel.
- Navegação por teclado: `Left`/`Right`/`Home`/`End` movem a aba ativa;
  `Tab` move o foco para dentro do painel (roving tabindex:
  `tabindex="0"` na aba ativa, `-1` nas demais).
- Painéis inativos usam `hidden`/`d-none` — não `display: contents`.

### FR-005: Preservação funcional

- Todo o código `@code` permanece funcionalmente idêntico: handlers,
  `_dirty`, validações, `ChangePasswordAsync`, toggles e saves por seção.
- O botão global "Save changes" (seção Configuration) continua salvando
  apenas os campos sob sua responsabilidade atual.

---

## 7. Business Rules

- Página permanece autenticada (`[Authorize]`).
- Secrets (tokens, API keys, senhas) continuam mascarados; a aba
  `security` não exibe valores existentes.
- A aba inicial padrão (sem `?tab=`) é `general`.

---

## 8. Domain Modeling

Nenhuma alteração de domínio. Somente estado local da página:

```csharp
private string _activeTab = "general";
private static readonly string[] TabIds = ["general", "integrations", "agents", "chat", "security"];
```

---

## 9. Expected Architecture

```
/settings?tab=integrations
        │
        ▼
Settings.razor
  ├─ <ul class="nav nav-tabs …" role="tablist">
  │     └─ 5 × <button role="tab" aria-selected>
  ├─ <div class="tab-content">
  │     ├─ <section role="tabpanel" id="tab-general">…</section>
  │     ├─ <section role="tabpanel" id="tab-integrations">…</section>
  │     └─ …
  └─ @code { ActivateTab(id) → _activeTab + Navigation.NavigateTo("?tab=") }
```

Sem novos serviços, endpoints ou entidades.

---

## 10. API Contracts

Nenhuma alteração. Todos os endpoints consumidos por `TaskboardClient`
permanecem iguais.

---

## 11. Application Contracts

Nenhuma alteração.

---

## 12. Persistence and Data

Nenhuma alteração. A aba ativa é derivada exclusivamente da URL.

---

## 13. Integrations

Nenhuma alteração.

---

## 14. Edge Cases and Error Scenarios

- `?tab=` ausente/vazio/desconhecido → `general`.
- `?tab=` com valor duplicado (`?tab=a&tab=b`) → primeiro valor válido.
- Refresh numa aba → retorna à mesma aba.
- Resize desktop→mobile com aba ativa fora da viewport do strip →
  `scrollIntoView` reposiciona.
- Erro de salvamento numa aba inativa: mensagens de erro são por seção —
  permanecem visíveis ao voltar à aba (estado preservado).

---

## 15. Few-Shot Examples

Markup-alvo (simplificado):

```razor
<ul class="nav nav-tabs settings-tabs flex-nowrap overflow-x-auto" role="tablist">
    @foreach (var tab in TabIds)
    {
        <li class="nav-item" role="presentation">
            <button type="button" role="tab"
                    class="nav-link @(_activeTab == tab ? "active" : null)"
                    aria-selected="@(_activeTab == tab)"
                    aria-controls="tab-@tab"
                    tabindex="@(_activeTab == tab ? 0 : -1)"
                    @onclick="() => ActivateTab(tab)">
                @TabLabel(tab)
            </button>
        </li>
    }
</ul>
<div class="tab-content pt-3">
    <section id="tab-general" role="tabpanel" aria-labelledby="tab-general"
             class="@(_activeTab == "general" ? null : "d-none")">
        … Features + Configuration …
    </section>
    …
</div>
```

---

## 16. Non-Functional Requirements

### Performance

- Sem re-render adicional: troca de aba é um `StateHasChanged` local.

### Accessibility

- Conformidade WAI-ARIA Authoring Practices — Tabs pattern.
- Focus outline visível (`:focus-visible`) em todos os tabs.
- Contraste ≥4.5:1 nos rótulos ativos/inativos.

### Responsiveness

- Strip utilizável a 360px de largura sem overflow da página.
- Transição para desktop sem saltos de layout.

---

## 17. Mandatory Guardrails

- Build com `TreatWarningsAsErrors` limpo.
- Nenhum `// nosonar`, `NOSONAR` ou exclusão de cobertura.
- Mudança limitada a `Settings.razor`, `site.css` e testes.

---

## 18. Expected Tests

### Unit tests (source-level, convenção do repo para `.razor`)

Em `tests/Taskboard.Tests.Unit/Blazor/SettingsFeaturesTests.cs` (ou novo
`SettingsTabsTests.cs`):

- `Dado_SettingsPage_Quando_Render_Entao_ExisteTabListComCincoAbas` —
  asserta `role="tablist"` e os 5 `role="tab"` com `aria-controls`.
- `Dado_SettingsPage_Quando_TabInativa_Entao_PainelOculto` — cada
  `role="tabpanel"` alterna `d-none`/`hidden` conforme `_activeTab`.
- `Dado_QueryString_Quando_TabValida_Entao_AbaInicialCorreta` — leitura de
  `?tab=` refletida no estado inicial.
- `Dado_QueryString_Quando_TabInvalida_Entao_FallbackGeneral`.

### Integration tests

- Nenhum novo — sem mudança de backend.

---

## 19. Acceptance Criteria

1. `/settings` renderiza 5 abas conforme tabela FR-001.
2. `/settings?tab=chat` abre diretamente na aba Chat.
3. Clique em aba atualiza URL para `?tab=<id>` sem reload.
4. Largura 360px: strip rolável, sem overflow horizontal da página.
5. Navegação por setas Left/Right/Home/End funciona com foco no tablist.
6. Todos os 1309+ testes unitários continuam verdes + novos testes passam.
7. `dotnet build` sem warnings.

---

## 20. Implementation Plan

1. RED: testes source-level do tab pattern falhando.
2. Envolver as 8 seções em painéis de abas; adicionar o strip.
3. Implementar `ActivateTab` + leitura de `?tab=`.
4. CSS mínimo em `site.css` (scroll strip + alvo de toque).
5. GREEN: testes passam; `dotnet build` limpo.

---

## 21. Rollback Strategy

- Reverter o commit: a página volta ao layout em coluna única sem perda
  funcional (nenhum handler muda).

---

## 22. Risks and Mitigations

| Risco | Mitigação |
|---|---|
| Quebra de estado ao alternar abas | Painéis ficam no DOM (`d-none`), inputs não desmontam — estado preservado. |
| `?tab=` conflitando com outros query params | Ler apenas a chave `tab`; demais params ignorados. |
| Blazor.Bootstrap `Tabs` component vs markup manual | Preferir markup Bootstrap puro: controle total de `role`/URL sem dependência do ciclo de vida do componente. |

---

## 23. Definition of Done

- [ ] 5 abas navegáveis por URL.
- [ ] Testes source-level verdes.
- [ ] `dotnet build`/`dotnet test` limpos.
- [ ] Revisão mobile a 360px sem overflow.
- [ ] ARIA tabs pattern validado.

---

## 24. Key Reminder

> The SPEC is the contract. Do not expand scope beyond reorganização de
> Settings em abas com deep-link.

---

## Pending Questions

1. O agrupamento de FR-001 (5 abas) está aprovado ou prefere mapeamento
   1:1 (8 abas, uma por seção atual)?
2. Rótulos das abas em inglês (padrão da UI) ou bilíngue?
3. Aceitável manter carregamento único (todas as seções no DOM) ou deseja
   lazy-load por aba no futuro?

---

## Human Approval Checklist

- [ ] Agrupamento de abas aprovado.
- [ ] Deep-link `?tab=` é o mecanismo desejado.
- [ ] Escopo (sem lazy-load, sem novos endpoints) aceito.
