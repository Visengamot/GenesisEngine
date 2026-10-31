# Natural population: native full-frame benchmark

This benchmark measures a **naturally evolved** seeded world, not an injected 10,000-agent stress scenario. Launch the Release application from the repository root:

```powershell
dotnet build src/Genesis.Engine.App/Genesis.Engine.App.csproj -c Release
dotnet run --no-build -c Release --project src/Genesis.Engine.App/Genesis.Engine.App.csproj -- --benchmark-natural
```

The opt-in flag does not change default gameplay or the historical `--benchmark-render` mode. It writes [`natural-1000-render.json`](<natural-1000-render.json>) rather than overwriting historical render results. Run this in an interactive desktop session; it opens and closes its own Raylib window. If closed early, the report is marked incomplete and the process returns exit code 2.

## Method

- Initialize a standard 160 × 90 seed-42 world with its original 80 agents. No agents are injected, spawned by the benchmark, or added beyond the simulation's ordinary reproductive population ceiling of 1,000.
- Evolve the world for **1,800 steps = 60 simulated seconds** at `dt=1/30` before creating the native window; then render 30 warmup frames and collect 120 measured frames. Every native frame advances a complete `Simulation.Step(1/30)` and draws the normal world and full HUD through the real `Raylib.EndDrawing()`.
- Request uncapped target FPS (`SetTargetFPS(0)`), without requesting VSync. Stopwatch wall-clock samples measure complete frame, simulation step, draw including present, and EndDrawing; allocations use `GC.GetAllocatedBytesForCurrentThread` on the render/simulation thread. The p95 is nearest-rank `ceil(0.95 × 120)` after sorting. Birth counts are derived from the simulation's persisted monotonic `NextId`, so deaths do not erase births. Population and generation snapshots show the actual load.
- Full-frame measurements can include driver/compositor presentation waits and are **not GPU timestamps**. `1000 / mean frame milliseconds` estimates throughput, not a guarantee of stable displayed FPS or a controlled 60-FPS guarantee. A single run is sensitive to machine load, JIT, GC, and GPU driver conditions.

## Measured result

Release native run completed on Windows 10 (`.NET 8.0.24`, AMD Radeon Vega 8, 1480 × 940 window). Exact run metadata and samples summary: [`natural-1000-render.json`](<natural-1000-render.json>).

| Quantity | Measured result |
|---|---:|
| Population at creation → after 60-second prewarm → measured start/end | 80 → 1,000 → 1,000 / 1,000 |
| Prewarm births / native warmup births / measured births | 952 / 2 / 7 |
| Generation at measured start/end | 6 / 6 |
| Step mean / p95 / minimum / maximum | 4.754 / 7.431 / 2.371 / 11.167 ms |
| Full-frame mean / p95 / minimum / maximum | **17.323 / 68.474 / 4.577 / 82.849 ms** |
| Full-frame mean throughput (`1000 / mean ms`) | **57.73 FPS** |
| Draw including EndDrawing mean / p95 | 12.565 / 62.308 ms |
| EndDrawing alone mean / p95 | 0.254 / 0.338 ms |
| Managed allocation mean per measured frame | 98,691 B |
| GC collections during measurement (generations 0 / 1 / 2) | 7 / 1 / 0 |

**Interpretation:** Mean full-frame time of 17.32 ms exceeds the 16.67-ms 60-FPS budget; p95 of 68.47 ms indicates significant intermittent stalls. This naturally populated run does **not** demonstrate sustained 60 FPS. The earlier synthetic native benchmark injects 600, 2,000, or 10,000 agents; those results measure different workloads and should not be presented as a naturally evolved 10,000-agent world. This is a single run, not a paired before/after performance comparison.
