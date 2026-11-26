param([string]$SdkPath,[string]$Ffmpeg,[switch]$CompileOnly)
$ErrorActionPreference='Stop'
$src=Split-Path -Parent $PSScriptRoot
$bin=Join-Path ([IO.Path]::GetTempPath()) ('Hub-smart-'+[Guid]::NewGuid().ToString('N'))
& (Join-Path $src 'build.ps1') -SdkPath $SdkPath -Destination $bin
$files=Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object FullName
$tester=Join-Path $bin 'SmartHomeTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
& $csc /nologo /target:exe /platform:x64 /codepage:65001 /main:SmartHomeTests "/out:$tester" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Net.Http.dll "/reference:$wpf/UIAutomationClient.dll" "/reference:$wpf/UIAutomationTypes.dll" "/reference:$wpf/WindowsBase.dll" "/reference:$bin/Microsoft.Web.WebView2.Core.dll" "/reference:$bin/Microsoft.Web.WebView2.WinForms.dll" $files (Join-Path $PSScriptRoot 'SmartHomeTests.cs')
if($LASTEXITCODE -ne 0){throw 'Smart-home tests failed to compile'}
if($CompileOnly){Write-Output $tester;return}
& $tester $Ffmpeg
if($LASTEXITCODE -ne 0){throw 'Smart-home tests failed'}

