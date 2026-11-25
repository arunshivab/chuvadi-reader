# A separate parse process on every host (D-198 B2): design

Foundation, 18 Nov. **Design for Arun, before any build. Nothing in the Reader has changed.** The numbers come from a
prototype that is in the repo for testing only (`tools/ParseProcessPrototype`). Every host chat can run it and add
its own numbers (section 9).

## 1. Short answer

- **The Reader is split in two.** The app keeps the screen and everything the person does: tabs, marks, bookmark
  edits, Arrange's desks, unsaved work, settings. A second process, the **engine**, is the only place where the
  Chuvadi libraries read a file: PDF, Word, Excel. They talk over a private pipe, in one simple format.
- **A crash costs the document, not the app.** A file that kills the engine kills only the engine. The app
  notices within milliseconds, starts a new engine and opens the other tabs' files again. The person keeps
  everything they had done. Measured here:
  - an uncatchable stack overflow was noticed in 15 ms, and the file was open again with page 1 drawn in 150–190 ms;
  - a runaway file is ended at its time limit;
  - a decompression bomb is refused in about 1 second (in-process today it took 27 seconds and then ran out of memory).
- **The cost is small where it matters.**
  - App startup: none, because the engine starts after the first screen is up.
  - Drawing a page: within the noise of the measurements (pages take 8–470 ms to draw; moving even a 700 KB page
    costs about 1–2 ms).
  - The first document opened: about 25 ms more, only if the engine had not started yet.
  - Memory: about 27 MB for an idle engine, on top of what documents use today.
- **One thing must change shape: Excel.** Sending a whole workbook's model across is fine for normal files, but a
  200,000-cell sheet becomes 100 MB of data, which is wrong. The engine should send the sheet's layout once, then
  the cells for the part on screen.
- **Hosts differ in how the engine starts, not in what it does.**
  - Windows and Linux: a small engine program next to the app.
  - Mac: the app's own program started a second time in engine mode.
  - Android: a second process of the app, run as an Android service.
  - Portal: each person's worker gets an engine of its own. Then a crash no longer ends that person's whole
    session, as it does today.
- **Effort:** this is the largest piece of work since the platform layer itself. My estimate is 4 to 6 patches over
  2 to 3 weeks of my time, plus each host chat's own wiring and its tests. Section 10 has the order.

## 2. Where the boundary sits

Today, Core's services call the libraries directly, inside the app (51 PDF open sites in 18 files, plus the Word and
Excel loaders). Each service splits into two halves:

- **App half (stays in Core and the Ui):** the same public services the Ui calls today: `IPdfReader` and
  `IPdfSession`, `DocxViewService`, `XlsxViewService`, `PdfToolsService`, `BenchService` and so on. They keep their
  signatures, so the Ui barely changes. Inside, each sends a request to the engine instead of calling the library.
- **Engine half (new project, `ChuvadiReader.Engine`):** the code that today calls the libraries, moved as it is.
  It answers requests and holds open documents by an id.
- **The contract** lives in `ChuvadiReader.Platform` (`IParseEngineHost`). It is the only new thing each host must
  provide: start an engine process, give back its two streams, end it.
- **The kit** (`ChuvadiReader.Hosting`) supplies everything else, the same on every host: the client, time limits,
  noticing a death, restarts, holding back after repeated crashes, reopening documents.

**What crosses, per kind of work.** Requests are a few bytes. Answers are text or bytes. No objects are shared,
so one side's crash cannot corrupt the other.

| Work | App → engine | Engine → app | Size measured |
|---|---|---|---|
| Open a PDF | file (path, or the file handle where the host must pass it; section 4), password | page count, page sizes, layers, encrypted / signed / tagged | under 1 KB to a few KB |
| Draw a page | document id, page, layer overrides, background | SVG text; for large-format pages the raster image bytes | 34 to 740 KB per page on the test files; up to 6 MB (raster fallback limit) |
| Find in the page | nothing: Find searches the drawn page's text in the Ui | | |
| Page text for copy, measure, properties, outline, signatures, layers | document id, page | small results (text, numbers, lists) | under 100 KB |
| Library, folder search, thumbnails | path | title, page count, text matches, a small thumbnail | small |
| Word | file | the document model (paragraphs, runs, tables, images as data) | 9 KB to 1 MB (2,000 paragraphs and a 200-row table: 1 MB, 90 ms) |
| Excel | file, then "cells in this range" as the person scrolls | sheet layout once (sizes, merges, frozen panes), then cells by range | today's whole model: 20 KB for a normal sheet, **100 MB for 200,000 cells**, so it must be sent by range |
| Tools that write a file: Arrange (bind, combine, lift, scatter, impose, export), Merge, Split, Extract, Stamp, Watermark, Flatten, Password, Sign, Press, Export as image | the job (files, pages, settings) and where to write | progress, then the finished file's path | small; the output goes straight to disk |

