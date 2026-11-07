param([string]$Version='6.7.3')
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path -Parent $PSScriptRoot
$toolDirectory=Join-Path $repositoryRoot 'artifacts\tooling'
New-Item -ItemType Directory -Force $toolDirectory | Out-Null
$tag='is-'+$Version.Replace('.','_')
$release=Invoke-RestMethod -Uri "https://api.github.com/repos/jrsoftware/issrc/releases/tags/$tag"
$asset=$release.assets | Where-Object name -eq "innosetup-$Version.exe"
if (-not $asset) { $asset=$release.assets | Where-Object name -eq "innosetup-$Version-x64.exe" }
if (-not $asset) { throw 'Official compiler asset not found' }
$download=Join-Path $toolDirectory $asset.name
if (-not (Test-Path -LiteralPath $download)) { Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $download }
$actual=(Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
if ($asset.digest -and $asset.digest -ne "sha256:$actual") { throw 'Compiler digest mismatch' }
$signature=Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid') { throw "Compiler signature is not valid: $($signature.Status)" }
$portableDirectory=Join-Path $toolDirectory 'inno'
$arguments=@('/PORTABLE=1','/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/LOG="'+(Join-Path $toolDirectory 'inno-extract.log')+'"'),('/DIR="'+$portableDirectory+'"'))
$setupProcess=Start-Process -FilePath $download -ArgumentList $arguments -PassThru -WindowStyle Hidden
$setupProcess.WaitForExit()
if ($setupProcess.ExitCode -ne 0) { throw "Portable compiler extraction failed: $($setupProcess.ExitCode)" }
[pscustomobject]@{ Compiler=(Join-Path $portableDirectory 'ISCC.exe'); SHA256=$actual; Signer=$signature.SignerCertificate.Subject } | ConvertTo-Json
