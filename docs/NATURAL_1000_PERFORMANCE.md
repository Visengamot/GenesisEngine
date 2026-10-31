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
| Step mean / p95 | 3.034 / 5.648 ms |
| Full-frame mean / p95 | **14.488 / 19.402 ms** |
| Full-frame mean throughput (`1000 / mean ms`) | **69.03 FPS** |
| Managed allocation mean per measured frame | 41,176 B |
| GC collections during measurement (generations 0 / 1 / 2) | 2 / 1 / 1 |

Earlier single-texture baseline on the same machine: **17.323 ms mean / 68.474 ms p95**, 57.73 FPS mean throughput. A separate diagnostic run with the old cache and refreshed core measured 20 full terrain-refresh frames averaging **49.49 ms**, while 100 reused-terrain frames averaged **5.94 ms**. That attribution motivated splitting the terrain texture into six bands: steady state refreshes one band per new simulation tick instead of the whole visible map every sixth tick. The current sample has 120 one-band refresh frames and **19.40 ms p95**, still above the 16.67-ms frame budget: **sustained 60 FPS is not demonstrated**. Brush/world/camera changes synchronously refresh all six bands, so those interactions can still produce a long frame. Neighboring bands may show vegetation and food from different ticks (at most five ticks apart). Smoke confirmed startup and texture cleanup, but a visual screenshot comparison of band seams was not available. This is a single unpaired before/after run, so gains are not a universal hardware guarantee. The earlier synthetic benchmark injecting 600/2,000/10,000 agents measures a different workload.
