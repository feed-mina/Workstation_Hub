param([string]$SdkPath,[string]$Destination=(Join-Path $PSScriptRoot "dist"))
$ErrorActionPreference='Stop'
$version='1.0.2903.40'
if (-not $SdkPath) {
    $cache=Join-Path $PSScriptRoot '.build'
    $SdkPath=Join-Path $cache "webview2-$version"
    if (-not (Test-Path -LiteralPath (Join-Path $SdkPath 'lib/net462/Microsoft.Web.WebView2.WinForms.dll'))) {
        New-Item -ItemType Directory -Force -Path $cache | Out-Null
        $package=Join-Path $cache 'webview2.zip'
        Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg" -OutFile $package
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash -ne 'EF128016DD1E51C59178C827ED5B8AA3322C57AFA8675D930F8109505542AD74') {throw 'WebView2 package hash mismatch'}
        Expand-Archive -LiteralPath $package -DestinationPath $SdkPath -Force
    }
}
$dest=New-Item -ItemType Directory -Path $Destination -Force
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$libs=Join-Path $SdkPath 'lib/net462'
$frameworkReferences=@('/reference:System.Net.Http.dll','/reference:System.Security.dll','/reference:System.Management.dll')
$wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
foreach($name in @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll')){$frameworkReferences+="/reference:$wpf/$name"}
$files=Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
$exe=Join-Path $dest.FullName 'WorkstationHub.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$exe" "/win32icon:$PSScriptRoot/chick.ico" "/win32manifest:$PSScriptRoot/app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $frameworkReferences "/reference:$libs/Microsoft.Web.WebView2.Core.dll" "/reference:$libs/Microsoft.Web.WebView2.WinForms.dll" $files
if ($LASTEXITCODE -ne 0){throw 'Compilation failed'}
foreach($f in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath (Join-Path $libs $f) -Destination $dest.FullName -Force}
Copy-Item -LiteralPath (Join-Path $SdkPath 'runtimes/win-x64/native/WebView2Loader.dll') -Destination $dest.FullName -Force
foreach($name in @('ui','licenses','chick.ico','README.md','LICENSE.md')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $dest.FullName -Recurse -Force}
& (Join-Path $PSScriptRoot 'prepare-github.ps1') -Destination $dest.FullName
Write-Output $exe

