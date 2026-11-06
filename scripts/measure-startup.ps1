<#
  Startup timing for the Windows app (dev tool, never shipped).

  Publishes ChuvadiReader.Windows in Release (optionally ReadyToRun), then launches it -Runs times
  and records, per run, the time from process start until its main window exists and is visible.
  Close is automatic. Run it on the branch and on main to compare.

    pwsh scripts/measure-startup.ps1                  # JIT build, 10 runs
    pwsh scripts/measure-startup.ps1 -ReadyToRun      # ReadyToRun build
    pwsh scripts/measure-startup.ps1 -Runs 20

  The Core/Ui part (services + first render of the real App, no WebView) is measured cross-platform
  by tools/StartupProbe; this script adds the Windows-only part (WPF window + WebView2 host).
#>
param(
    [int]$Runs = 10,
    [switch]$ReadyToRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $env:TEMP ("chuvadi-startup-" + ($(if ($ReadyToRun) { 'r2r' } else { 'jit' })))
$r2r = if ($ReadyToRun) { 'true' } else { 'false' }

Write-Host "Publishing to $out (ReadyToRun=$r2r)…"
dotnet publish (Join-Path $root 'src/ChuvadiReader.Windows/ChuvadiReader.Windows.csproj') `
    -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=$r2r -o $out --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

$exe = Join-Path $out 'ChuvadiReader.Windows.exe'
$times = @()
for ($i = 1; $i -le $Runs; $i++) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $exe -PassThru
    while ($p.MainWindowHandle -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 30) {
        Start-Sleep -Milliseconds 5
        $p.Refresh()
    }
    $ms = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
    $times += $ms
    Write-Host ("run {0,2}: window shown after {1} ms" -f $i, $ms)
    Start-Sleep -Milliseconds 1500   # let the WebView finish painting before closing
    $null = $p.CloseMainWindow()
    if (-not $p.WaitForExit(5000)) { $p.Kill() }
    Start-Sleep -Milliseconds 500
}

$sorted = $times | Sort-Object
$median = $sorted[[int][math]::Floor($sorted.Count / 2)]
Write-Host ("median {0} ms · min {1} ms · max {2} ms ({3} runs, ReadyToRun={4})" -f $median, $sorted[0], $sorted[-1], $Runs, $r2r)
