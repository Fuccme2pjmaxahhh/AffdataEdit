param(
    [string]$UnityPath,
    [string]$BuildWorkspace
)

$ErrorActionPreference = 'Stop'
function Get-BuildSha256([string]$Path) {
    $taskHashStream = [IO.File]::OpenRead($Path)
    $taskSha256 = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($taskSha256.ComputeHash($taskHashStream)).Replace('-', '') }
    finally { $taskSha256.Dispose(); $taskHashStream.Dispose() }
}
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskDevRoot = Split-Path $taskProjectRoot -Parent
$taskDestination = 'C:\AffdataEdit_custom'
if (-not $BuildWorkspace) { $BuildWorkspace = Join-Path $taskDevRoot 'BuildWorkspace' }
$BuildWorkspace = [IO.Path]::GetFullPath($BuildWorkspace)
if (-not $UnityPath) {
    $versionLine = Select-String -LiteralPath (Join-Path $taskProjectRoot 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$'
    $UnityPath = Join-Path $taskDevRoot ('Unity\' + $versionLine.Matches[0].Groups[1].Value.Trim() + '\Editor\Unity.exe')
}
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity not found: $UnityPath" }
if ($BuildWorkspace -eq $taskProjectRoot -or $BuildWorkspace.StartsWith($taskProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'BuildWorkspace must be separate from the source project.'
}

# One persistent build project keeps its Library, shader and player caches.
$taskMutex = New-Object Threading.Mutex($false, 'Local\AffdataEdit-WindowsBuild')
if (-not $taskMutex.WaitOne(0)) { $taskMutex.Dispose(); throw 'An AffdataEdit build is already running.' }
$taskTimer = [Diagnostics.Stopwatch]::StartNew()
try {
    $busy = Get-CimInstance Win32_Process -Filter "name='Unity.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.IndexOf($BuildWorkspace, [StringComparison]::OrdinalIgnoreCase) -ge 0
    }
    if ($busy) { throw 'The build workspace is open in Unity. Close that editor first.' }
    foreach ($path in @($BuildWorkspace, $taskDestination)) {
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing a linked destination: $path"
        }
    }
    New-Item -ItemType Directory -Path $BuildWorkspace -Force | Out-Null
    $taskLogFolder = Join-Path $taskDevRoot 'BuildLogs'
    New-Item -ItemType Directory -Path $taskLogFolder -Force | Out-Null
    $taskLog = Join-Path $taskLogFolder ('Windows_' + (Get-Date -Format 'yyyy-MM-dd_HH-mm-ss') + '.log')

    Write-Host '1/3 Syncing source changes into the cached build workspace...'
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        $source = Join-Path $taskProjectRoot $folder
        $target = [IO.Path]::GetFullPath((Join-Path $BuildWorkspace $folder))
        # /MIR may remove obsolete cached sources: constrain it to these owned folders.
        if (-not $target.StartsWith($BuildWorkspace + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe sync target: $target"
        }
        if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing a linked sync target: $target"
        }
        & robocopy $source $target /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { throw "Source sync failed: $folder (robocopy $LASTEXITCODE)" }
    }

    $taskGitFolder = Join-Path $taskDevRoot 'Git\cmd'
    if (Test-Path -LiteralPath $taskGitFolder) { $env:PATH = $taskGitFolder + ';' + $env:PATH }
    $taskTemp = Join-Path $taskDevRoot 'Temp'
    New-Item -ItemType Directory -Path $taskTemp -Force | Out-Null
    $env:TEMP = $taskTemp
    $env:TMP = $taskTemp
    Write-Host '2/3 Building Windows player (Unity reuses its incremental caches)...'
    Write-Host "Build log: $taskLog"
    # Player builds do not need an editor graphics device. Headless mode avoids
    # native crashes while loading editor GUI materials/resources on this machine.
    $taskArguments = '-batchmode -nographics -quit -job-worker-count 2 -desiredWorkerCount 1 -projectPath "' + $BuildWorkspace +
        '" -executeMethod WindowsCiBuild.Build -logFile "' + $taskLog + '"'
    $taskUnity = Start-Process -FilePath $UnityPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -Wait
    if ($taskUnity.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $taskLog) {
            $taskBuildErrors = @(Select-String -LiteralPath $taskLog -Pattern 'error CS\d+|warning CS\d+|Shader error|Shader warning|Fatal Error!|BuildFailedException:' | Select-Object -First 8)
            if ($taskBuildErrors.Count -gt 0) {
                Write-Host 'Unity reported these build errors:' -ForegroundColor Red
                foreach ($taskBuildError in $taskBuildErrors) { Write-Host $taskBuildError.Line -ForegroundColor Red }
            }
        }
        throw "Unity build failed ($($taskUnity.ExitCode)). See $taskLog"
    }
    if (-not (Select-String -LiteralPath $taskLog -SimpleMatch 'Build Finished, Result: Success.' -Quiet)) {
        throw "Unity did not report a successful build. See $taskLog"
    }
    $taskPlayer = Join-Path $BuildWorkspace 'Build\Windows'
    foreach ($file in @('AffdataEdit.exe', 'UnityPlayer.dll', 'AffdataEdit_Data\Managed\Arcade.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $taskPlayer $file) -PathType Leaf)) { throw "Missing build output: $file" }
    }

    Write-Host '3/3 Updating C:\AffdataEdit_custom (settings and user files are preserved)...'
    $taskExe = Join-Path $taskDestination 'AffdataEdit.exe'
    # User authorization (2026-11-08): close only the custom build without saving.
    $taskRunning = @(Get-CimInstance Win32_Process -Filter "name='AffdataEdit.exe'" | Where-Object { $_.ExecutablePath -eq $taskExe })
    foreach ($taskProcess in $taskRunning) {
        Write-Host 'Closing the authorized custom AE instance without saving...'
        Stop-Process -Id $taskProcess.ProcessId -Force -ErrorAction Stop
        Wait-Process -Id $taskProcess.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
    }
    # No /MIR here: never delete user settings, charts or backgrounds in the installed app.
    & robocopy $taskPlayer $taskDestination /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "Publishing failed (robocopy $LASTEXITCODE). See $taskLog" }
    $sourceHash = Get-BuildSha256 (Join-Path $taskPlayer 'AffdataEdit_Data\Managed\Arcade.dll')
    $targetHash = Get-BuildSha256 (Join-Path $taskDestination 'AffdataEdit_Data\Managed\Arcade.dll')
    if ($sourceHash -ne $targetHash) { throw 'Published assembly verification failed.' }
    # Retire only recognized generated files absent from the new player. Keep a
    # recoverable copy outside the installation; never mirror the installation.
    $taskRetiredRoot = Join-Path $taskDevRoot ('RetiredPlayerFiles\' + (Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'))
    $taskRetiredCount = 0
    function Save-RetiredPlayerFile([string]$File) {
        $taskFullFile = [IO.Path]::GetFullPath($File)
        if (-not $taskFullFile.StartsWith($taskDestination + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe retirement source: $taskFullFile"
        }
        if ((Get-Item -LiteralPath $taskFullFile).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing a linked player file: $taskFullFile"
        }
        $taskRelative = $taskFullFile.Substring($taskDestination.Length + 1)
        $taskBackup = [IO.Path]::GetFullPath((Join-Path $taskRetiredRoot $taskRelative))
        if (-not $taskBackup.StartsWith($taskRetiredRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe retirement destination: $taskBackup"
        }
        New-Item -ItemType Directory -Path (Split-Path $taskBackup -Parent) -Force | Out-Null
        Move-Item -LiteralPath $taskFullFile -Destination $taskBackup
        $script:taskRetiredCount++
    }
    $taskData = Join-Path $taskDestination 'AffdataEdit_Data'
    foreach ($taskOldFile in Get-ChildItem -LiteralPath $taskData -File) {
        if ($taskOldFile.Name -match '^(?:globalgamemanagers(?:\.assets)?|resources\.assets|sharedassets\d+\.assets|level\d+)(?:\.resS|\.resource)?$|^data\.unity3d$') {
            if (-not (Test-Path -LiteralPath (Join-Path $taskPlayer ('AffdataEdit_Data\' + $taskOldFile.Name)))) {
                Save-RetiredPlayerFile $taskOldFile.FullName
            }
        }
    }
    if ($taskRetiredCount -gt 0) { Write-Host "Archived $taskRetiredCount obsolete bundled files: $taskRetiredRoot" }
    Write-Host ('Build complete in {0:N1}s: {1}' -f $taskTimer.Elapsed.TotalSeconds, $taskExe)
}
finally {
    $taskMutex.ReleaseMutex()
    $taskMutex.Dispose()
}
