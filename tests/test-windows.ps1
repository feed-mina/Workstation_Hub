param([string]$SdkPath, [switch]$CompileOnly)
$ErrorActionPreference='Stop'
$src=Split-Path -Parent $PSScriptRoot
$scratch=New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('WorkstationHub-test-'+[Guid]::NewGuid().ToString('N')))
$bin=New-Item -ItemType Directory -Path (Join-Path $scratch.FullName 'bin')
$profile=New-Item -ItemType Directory -Path (Join-Path $scratch.FullName 'profile')
$screens=New-Item -ItemType Directory -Path (Join-Path $scratch.FullName 'screens')
& (Join-Path $src 'build.ps1') -SdkPath $SdkPath -Destination $bin.FullName
$base=$bin.FullName
$tester=Join-Path $base 'DesktopSmoke.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$files=Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object FullName
$wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /main:DesktopSmoke "/out:$tester" "/win32manifest:$src/app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Net.Http.dll "/reference:$wpf/UIAutomationClient.dll" "/reference:$wpf/UIAutomationTypes.dll" "/reference:$wpf/WindowsBase.dll" "/reference:$base/Microsoft.Web.WebView2.Core.dll" "/reference:$base/Microsoft.Web.WebView2.WinForms.dll" $files (Join-Path $PSScriptRoot 'DesktopSmoke.cs')
if ($LASTEXITCODE -ne 0) {throw 'Test build failed'}
if ($CompileOnly) {Write-Output $tester;return}
foreach ($phase in @('initial','reopen')) {
    $report=Join-Path $scratch.FullName ($phase+'.txt')
    $arguments=@(('"'+$profile.FullName+'"'),('"'+$report+'"'),('"'+$screens.FullName+'"'))
    if ($phase -eq 'reopen') {$arguments+='reopen'}
    $process=Start-Process -FilePath $tester -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if (Test-Path -LiteralPath $report) {Get-Content -Encoding UTF8 -LiteralPath $report}
    if ($process.ExitCode -ne 0) {throw "Windows test failed. See $report"}
}
Write-Output $scratch.FullName

