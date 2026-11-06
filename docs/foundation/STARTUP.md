# Startup — measured before and after the Foundation edits

Rule (Arun, 6 Nov): the app should load in milliseconds; Foundation edits may not add startup cost.

## Method
- `tools/StartupProbe` (dev-only, never shipped): a fresh process per run, **published Release**, that does what the Windows host does before the WebView paints: build the services (Windows registration on `main`; `AddChuvadiReader` on the branch) and render the real `App` (MainLayout + Home, every `OnInitialized`) through Blazor's `HtmlRenderer`. Reports runtime-to-Main, services, first render, total, methods JIT-compiled (`JitInfo`), assemblies loaded.
- Linux x64, .NET 10.0.403 runtime, 2-core sandbox; runs interleaved main/branch; 25–40 runs per figure; A/A noise floor ≈ 7 ms total.
- The WPF window and WebView2 part can only be timed on Windows: `scripts/measure-startup.ps1` (window-shown time, JIT or `-ReadyToRun`).
- Rendered HTML compared: identical to `main` on the Windows profile apart from the clock, whitespace and `·` written as an entity.

## Results (medians)
| Build | Services | First render | Total | Methods JIT-compiled | Assemblies |
|---|---|---|---|---|---|
| main c7b9e44, JIT | 26.0 ms | 198.4 ms | 256.3 ms | 1,372 | 41 |
| branch, JIT | 28.0 ms | 204.5 ms | 263.8 ms | 1,415 | 43 |
| main, ReadyToRun | 22.7 ms | 124.1 ms | 177.5 ms | 351 | 41 |
| branch, ReadyToRun | 23.1 ms | 126.3 ms | 177.3 ms | 357 | 43 |

- **ReadyToRun: no added cost** (177.3 vs 177.5 ms).
- **JIT build: +43 methods (+~7 ms, ~3 %)**, down from +82 after the startup fixes below. What remains is the platform-info objects' trivial accessors (~25), the new host-folder/account code (Group C, real function) and two small assemblies (Platform, Hosting).

## Startup fixes made on the branch
1. No `platform.js` module fetch: the setter lives in the already-loaded `theme.js`; Windows never calls it.
2. No `PlatformRoot`/`CascadingValue` at the app root (−25 JIT methods); MainLayout sets the attributes, non-Windows only.
3. Right-hand window buttons and the status text inline in MainLayout (no extra components on Windows).
4. Host kit registers with non-generic descriptors (no generic instantiations to JIT).

## Startup wins seen (proposals — not made)
| # | Win | Measured / expected | For | Against |
|---|---|---|---|---|
| S1 | **Done (D-50)** — Windows publish is ReadyToRun. **Publish the Windows app ReadyToRun** (`-r win-x64 -p:PublishReadyToRun=true`, or `<PublishReadyToRun>` in the Windows csproj) | −80 ms (−31 %) of Core/Ui startup here; JIT methods 1,415 → 357 | biggest single win; no code change; also erases every platform-layer JIT cost | app DLLs ~2–3× larger on disk; publish must name the runtime (win-x64 / win-arm64); slightly longer publish |
| S2 | ReadyToRun **composite** or partial **TieredPGO-friendly** settings (`TieredCompilation` quick-JIT for loops) | not measured | can shave more after S1 | bigger output; measure first |
| S3 | Find what the remaining ~124 ms first render spends (R2R build) — the whole Home page renders before first paint | to profile | likely large: Home, Library preview and recents all render up front | needs a profiler run on Windows |
| S4 | **Done (D-52)** — `StatusClock` re-renders only its span. MainLayout's 1-second clock timer re-renders the whole shell every second | runtime cost, not startup | rendering only the clock span would cut steady CPU | small Ui edit |
| S5 | Keep the splash off by default (already) and WebView2 profile reuse (already) | — | already in place | — |

Recommendation: S1 and S4 are done; next S3 on Arun's machine with `scripts/measure-startup.ps1 -ReadyToRun`.

## Re-check after the combined hooks (D-26, 6 Nov evening)
Same probe, 25 interleaved runs, before `be4498a` vs after `3358b00` (this sandbox runs slower than in the morning, so compare these rows with each other):

| Build | Total (median) | Methods JIT-compiled |
|---|---|---|
| before, ReadyToRun | 209.9 ms | 357 |
| after, ReadyToRun | 211.1 ms | 358 |
| before, JIT | 317.8 ms | 1,415 |
| after, JIT | 305.1 ms | 1,427 |

ReadyToRun (what Windows now ships): +1 method, +1.2 ms, inside the ≈7 ms noise floor. The phone, tablet and portal components are never rendered on Windows.
