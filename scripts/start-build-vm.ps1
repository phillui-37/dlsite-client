#Requires -Version 7.0
param([string]$VmPath = $env:DLSITE_BUILD_VM)
$ErrorActionPreference = 'Stop'
if (-not $VmPath -or -not (Test-Path -LiteralPath $VmPath)) { throw 'Set DLSITE_BUILD_VM to the prepared build VM configuration.' }
$vmrun = Join-Path ${env:ProgramFiles} 'VMware/VMware Workstation/vmrun.exe'
$running = & $vmrun list
if ($LASTEXITCODE -ne 0) { throw 'Cannot read VMware state.' }
if ($running -notcontains $VmPath) {
    & $vmrun start $VmPath nogui
    if ($LASTEXITCODE -ne 0) { throw 'Cannot start build VM.' }
}
$deadline = [DateTime]::UtcNow.AddMinutes(3)
while ([DateTime]::UtcNow -lt $deadline) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'dlsite-steamos-agent', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(2000)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
        $writer.NewLine = "`n"
        $writer.AutoFlush = $true
        $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 4096, $true)
        $marker = 'RUNNER_START_' + [Guid]::NewGuid().ToString('N')
        $writer.WriteLine('nohup bash /mnt/dlsite-test/start-actions-runner.sh >/mnt/dlsite-test/runner-start.log 2>&1 </dev/null & printf "' + $marker + '\n"')
        $attemptDeadline = [DateTime]::UtcNow.AddSeconds(8)
        while ([DateTime]::UtcNow -lt $attemptDeadline) {
            $line = $reader.ReadLineAsync()
            if (-not $line.Wait(2000)) { break }
            if ($line.Result -eq $marker) { Write-Output 'Linux build runner started.'; return }
            if ($null -eq $line.Result) { break }
        }
    } catch [TimeoutException] {
        # The VM command agent is still starting.
    } finally {
        $pipe.Dispose()
    }
    Start-Sleep -Seconds 3
}
throw 'The VM command agent did not respond within three minutes.'
