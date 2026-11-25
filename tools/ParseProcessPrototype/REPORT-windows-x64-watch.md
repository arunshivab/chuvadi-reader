# Parse-process prototype ΓÇö Microsoft Windows 10.0.26200, X64, 12 cores, .NET 10.0.15
engine: heap cap 512 MB (DOTNET_GCHeapHardLimit), time limit 10 s per request, job object 1024 MB cap, crash watch on

## Stack sizes
app process: main 1536 KB, thread pool 1536 KB, new Thread() 1536 KB
engine process: main 1536 KB, thread pool 1536 KB, new Thread() 1536 KB

## 1. Starting the engine (process start to first answer)
first 313.4 ms; then median 117.1 ms (min 107.9, max 187.8); engine memory idle 24 MB
first document, open and page 1 (synthetic-cad-a1.pdf): today, in a running app 3897.2 ms; with an engine started for it (start included) 3762.8 ms; with an engine already running, see table 2
wire, a payload sent and returned (twice the one-way cost): 1 KB 0.0 ms, 100 KB 0.2 ms, 1000 KB 1.1 ms, 6000 KB 7.1 ms

## 2. PDF pages (open, then the first 8 pages), warm: one file drawn first on both sides
| file | pages | SVG KB/page | open in-process | open engine | page in-process (median) | page engine (median) | overhead/page |
|---|---|---|---|---|---|---|---|
| synthetic-cad-a1.pdf | 5 | 640 | 2.2 ms | 3.6 ms | 2613.9 ms | 2606.0 ms | -7.9 ms |
| journal-10p.pdf | 10 | 161 | 1.3 ms | 1.2 ms | 14.7 ms | 16.5 ms | 1.8 ms |
| inherited-cropbox.pdf | 1 | 0 | 0.4 ms | 0.3 ms | 0.3 ms | 0.5 ms | 0.2 ms |
engine memory after drawing: 437 MB

Engine memory per file (GC and process; on Windows the job's peak, which is what the cap counts):
- after synthetic-cad-a1.pdf: GC heap 174 MB, committed 184 MB, fragmented 34 MB, GC limit 512 MB, peak working set 455 MB, private now 367 MB, allocated in all 17666 MB; job peak 435 MB
- after journal-10p.pdf: GC heap 302 MB, committed 313 MB, fragmented 0 MB, GC limit 512 MB, peak working set 455 MB, private now 412 MB, allocated in all 17817 MB; job peak 435 MB
- after inherited-cropbox.pdf: GC heap 306 MB, committed 315 MB, fragmented 0 MB, GC limit 512 MB, peak working set 455 MB, private now 412 MB, allocated in all 17817 MB; job peak 435 MB

## 3. Word and Excel (load in-process, against load in the engine plus the model sent back as JSON)
| file | in-process | engine | JSON KB |
|---|---|---|---|
| discharge.docx | 24.4 ms | 20.7 ms | 9 |
| landscape.docx | 22.8 ms | 23.2 ms | 1 |
| layout.xlsx | 0.9 ms | 1.3 ms | 20 |
| realistic.xlsx | 4.5 ms | 9.4 ms | 585 |

## 4. Failure: the engine dies or runs away; the app carries on
- stack overflow (uncatchable): engine ended (exit code -1), noticed after 38.0 ms; new engine, file reopened and page 1 drawn again in 3516.0 ms (631 KB). App still running: yes.
- endless loop: engine passed its time limit (10 s) and was ended, noticed after 10016.0 ms; new engine, file reopened and page 1 drawn again in 3387.4 ms (631 KB). App still running: yes.
- allocates without end: engine refused and stayed up: OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown., noticed after 77.3 ms; new engine, file reopened and page 1 drawn again in 3407.9 ms (631 KB). App still running: yes.

## 5. Probe files (each in its own engine; the app must stay up)
- bomb-95kb-empty-p-11m.docx: refused: DocumentViewException: This Word document is too large to open: its contents unpack to more than Chuvadi allows. It may be damaged. after 67.4 ms. App still running: yes.
- c08_softmask_cycle.pdf: engine ended (exit code -1) after 126.2 ms. App still running: yes.
- c09_type3_self.pdf: engine ended (exit code -1) after 126.6 ms. App still running: yes.
- c23_cff_subr_self_call.pdf: engine ended (exit code -1) after 161.9 ms. App still running: yes.
- hyperlinks-248.docx: refused: DocumentViewException: This Word document is nested more deeply than Chuvadi allows, so it isn't opened. It may be damaged, or made to attack the reader. after 125.6 ms. App still running: yes.
- ins-248.docx: refused: DocumentViewException: This Word document is nested more deeply than Chuvadi allows, so it isn't opened. It may be damaged, or made to attack the reader. after 113.3 ms. App still running: yes.
- sec-s1-nested-groups.xlsx: engine ended (exit code -1) after 4937.7 ms. App still running: yes.
- sec01-6m-cells.xlsx: refused: OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown. after 2131.6 ms. App still running: yes.
- sec08-nested-brackets.xlsx: loaded after 150.4 ms. App still running: yes.
- sec10-wildcard.xlsx: loaded after 145.9 ms. App still running: yes.
