# SPEC-20261010-gantt-bar-clamp: Gantt bar geometry — crash on window edge

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Gantt bar width clamp — `Argument_MinMaxValue` fix |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Application.Contracts + Blazor WASM |
| Change type | Bugfix |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `devin/1791202654-gantt-bar-clamp-fix` |
| Depends on | — |

## 1. Executive Summary

### Problem

`GET /gantt` crashes the whole page render:

```
System.ArgumentException: Argument_MinMaxValue, 1, 0
   at Taskboard.Blazor.Components.Pages.Gantt.GetBarWidth(IssueTimelineDto issue)
```

`GetBarWidth` clamps `Math.Clamp(days / total * 100, 1, 100 - GetBarLeft(issue))`.
`GetBarLeft` saturates at 100 when `issue.CreatedAt.Date >= _rangeEnd` (an issue
created on the last day of the 90-day window — or later). At `left = 100` the
clamp becomes `Clamp(width, 1, 0)` → `Argument_MinMaxValue`, and Blazor's
ErrorBoundary kills the render.

### Solution

Move the bar geometry out of the `.razor` file into a pure static helper
`Taskboard.GitHub.GanttBarGeometry` (Application.Contracts — shared with the
WASM client and unit-testable, since the test project does not reference
`Taskboard.Blazor`):

- `WidthPercent` floors the clamp max at 1 → bounds can never invert.
- `BarPercent` returns the `(left, width)` pair and pulls `left` back to
  `100 - width` when the floored minimum would overflow the track — so a bar
  created on the last window day renders a 1% sliver at the right edge
  instead of crashing.
- Defensive `total <= 0` path (degenerate window) → full-width bar.

`Gantt.razor`'s `GetBarStyle` delegates to `BarPercent`; the private
`GetBarLeft`/`GetBarWidth` methods are removed.

### Scope

In scope: `GanttBarGeometry` (new), `Gantt.razor` (GetBarStyle), unit tests.
Out of scope: Gantt window configurability, lazy timeline loading, any other
page.

## 2. Requisitos

### RF-001 — `GanttBarGeometry` static helper

`src/Taskboard.Application.Contracts/GitHub/GanttBarGeometry.cs`:

| Member | Contract |
|---|---|
| `LeftPercent(rangeStart, rangeEnd, createdUtcDate)` | `(clamp(created, ≥ rangeStart) − rangeStart) / total * 100`, clamped `[0,100]`; `0` when `total ≤ 0` |
| `WidthPercent(rangeStart, rangeEnd, createdUtcDate, closedUtcDate, leftPercent)` | open issue → `end = rangeEnd`; `end` clamped `≤ rangeEnd`; `days = max(end − start, 1)`; width clamped `[1, max(100 − left, 1)]` |
| `BarPercent(rangeStart, rangeEnd, createdUtcDate, closedUtcDate)` | `(left, width)` with `left + width ≤ 100` guaranteed — pulls `left` back to `max(0, 100 − width)` on overflow |

### RF-002 — `Gantt.razor` delegates

`GetBarStyle` calls `BarPercent(rangeStart, rangeEnd, created.Date, closed?.Date)`
and formats `left:`/`width:` with the existing `Percent` formatter.
`GetBarLeft`/`GetBarWidth` are deleted — no other caller exists.

## 3. Arquitetura

```
Gantt.razor.GetBarStyle
     └─▶ GanttBarGeometry.BarPercent (Application.Contracts/GitHub — pure)
            ├─ LeftPercent  [0,100]
            └─ WidthPercent [1, max(100−left, 1)] → nunca min>max
```

## 4. Config

Nenhuma chave nova.

## 5. Segurança

Sem superfície nova — matemática pura sobre DTOs já autorizados.

## 6. Testes

Unit (`tests/Taskboard.Tests.Unit/GitHub/GanttBarGeometryTests.cs`, pt-BR
`Dado_Quando_Entao`):

- `Dado_IssueCriadaNoUltimoDiaDaJanela_Quando_BarPercent_Entao_NaoLancaEFicaDentroDaTrilha` — regressão do crash reportado.
- `Dado_IssueCriadaDepoisDoRangeEnd_Quando_BarPercent_Entao_BarraPermaneceNaTrilha`.
- `Dado_IssueAbertaAntiga_Quando_BarPercent_Entao_LarguraRespeitaOBordoDireito` (0→100).
- `Dado_IssueFechadaNoMeioDaJanela_Quando_BarPercent_Entao_GeometriaProporcional` (50/50).
- `Dado_IssueComFimAlemDoRangeEnd_Quando_BarPercent_Entao_FimClampadoNoBordo`.
- `Dado_JanelaDegenerada_Quando_BarPercent_Entao_GeometriaDefensiva` (total=0).
- `Dado_IssueFechadaAntesDeCriada_Quando_BarPercent_Entao_LarguraMinimaUmPorCento`.

## 7. Fases

- **P1**: RF-001 + RF-002 + testes — PR único (`fix(gantt): clamp bar width with max≥1`).

## Acceptance criteria

- Issue criada hoje (último dia da janela de 90d) → a página `/gantt` renderiza sem exceção; a barra aparece como sliver de 1% na borda direita.
- Nenhum `Math.Clamp` na página recebe `min > max` — invariante garantido pela helper.
- `dotnet test tests/Taskboard.Tests.Unit --filter GanttBarGeometry` → 7/7 verde.

## Open questions

1. Barra de 1% na borda (proposto) vs. esconder issues fora da janela — a
   escolha atual mantém visível o que existe; revisitar se poluir.