**One engine, or more?** This is a real choice (option A in section 8). My recommendation:

- one long-lived **viewing engine** shared by all tabs;
- a short-lived **tool engine** for each writing job (bind, merge, stamp and the rest).

A crash while writing a file then never touches the documents on screen. A crash while viewing loses no work,
because no work lives in the engine.

## 3. Limits: memory, time, stack

- **Memory.**
  - Every engine runs with a managed-heap cap. The libraries are all managed code, so this is the cap that
    matters. Measured: with a 512 MB cap, a page that unpacks to 560 MB was refused with a clean "out of memory"
    after 1 second, and the engine stayed up to serve the next request. Without a cap, in the app today, the same
    file ran 27 seconds and then ran out of memory inside the app.
  - On top of that, an operating-system cap where the host has one (section 4), for memory the runtime itself uses.
  - Proposed caps:
    - desktop viewing engine: the smaller of 2 GB and half the machine's memory;
    - tool engine: the same;
    - Android: 512 MB;
    - portal: 1 GB per engine, with the worker itself capped lower once parsing has left it (section 6).
- **Time, per request.** The app keeps the clock and ends the engine when a request passes its limit. Measured: an
  endless loop was ended exactly at the 10 s limit, and the file was open again 160 ms later. Proposed limits:

  | Request | Limit | Why |
  |---|---|---|
  | Open | 30 s | the largest real files open in under 1 s here |
  | Draw a page | 30 s | real pages take 8–470 ms here; a vector CAD sheet can take seconds |
  | Word or Excel load | 60 s | |
  | Excel cells for a range | 10 s | |
  | Writing jobs | none overall, but 60 s with no progress ends it | a 2,000-page bind is legitimately long |

  The person can always stop a job, as today.
- **Stack.**
  - The engine does all parsing on its own threads, created with a stated stack of 16 MB. Docs 2.4.0's crashers need
    more than 256 KB and load with 1 MB, so a larger stack removes that whole class on every host, whatever the
    host's default.
  - Measured defaults here (Linux): 8 MB for the main thread, the thread pool and new threads. Word loads on the
    thread pool today.
  - Windows, Mac and Android defaults are smaller and must be measured there (section 9). The engine's own threads
    make the answer stop mattering.

## 4. Each host

