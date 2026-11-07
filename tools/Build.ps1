param([switch]$Installer)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    New-Item -ItemType Directory -Force artifacts/reports | Out-Null
    dotnet build KeySwitch.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet test tests/KeySwitch.Tests -c Release --no-build --nologo --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/reports
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    dotnet run --project tools/Evaluate -c Release --no-build > artifacts/reports/evaluate-known.json
    if ($LASTEXITCODE -ne 0) { throw 'In-vocabulary evaluation failed' }
    dotnet run --project tools/Evaluate -c Release --no-build -- --holdout > artifacts/reports/evaluate-holdout.json
    if ($LASTEXITCODE -ne 0) { throw 'Holdout evaluation failed' }
    dotnet publish src/KeySwitch.App -c Release -r win-x64 --self-contained true -o artifacts/publish -p:DebugType=None --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $smokeProcess=Start-Process -FilePath (Join-Path $repositoryRoot 'artifacts/publish/KeySwitch.exe') -ArgumentList '--smoke','--settings' -PassThru -WindowStyle Hidden
    if (-not $smokeProcess.WaitForExit(15000)) { throw 'Smoke timed out' }
    if ($smokeProcess.ExitCode -ne 0) { throw "Smoke failed: $($smokeProcess.ExitCode)" }
    $localCompiler=Join-Path $repositoryRoot 'artifacts/tooling/inno/ISCC.exe'
    $installedCompiler=Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $localCompiler) { $compiler=$localCompiler }
    elseif ($installedCompiler) { $compiler=$installedCompiler.Source }
    else { $compiler=$null }
    if ($compiler) { & $compiler installer/KeySwitch.iss; if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' } }
    elseif ($Installer) { throw 'Install Inno Setup or run tools/Get-InnoSetup.ps1 first' }
    else { Write-Output 'Published application; installer compiler not present.' }
} finally { Pop-Location }
