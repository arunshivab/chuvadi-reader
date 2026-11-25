# Parse-process prototype ΓÇö Microsoft Windows 10.0.26200, X64, 12 cores, .NET 10.0.15
engine: heap cap 512 MB (DOTNET_GCHeapHardLimit), time limit 10 s per request

## Stack sizes
app process: main 1536 KB, thread pool 1536 KB, new Thread() 1536 KB
engine process: main 1536 KB, thread pool 1536 KB, new Thread() 1536 KB

## 1. Starting the engine (process start to first answer)
first 152.7 ms; then median 51.2 ms (min 49.3, max 52.8); engine memory idle 23 MB
first document, open and page 1 (a real A1 CAD drawing (not in repo)): today, in a running app 3288.7 ms; with an engine started for it (start included) 3385.4 ms; with an engine already running, see table 2
wire, a payload sent and returned (twice the one-way cost): 1 KB 0.1 ms, 100 KB 0.2 ms, 1000 KB 1.3 ms, 6000 KB 7.1 ms

## 2. PDF pages (open, then the first 8 pages), warm: one file drawn first on both sides
| file | pages | SVG KB/page | open in-process | open engine | page in-process (median) | page engine (median) | overhead/page |
|---|---|---|---|---|---|---|---|
| a real A1 CAD drawing (not in repo) | 5 | 3272 | 6.6 ms | 6.9 ms | 3507.4 ms | 3321.7 ms | -185.7 ms |
| inherited-cropbox.pdf | 1 | 0 | 0.2 ms | 0.3 ms | 0.3 ms | 0.4 ms | 0.1 ms |
| journal-10p.pdf | 10 | 161 | 1.9 ms | 1.6 ms | 16.0 ms | 16.0 ms | -0.0 ms |
engine memory after drawing: 71 MB

## 3. Word and Excel (load in-process, against load in the engine plus the model sent back as JSON)
| file | in-process | engine | JSON KB |
|---|---|---|---|
| discharge.docx | 20.5 ms | 20.0 ms | 9 |
| landscape.docx | 21.6 ms | 20.2 ms | 1 |
| layout.xlsx | 0.8 ms | 1.1 ms | 20 |
| realistic.xlsx | 3.8 ms | 9.1 ms | 585 |

## 4. Failure: the engine dies or runs away; the app carries on
- stack overflow (uncatchable): engine ended (exit code -1073741571), noticed after 7447.7 ms; new engine, file reopened and page 1 drawn again in 3331.8 ms (2518 KB). App still running: yes.
- endless loop: engine passed its time limit (10 s) and was ended, noticed after 10026.3 ms; new engine, file reopened and page 1 drawn again in 3353.3 ms (2518 KB). App still running: yes.
- allocates without end: engine refused and stayed up: OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown., noticed after 60.4 ms; new engine, file reopened and page 1 drawn again in 3374.0 ms (2518 KB). App still running: yes.

## 5. Probe files (each in its own engine; the app must stay up)
- bomb-95kb-empty-p-11m.docx: refused: DocumentViewException: This Word document is too large to open: its contents unpack to more than Chuvadi allows. It may be damaged. after 67.3 ms. App still running: yes.
- c08_softmask_cycle.pdf: engine ended (exit code -1073741571) after 6347.5 ms. App still running: yes.
- c09_type3_self.pdf: engine ended (exit code -1073741571) after 5687.8 ms. App still running: yes.
- c23_cff_subr_self_call.pdf: engine ended (exit code -1073741571) after 5993.8 ms. App still running: yes.
- hyperlinks-248.docx: refused: DocumentViewException: This Word document is too large to open: its contents unpack to more than Chuvadi allows. It may be damaged. after 105.3 ms. App still running: yes.
- ins-248.docx: refused: DocumentViewException: This Word document is too large to open: its contents unpack to more than Chuvadi allows. It may be damaged. after 107.9 ms. App still running: yes.
- sec-s1-nested-groups.xlsx: engine passed its time limit (10 s) and was ended after 10039.8 ms. App still running: yes.
- sec01-6m-cells.xlsx: refused: OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown. after 2141.3 ms. App still running: yes.
- sec08-nested-brackets.xlsx: loaded after 148.1 ms. App still running: yes.
- sec10-wildcard.xlsx: loaded after 147.1 ms. App still running: yes.
