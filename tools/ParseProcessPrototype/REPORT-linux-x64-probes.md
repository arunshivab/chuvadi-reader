# Parse-process prototype — Ubuntu 24.04.5 LTS, X64, 2 cores, .NET 10.0.14
engine: heap cap 512 MB (DOTNET_GCHeapHardLimit), time limit 10 s per request, crash watch on

## Stack sizes
app process: main 8156 KB, thread pool 8192 KB, new Thread() 8192 KB
engine process: main 8156 KB, thread pool 8192 KB, new Thread() 8192 KB

## 1. Starting the engine (process start to first answer)
first 434.3 ms; then median 35.6 ms (min 33.5, max 56.4); engine memory idle 27 MB
first document, open and page 1 (a real A1 CAD drawing (not in repo)): today, in a running app 6660.8 ms; with an engine started for it (start included) 6714.1 ms; with an engine already running, see table 2
wire, a payload sent and returned (twice the one-way cost): 1 KB 0.1 ms, 100 KB 0.4 ms, 1000 KB 3.3 ms, 6000 KB 26.0 ms

## 2. PDF pages (open, then the first 8 pages), warm: one file drawn first on both sides
| file | pages | SVG KB/page | open in-process | open engine | page in-process (median) | page engine (median) | overhead/page |
|---|---|---|---|---|---|---|---|
| a real A1 CAD drawing (not in repo) | 5 | 3272 | 10.9 ms | 20.0 ms | 6565.8 ms | 6824.3 ms | 258.5 ms |
| inherited-cropbox.pdf | 1 | 0 | 0.2 ms | 0.6 ms | 0.8 ms | 0.7 ms | -0.1 ms |
| journal-10p.pdf | 10 | 161 | 0.7 ms | 5.8 ms | 30.6 ms | 31.6 ms | 1.0 ms |
engine memory after drawing: 99 MB

## 3. Word and Excel (load in-process, against load in the engine plus the model sent back as JSON)
| file | in-process | engine | JSON KB |
|---|---|---|---|
| discharge.docx | 90.4 ms | 55.4 ms | 9 |
| landscape.docx | 24.2 ms | 67.0 ms | 1 |
| layout.xlsx | 1.3 ms | 1.6 ms | 20 |
| realistic.xlsx | 5.5 ms | 14.4 ms | 585 |

## 4. Failure: the engine dies or runs away; the app carries on
- stack overflow (uncatchable): engine ended (the runtime said "Stack overflow."; ended at once), noticed after 41.8 ms; new engine, file reopened and page 1 drawn again in 6887.7 ms (2518 KB). App still running: yes.
- endless loop: engine passed its time limit (10 s) and was ended, noticed after 10015.2 ms; new engine, file reopened and page 1 drawn again in 5941.1 ms (2518 KB). App still running: yes.
- allocates without end: engine refused and stayed up: OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown., noticed after 163.9 ms; new engine, file reopened and page 1 drawn again in 5766.1 ms (2518 KB). App still running: yes.

## 5. Probe files (each in its own engine; the app must stay up)
- bomb-95kb-empty-p-11m.docx: refused: DocumentViewException: This Word document is too large to open: its contents unpack to more than Chuvadi allows. It may be damaged. after 43.8 ms. App still running: yes.
- c08_softmask_cycle.pdf: engine ended (the runtime said "Stack overflow."; ended at once) after 169.4 ms. App still running: yes.
- c09_type3_self.pdf: engine ended (the runtime said "Stack overflow."; ended at once) after 165.6 ms. App still running: yes.
- c23_cff_subr_self_call.pdf: engine ended (the runtime said "Stack overflow."; ended at once) after 183.0 ms. App still running: yes.
- hyperlinks-248.docx: refused: DocumentViewException: This Word document is nested more deeply than Chuvadi allows, so it isn't opened. It may be damaged, or made to attack the reader. after 165.8 ms. App still running: yes.
- ins-248.docx: refused: DocumentViewException: This Word document is nested more deeply than Chuvadi allows, so it isn't opened. It may be damaged, or made to attack the reader. after 121.4 ms. App still running: yes.
- sec-s1-nested-groups.xlsx: engine ended (the runtime said "Stack overflow."; ended at once) after 5351.9 ms. App still running: yes.
- sec01-6m-cells.xlsx: refused: OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown. after 4082.1 ms. App still running: yes.
- sec08-nested-brackets.xlsx: loaded after 246.2 ms. App still running: yes.
- sec10-wildcard.xlsx: loaded after 192.5 ms. App still running: yes.
