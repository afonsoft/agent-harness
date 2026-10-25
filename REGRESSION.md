# REGRESSION.md — flake/incident ledger

SPEC-20261024-p1-agent-reliability RF-H02: **append-only** log of production
and CI flakes. One row per incident, newest at the bottom — never edit or
remove a row (fix forward by appending).

Format: `date | symptom | root cause | fix commit | regression test`

| date | symptom | root cause | fix commit | regression test |
|---|---|---|---|---|
| 2026-10-24 | ChatJobs settle race — instant-exit jobs settled before the run row committed, leaving runs stuck `running` | job runner observed the settle event before the writer's UoW flushed the row | `91a73bb` | `ChatJobsScheduleSearchTests` settle-ordering cases |
| 2026-10-24 | ManagedJob trigger race — `RunRequested`/`ScheduleChanged` signals dropped under concurrent ticks | `Task.WhenAny` abandoned `signalTask` without cancelling; the next iteration's `ReadAsync` consumed the pending signal (SPEC-20261001 B-04) | `e4f91db` | `ManagedJobService` loop rewrite + job tick coverage |
