param([string]$Destination=(Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference='Stop'
$version='2.105.0'
$expected='8813a72c899900afaa10e3fec8fbc64b908c89dbfe837e95ed2fa500fc140627'
$cache=Join-Path $PSScriptRoot '.build/github-cli'
$archive=Join-Path $cache "gh_$($version)_windows_amd64.zip"
$expanded=Join-Path $cache $version
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if(-not (Test-Path -LiteralPath $archive)) {Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/cli/cli/releases/download/v$version/gh_$($version)_windows_amd64.zip" -OutFile $archive}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'GitHub CLI checksum mismatch'}
if(-not (Test-Path -LiteralPath (Join-Path $expanded 'bin/gh.exe'))){Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force}
$target=Join-Path $Destination 'tools/github'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $expanded 'bin') -Destination $target -Recurse -Force
foreach($name in @('LICENSE','share')){if(Test-Path -LiteralPath (Join-Path $expanded $name)){Copy-Item -LiteralPath (Join-Path $expanded $name) -Destination $target -Recurse -Force}}
Write-Output "Bundled official GitHub CLI $version (verified SHA-256)."
