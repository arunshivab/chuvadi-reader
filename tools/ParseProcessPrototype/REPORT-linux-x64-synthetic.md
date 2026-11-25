# Parse-process prototype — Ubuntu 24.04.5 LTS, X64, 2 cores, .NET 10.0.14
engine: heap cap 512 MB (DOTNET_GCHeapHardLimit), time limit 10 s per request, crash watch on

## Stack sizes
app process: main 8160 KB, thread pool 8192 KB, new Thread() 8192 KB
engine process: main 8160 KB, thread pool 8192 KB, new Thread() 8192 KB

## 1. Starting the engine (process start to first answer)
first 43.4 ms; then median 42.3 ms (min 33.3, max 97.9); engine memory idle 27 MB
first document, open and page 1 (synthetic-cad-a1.pdf): today, in a running app 6724.9 ms; with an engine started for it (start included) 6807.0 ms; with an engine already running, see table 2
wire, a payload sent and returned (twice the one-way cost): 1 KB 0.1 ms, 100 KB 0.5 ms, 1000 KB 3.4 ms, 6000 KB 19.4 ms

## 2. PDF pages (open, then the first 8 pages), warm: one file drawn first on both sides
| file | pages | SVG KB/page | open in-process | open engine | page in-process (median) | page engine (median) | overhead/page |
|---|---|---|---|---|---|---|---|
| synthetic-cad-a1.pdf | 5 | 640 | 5.4 ms | 4.2 ms | 4458.4 ms | 4349.9 ms | -108.5 ms |
| journal-10p.pdf | 10 | 161 | 0.8 ms | 3.7 ms | 24.5 ms | 28.5 ms | 3.9 ms |
| inherited-cropbox.pdf | 1 | 0 | 0.2 ms | 0.3 ms | 0.3 ms | 0.3 ms | 0.0 ms |
engine memory after drawing: 102 MB

Engine memory per file (GC and process; on Windows the job's peak, which is what the cap counts):
- after synthetic-cad-a1.pdf: GC heap 218 MB, committed 429 MB, fragmented 93 MB, GC limit 512 MB, peak working set 537 MB, private now 605 MB, allocated in all 17666 MB
- after journal-10p.pdf: GC heap 24 MB, committed 30 MB, fragmented 17 MB, GC limit 512 MB, peak working set 552 MB, private now 161 MB, allocated in all 17816 MB
- after inherited-cropbox.pdf: GC heap 24 MB, committed 30 MB, fragmented 17 MB, GC limit 512 MB, peak working set 552 MB, private now 161 MB, allocated in all 17816 MB

## 3. Word and Excel (load in-process, against load in the engine plus the model sent back as JSON)
| file | in-process | engine | JSON KB |
|---|---|---|---|
| discharge.docx | 35.1 ms | 33.2 ms | 9 |
| landscape.docx | 59.8 ms | 33.4 ms | 1 |
| layout.xlsx | 1.5 ms | 1.7 ms | 20 |
| realistic.xlsx | 7.4 ms | 14.5 ms | 585 |

## 4. Failure: the engine dies or runs away; the app carries on
- stack overflow (uncatchable): engine ended (the runtime said "Stack overflow."; ended at once), noticed after 28.2 ms; new engine, file reopened and page 1 drawn again in 4999.1 ms (631 KB). App still running: yes.
- endless loop: engine passed its time limit (10 s) and was ended, noticed after 10011.6 ms; new engine, file reopened and page 1 drawn again in 4835.5 ms (631 KB). App still running: yes.
- allocates without end: engine refused and stayed up: OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown., noticed after 196.0 ms; new engine, file reopened and page 1 drawn again in 4992.0 ms (631 KB). App still running: yes.

## 5. Probe files (each in its own engine; the app must stay up)
- bomb-95kb-empty-p-11m.docx: refused: DocumentViewException: This Word document is too large to open: its contents unpack to more than Chuvadi allows. It may be damaged. after 55.8 ms. App still running: yes.
- c08_softmask_cycle.pdf: engine ended (the runtime said "Stack overflow."; ended at once) after 919.4 ms. App still running: yes.
- c09_type3_self.pdf: engine ended (the runtime said "Stack overflow."; ended at once) after 135.1 ms. App still running: yes.
- c23_cff_subr_self_call.pdf: engine ended (exit code 134) after 171.0 ms. App still running: yes.
- hyperlinks-248.docx: refused: DocumentViewException: This Word document is nested more deeply than Chuvadi allows, so it isn't opened. It may be damaged, or made to attack the reader. after 101.8 ms. App still running: yes.
- ins-248.docx: refused: DocumentViewException: This Word document is nested more deeply than Chuvadi allows, so it isn't opened. It may be damaged, or made to attack the reader. after 107.2 ms. App still running: yes.
- sec-s1-nested-groups.xlsx: engine ended (the runtime said "Stack overflow."; ended at once) after 7182.3 ms. App still running: yes.
- sec01-6m-cells.xlsx: refused: OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown. after 3549.0 ms. App still running: yes.
- sec08-nested-brackets.xlsx: loaded after 171.5 ms. App still running: yes.
- sec10-wildcard.xlsx: loaded after 168.6 ms. App still running: yes.
