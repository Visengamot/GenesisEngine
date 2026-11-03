# Производительность после перехода к ECS

> **Measured after rebuilding Release:** the tool and app were rebuilt against ECS commit `3ee920b` plus uncommitted terrain-navigation edits in `Core.cs` as of the run. This is a measurement of the combined state, **not** an isolated ECS speedup test.

## Reproducible method

Run the Release `tools/Benchmark` assembly from an isolated working directory so its hard-coded `benchmark-results.json` output does not overwrite the pre-migration baseline at the repository root. Explicit arguments: `600 2000 10000`. Each scenario constructs a seeded 160 × 90 world (`seed=42`), injects independently generated agents beyond the then-applicable birth limit of 600 (now 1,000), runs 30 unmeasured warm-up steps, then measures 120 complete `Simulation.Step(1f/30)` calls, reporting arithmetic mean, 95th percentile, maximum, and managed bytes allocated per measured step. All simulation systems remain enabled. The benchmark's p95 calculation takes sorted index 114 (zero-based) for 120 samples, rather than the nearest-rank index 113; compare outputs from the same executable methodology only.

Historical measurements are preserved in [`../benchmark-results.json`](<../benchmark-results.json>); the separate native-window baseline is [`../render-benchmark-results.json`](<../render-benchmark-results.json>). Headless step time is **not rendered FPS**. Figures below are single-run measurements, sensitive to concurrent workloads, JIT/GC and machine conditions; they are not confidence intervals or a controlled causal attribution to ECS alone.

## Headless results

New raw results: [`post-ecs-benchmark.json`](<post-ecs-benchmark.json>). Baseline: [`../benchmark-results.json`](<../benchmark-results.json>). Both report final population equal to injected population.

| Agents | Mean step: baseline → new (change) | p95 step: baseline → new | Max step: baseline → new | Bytes/step: baseline → new |
|---:|---:|---:|---:|---:|
| 600 | 6.466 → **8.354 ms (+29%)** | 11.616 → **17.123 ms** | 14.965 → 25.332 ms | 33,699 → **162,471 B (4.82×)** |
| 2,000 | 4.352 → **4.572 ms (+5%)** | 12.080 → **7.476 ms** | 19.989 → 14.232 ms | 136,706 → **653,511 B (4.78×)** |
| 10,000 | 18.359 → **27.445 ms (+49%)** | 19.867 → **31.723 ms** | 272.489 → 185.058 ms | 602,871 → **2,940,066 B (4.88×)** |

**Interpretation:** 10,000-agent headless mean and p95 regress, and managed allocations rise roughly fivefold at all population sizes; 2,000-agent p95 improves in this one run but does not negate its increased allocations. The counterintuitive 600-agent mean exceeding 2,000-agent mean in *both* runs reinforces sampling/noise caveats. Do not claim an ECS performance improvement from these data. The benchmark's embedded `note` still says "no ECS claim": that static note was not updated by the migration and is not a statement about the rebuilt assembly.

## Native renderer

The existing native baseline covers 600 / 2,000 / 10,000 agents, 30 warm-up and 120 measured frames per scenario, with one full simulation step per frame, full world/HUD drawing, and `EndDrawing`. Historical baseline (not post-ECS):

| Injected agents | Mean step | p95 step | Mean full frame | p95 full frame | Mean throughput |
|---:|---:|---:|---:|---:|---:|
| 600 | 4.333 ms | 9.723 ms | 13.917 ms | 49.592 ms | 71.86 FPS |
| 2,000 | 4.576 ms | 6.449 ms | 16.661 ms | 57.888 ms | 60.02 FPS |
| 10,000 | 18.164 ms | 20.323 ms | 40.977 ms | 75.082 ms | 24.40 FPS |

The rebuilt native run completed all three scenarios in a 1480 × 940 window on AMD Radeon Vega 8. Raw data: [`post-ecs-render-benchmark.json`](<post-ecs-render-benchmark.json>). Population stayed constant; each scenario recorded 120 ticks after 30 warm-up ticks. Same machine/runtime (`.NET 8.0.24`, eight logical processors) as historical native baseline.

| Agents | Mean step: baseline → new | Mean full frame: baseline → new | p95 full frame: baseline → new | Mean throughput: baseline → new | Mean managed bytes/frame: baseline → new |
|---:|---:|---:|---:|---:|---:|
| 600 | 4.333 → **4.351 ms** | 13.917 → **13.456 ms** | 49.592 → **53.052 ms** | 71.86 → **74.32 FPS** | 42,001 → **168,706 B** |
| 2,000 | 4.576 → **6.709 ms** | 16.661 → **18.209 ms** | 57.888 → **55.509 ms** | 60.02 → **54.92 FPS** | 145,627 → **660,817 B** |
| 10,000 | 18.164 → **27.054 ms** | 40.977 → **47.360 ms** | 75.082 → **81.914 ms** | 24.40 → **21.11 FPS** | 611,429 → **2,948,155 B** |

The 10,000-agent scenario regresses by 6.38 ms (+16%) mean full-frame time and 6.83 ms p95; its managed bytes/frame rise 4.82×. Its new mean frame is 47.36 ms, well above the 16.67-ms budget for 60 FPS. Even the 600-agent mean-throughput gain does **not** imply sustained 60 FPS: its p95 frame is 53.05 ms. Full-frame CPU times include presentation and other runtime waits; renderer-only times must not be interpreted as isolated GPU results. The native benchmark writes `render-benchmark-results.json` into its process working directory; this run used an isolated directory to preserve the root baseline. Mean throughput is `1000 / mean frame ms`.

## Limitations

- The public `Agents` list remains authoritative across phases; ECS component storage is synchronized to it, and agents/brains are still object-backed. This benchmark does not isolate ECS memory bandwidth or quantify per-system improvements.
- At the time of this measurement the game cap was 600; it is now 1,000. Injected populations beyond the applicable cap are synthetic, not typical sustained gameplay. Final populations should be reported, because births/deaths change load.
- A 120-step sample (~4 seconds of simulation time) does not establish long-run stability, mutation/reproduction costs, or sustained GC pressure.
- Renderer CPU wall-clock times may include driver/compositor presentation waits; they are not GPU timestamps, and an uncapped target does not guarantee real display FPS.
- The rebuilt state also includes uncommitted terrain-navigation edits (movement sweep). No separate ECS-only binary was measured. A separate test process may have run during the headless measurement; the baseline and new runs were not paired or load-isolated. Repeat on a quiet machine at an identified immutable commit before treating small changes as reliable.
