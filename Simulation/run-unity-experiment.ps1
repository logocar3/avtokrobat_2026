# Run experiment files on the Unity engine (UDIRC 1610S + VESC + 1 kHz controller), headless.
# No Docker, no Play mode. Results: output\unity_experiments\<name>_<yyyyMMdd-HHmmss>\ (a new folder per run)
#
# Usage (from the Simulation folder):
#   .\run-unity-experiment.ps1 experiments\07_udirc_jump_speed.json
#   .\run-unity-experiment.ps1 experiments                      # every .json in the folder
#   .\run-unity-experiment.ps1 my_tests\a.json my_tests\b.json
#   .\run-unity-experiment.ps1 experiments -UnityExe "D:\Unity\6000.5.2f1\Editor\Unity.exe"
#
# The Unity editor may stay open: the project is mirrored to Unity\_runner\ (incrementally, so
# later runs start faster) and the copy is run in batch mode. The first run imports the
# project, which takes a minute or two.
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)][string[]]$Experiments,
    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.5.2f1\Editor\Unity.exe"
)
$ErrorActionPreference = "Stop"
$sim = $PSScriptRoot
$project = Join-Path $sim "Unity\AvtokrobatViz"
$runner = Join-Path $sim "Unity\_runner"          # must sit next to AvtokrobatViz (config paths are relative)
$outRoot = Join-Path $sim "output\unity_experiments"

if (-not $Experiments -or $Experiments.Count -eq 0) {
    Write-Host "usage: .\run-unity-experiment.ps1 <experiment.json | folder> ..." ; exit 2
}
if (-not (Test-Path $UnityExe)) {
    Write-Host "Unity not found at $UnityExe - pass -UnityExe <path to Unity.exe> (Unity 6000.5.x)" ; exit 2
}

# absolute paths, so the batch process finds them regardless of its working directory
$paths = @()
foreach ($e in $Experiments) {
    $p = if ([System.IO.Path]::IsPathRooted($e)) { $e } else { Join-Path (Get-Location) $e }
    if (-not (Test-Path $p)) { Write-Host "not found: $e" ; exit 2 }
    $paths += (Resolve-Path $p).Path
}

Write-Host "[unity] syncing project copy -> $runner"
foreach ($d in "Assets", "ProjectSettings", "Packages", "Library") {
    if (Test-Path (Join-Path $project $d)) {
        robocopy (Join-Path $project $d) (Join-Path $runner $d) /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) { Write-Host "robocopy failed for $d (code $LASTEXITCODE)" ; exit 1 }
    }
}

New-Item -ItemType Directory -Force $outRoot | Out-Null
$log = Join-Path $outRoot "unity_batch.log"
Write-Host "[unity] running $($paths.Count) path(s) in batch mode (log: $log)"
$unityArgs = @("-batchmode", "-nographics", "-quit", "-projectPath", $runner, "-logFile", $log,
          "-executeMethod", "Avtokrobat.ExperimentCli.Run", "-experiments") + $paths
$proc = Start-Process -FilePath $UnityExe -ArgumentList ($unityArgs | ForEach-Object { '"' + $_ + '"' }) -Wait -PassThru -NoNewWindow

Select-String -Path $log -Pattern "^\[experiment\]|^    car check|error CS\d+" | ForEach-Object { Write-Host $_.Line }
if ($proc.ExitCode -ne 0) { Write-Host "[unity] exit code $($proc.ExitCode) - see $log" }
exit $proc.ExitCode