| Host | How the engine starts | Ends with the app | Memory cap from the system | What is not possible or not proven |
|---|---|---|---|---|
| **Windows** | A small engine program (`ChuvadiReader.Engine.exe`) in the app's folder, using the same .NET and library files (the app is framework-dependent, so no extra runtime is shipped) | A job object with "kill on close": if the app dies, the engine dies with it | The job object's per-process memory limit: a real hard cap | Nothing known. Start time and memory must be measured on Windows; process start is slower than on Linux |
| **Linux** | The same small engine program beside the app | `PR_SET_PDEATHSIG`: the kernel ends the engine when the app dies | Only the managed-heap cap is certain. A cgroup memory cap needs a systemd user session (most desktops have one; Flatpak and some setups do not), so it is used when present. `RLIMIT_AS` is not usable: .NET reserves large address space and would fail to start | Measured here: start 33–54 ms, idle 27 MB |
| **Mac (Catalyst)** | The app's own program started a second time with an "engine" switch, checked in `Main` before anything of UIKit starts. Nothing extra to ship, no second runtime | The app passes a pipe; the engine ends when the pipe closes | No per-process cap on macOS short of root; the runtime's own heap cap is used | **To prove on a Mac:** that .NET's process start works in a Catalyst app; that the Catalyst runtime (Mono by default) honours a heap cap (`MONO_GC_PARAMS=max-heap-size`, not the CoreCLR setting); and how Mono reports a stack overflow. **If the app is ever sandboxed** (it is not today: `app-sandbox` is false), the engine needs the inherit entitlement, and files the person picked may have to be passed as open handles. Alternative: an XPC service, Apple's own way (option C) |
| **Android** | Android cannot start a second executable from an app (newer versions forbid running files from the app's storage). The engine is instead an Android **service declared in its own process** (`android:process=":engine"`): the system starts a second process of the same app, which starts .NET again and runs only the engine | The app binds to the service; if the app goes, the system ends its process. The app can also end the engine itself (same user) | Each process gets its own memory from the system, which kills it under pressure; the runtime's heap cap applies inside it | **To prove on a device:** how long the second process takes to start (likely several hundred ms; Android Test to measure); the pipe (Android's binder limits one message to 1 MB, so pages travel over a local socket or a pipe the service hands back); Mono's stack-overflow behaviour. A stricter `isolatedProcess` engine (no file access at all, files passed as handles) is possible but unproven with .NET (option D) |
| **Portal** | Each person's worker (already a separate process) starts its own engine, the same way as Linux | The worker's death ends its engine | cgroup per engine where the portal runs under systemd (production does); heap cap always | Measured: the Linux numbers above apply |

**Files the engine opens.**
- Windows, Linux, Mac (unsandboxed) and the portal: the engine opens the same path the app would.
- Android: files arrive as content URIs that the app copies or opens today. The app opens them and passes the open
  handle to the engine, which never needs storage permission.

## 5. When the engine dies: what the person sees, and their work

**Where the work lives.** Unsaved work is never in the engine, so it survives every engine death:

- marks and their undo history;
- bookmark edits;
- Arrange's desks;
- signature placements;
- the last view of each tab (page, zoom, layers).

All of these live in the app. The engine holds only open files and caches, which can be rebuilt.

**What happens.**
1. The app notices at once: the pipe closes, or a time limit passes and the app ends the engine. It knows which
   request was running, so it knows which file did it.
2. It starts a new engine and quietly reopens every other open file. Each tab returns to its page; the person may
   see the pages redraw.
3. The tab whose file was being read gets a bar over the page:
   - "This file stopped Chuvadi's reader. Your marks and changes are kept. **Try again** · **Close the file**"
   - with the reason in plain words: "it took too long", "it needed too much memory", or "the reader stopped
     while reading it".
4. **No loops.**
   - A file that stops the engine twice in one run is not reopened automatically again: "This file stops the reader
     each time. It may be damaged or made to attack the reader."
   - Saving its marks into a new file is still offered. That needs the engine to read the file again, so the person
     is asked first.
5. **Writing jobs.** If a tool engine dies, the job reports "Couldn't finish: the file stopped the reader" and
   nothing half-written is left. Jobs write to a temporary name and rename only at the end, as most do today.
6. **Slowing repeated crashes.** After 3 engine deaths in 1 minute, the app waits before starting the next engine
   (5, then 20, then 60 seconds). It says so on the tabs that are waiting, so a hostile file cannot keep a
   processor busy restarting.

## 6. The portal: the same contract, and its hardening

**Today** each signed-in person has a worker process. The worker is the whole Reader for that person, and parsing
happens inside it. A crash therefore ends their session, with every open document and their unsaved work, and the
gateway shows "Your session stopped, please reload". The gateway itself survives: it is a separate process, and Web
Test has killed and frozen workers on purpose.

**With this design:**

- **The worker gets an engine of its own.** A hostile upload kills only that engine. The person's session, tabs and
  unsaved work stay, and they see the same bar as on desktop.
- **Operating-system memory cap.**
  - Engine: a cgroup cap of 1 GB, plus the heap cap.
  - Worker: once parsing has left it, it needs much less. Today's managed-heap cap of 1,536 MB can come down, with a
    cgroup cap added.
- **Time limits:** as section 3, kept by the worker for its engine.
- **Single mode refused in production.**
  - Single mode runs everything in one process. Today it is the default and is meant for development.
  - The portal will refuse to start in Single mode when the environment is Production, saying why. Development
    stays as it is.
- **A message when a worker dies.** If a worker itself still dies (now much less likely), the gateway's page names
  what happened in plain words and offers "Start again". The worker's own folder is wiped as today.
- **Slowing repeat crashes per person.**
  - The same 3-in-a-minute rule applies per person, for engines and for workers.
  - A file that has stopped an engine twice is refused at upload for that person for the rest of the session, with
    the reason.

The Web chat would build the gateway's parts; I would build the worker-engine contract in the kit.

## 7. Cost, measured (Linux, 2 cores; section 9 for the other hosts)

| What | Measured |
|---|---|
| Starting an engine, to its first answer | 33–54 ms (first ever: 54–88 ms) |
| Engine memory, idle / after drawing 5 files | 27 MB / 140 MB (the documents' memory, moved out of the app) |
| Moving data on the pipe, there and back | 100 KB 0.5 ms; 1 MB 3 ms; 6 MB 23 ms |
| A page drawn, in-process against the engine (median of 3 rounds, 8 pages each) | 9.2 / 8.0 ms; 136 / 113 ms; 80 / 83 ms; 467 / 462 ms; 270 / 264 ms: the same within noise |
| Opening a PDF | 0.5–10 ms in-process, 0.7–21 ms in the engine |
| First document in a running app: open and draw page 1 | today 127 ms; engine started for it 151 ms; engine already running: as today |
| Word: 9 KB and 1 MB models | 31 / 84 ms and 88 / 90 ms; the first includes the cost of turning the model into text, which a generated serializer will cut |
| Excel: normal sheet / 200,000 cells, whole model | 1 / 3 ms; **474 ms in-process, 814 ms and 100 MB through the engine**, so sent by range instead (option B) |
| App startup | unchanged: the engine starts about a second after the first screen, in the background |

## 8. Options for Arun

**A. How many engines**

| Option | For | Against |
|---|---|---|
| A1. One engine for everything | Least memory (one extra process); simplest | A crash while writing a bound PDF also reloads every tab; a slow job blocks drawing pages |
| **A2. One viewing engine, plus one short-lived engine per writing job (recommended)** | Viewing never waits behind a job and is never disturbed by one; a crash is confined to what caused it | Two processes during a job (about 30 MB more for its length) |
| A3. One engine per open document | Strongest isolation: one bad file touches nothing else | Memory and start time grow with tabs (27 MB and about 40 ms each, much more on Android); Arrange reads pages from many files at once, which would mean many engines |

**B. Excel through the boundary**

| Option | For | Against |
|---|---|---|
| **B1. Layout once, then cells by visible range (recommended)** | Constant cost whatever the sheet's size; the engine can drop the rest from memory | The Excel viewer changes from "has the whole sheet" to "asks for what it shows": real work in `XlsxView` and its find and resize code |
| B2. The whole model, in a compact binary form | Little change to the viewer | Still tens of MB for large sheets, all held in the app; the 100 MB case becomes perhaps 20 MB, not small |

**C. Mac**

| Option | For | Against |
|---|---|---|
| **C1. The app's own program in engine mode (recommended)** | Nothing extra to ship; same model as Windows and Linux; works today because the app is not sandboxed | Depends on process start working in Catalyst (to prove); a sandboxed future needs entitlement work |
| C2. An XPC service | Apple's own design: the system starts, restarts and isolates it; fits the App Store and sandboxing | Needs native glue between .NET and XPC; a second bundle to build and sign; more work for the Mac chat |

**D. Android**

| Option | For | Against |
|---|---|---|
| **D1. A service in its own process (recommended)** | Supported Android pattern; the engine reads the handles the app passes | Second start of .NET costs time and memory (to measure) |
| D2. The same, as an isolated process | Strongest sandbox: the engine can touch nothing but what it is handed | Unproven with .NET; every file and font passed as a handle; harder to debug |

**E. When the engine starts**

| Option | For | Against |
|---|---|---|
| **E1. In the background about a second after the first screen (recommended on desktop)** | The first document opens as fast as today | About 27 MB used even if no document is opened |
| E2. When the first document opens (recommended on Android, and for tool engines) | No cost until needed | The first document is about 25 ms slower on Linux; more on Android |

**F. Signing with a certificate**

Signing reads the PDF, so it belongs in a tool engine. The certificate's private key would then cross to that engine
(same user, same machine, a private pipe).

| Option | For | Against |
|---|---|---|
| **F1. Pass the certificate to the tool engine for that one job (recommended)** | Simple; the engine is short-lived and ends after the job | The key exists briefly in a second process |
| F2. The engine prepares the signature, the app signs the hash and sends it back | The key never leaves the app | Needs the library to sign in two steps (to ask the library chat); more round trips |

## 9. What each host chat should run

- **Command, in the repo:** `dotnet run -c Release --project tools/ParseProcessPrototype -- bench <folder>`, where
  the folder holds a few PDFs, a .docx and a .xlsx. An optional `probes` subfolder holds the library chats' probe
  files. It prints the report above for that machine: stack sizes, engine start, the wire, pages, Word and Excel,
  and crash behaviour.
- **Windows and Linux chats:** as is. The Windows chat should add the job object's memory cap; I will add a switch
  for it.
- **Mac and Android chats:** the prototype is a console program. They need a small host-side version, which I will
  write with them if Arun chooses this design. Their numbers matter most, because their runtime (Mono) and process
  model differ.
- **Web chat:** run it on the portal's server.
- **Probe files:** when Arun uploads the library chats' probe zips, they go in `probes` (opened only by the
  engine). The prototype must then show sec08, sec-s1, hyperlinks-248 and c23 each killing or being refused by an
  engine while the app stays up. The synthetic stack overflow above already shows the mechanism.

## 10. Build order, if Arun approves

1. **The contract and the engine, with PDF viewing.** `IParseEngineHost`, the engine project, `IPdfSession` over
   the wire, death and restart handling, and the bar on the tab. Windows and Linux hosts first. Proof: every
   existing proof and test, pixel-identical pages, the crash checks, startup unchanged.
2. **Word and Excel.** Excel by range (option B1).
3. **The rest:** writing jobs in tool engines, and the smaller readers (thumbnails, properties, signatures, outline,
   folder search, measure).
4. **Mac and Android** with their chats; their proofs on their machines.
5. **The portal's worker-engine,** and the gateway hardening with the Web chat.
6. **The library move (D-198 A1)** can come before or after. The engine is where the shared PDF open helper with
   `PdfLimits` will live.

## 11. Not measured here, and what is assumed

- Windows, Mac and Android numbers: not run here (section 9).
- That Catalyst can start a process, and how Mono handles a heap cap and a stack overflow on Mac and Android:
  assumed from documentation, to prove.
- The library chats' real probe files: not yet received. A synthetic uncatchable stack overflow, an endless loop,
  endless allocation, a decompression bomb and four crafted structure files were used instead. All were contained
  or refused.
- The JSON serializer in the prototype is the slow, general one. The real contract uses generated code (also
  needed for trimming), so the Word timings above are an upper bound.

## 12. Windows, measured by Arun (18 Nov), and what step 1 does about it

Arun ran the bench on his PC: Windows 11 10.0.26200, x64, 12 cores, .NET 10.0.15, with only the 512 MB heap cap.

| What | Windows | Linux (section 7) |
|---|---|---|
| Starting an engine | 153 ms first, then 51 ms (49–53) | 33–54 ms |
| Engine memory, idle / after drawing | 23 MB / 71 MB | 27 MB / 140 MB |
| 6 MB on the pipe, there and back | 7 ms | 23 ms |
| Pages, engine against in-process | the same (16.0 / 16.0 ms; a CAD sheet 3.5 / 3.3 s) | the same |
| Stack, every thread | **1,536 KB** | 8 MB |
| **A crash noticed** | **5.7–7.4 s** (stack overflows, c08, c09, c23); sec-s1 reached the 10 s limit | 15–30 ms |

**Probe files on Windows.** Every one was contained, and the app stayed up each time:

| Outcome | Files |
|---|---|
| Engine ended | c08, c09, c23 |
| Ended at the time limit | sec-s1 |
| Refused | sec01 (out of memory at 512 MB, 2.1 s), the 11-million-paragraph bomb, hyperlinks-248, ins-248 |
| Loaded | sec08, sec10 (conditional formats are off) |

**What this changes in step 1:**
- **The 16 MB parse threads (section 3) are what makes Windows safe.** Windows gives every thread only 1.5 MB, so its default is the tightest of all the hosts.
- **Noticing a crash: from seconds to milliseconds.** On Windows a dying process is held while the system's error reporting runs, and the runtime first writes the overflowed stack to stderr. Step 1 does three things:
  1. The engine runs in a **job object** with three settings:
     - "die on unhandled exception": no error-reporting round;
     - "kill on job close": the engine ends with the app;
     - the **hard memory cap** from section 3.
  2. The engine switches off fault dialogs and error reporting for itself as its first act (`SetErrorMode`, `WerSetFlags`).
  3. The app always reads the engine's stderr. The moment the runtime writes "Stack overflow", "Fatal error" or "Process terminated", it ends the engine and starts the next one. It does not wait for the process to finish dying.
- **All three are in the bench now** (`--job-mb N` for the job object's cap, and the crash watch, on by default; `--no-crash-watch` to compare), so Arun can re-run it and see the difference before step 1 is built. On Linux the crash watch makes no difference: 12–19 ms either way.
