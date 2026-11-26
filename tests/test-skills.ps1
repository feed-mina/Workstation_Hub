param([string]$Destination=(Join-Path ([IO.Path]::GetTempPath()) ('Hub-skills-'+[Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference='Stop'
$src=Split-Path -Parent $PSScriptRoot
$bin=New-Item -ItemType Directory -Force -Path $Destination
$tester=Join-Path $bin.FullName 'ProjectSkillTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
$files=@('SmartSettings.cs','SkillWorkspaces.cs','SkillCatalog.cs','DesktopSkillRunner.cs') | ForEach-Object {Join-Path $src $_}
& $csc /nologo /target:exe /platform:x64 /codepage:65001 "/out:$tester" /reference:System.Web.Extensions.dll /reference:System.Web.dll /reference:System.Security.dll "/reference:$wpf/UIAutomationClient.dll" "/reference:$wpf/UIAutomationTypes.dll" "/reference:$wpf/WindowsBase.dll" $files (Join-Path $PSScriptRoot 'ProjectSkillTests.cs')
if($LASTEXITCODE -ne 0){throw 'Skill tests failed to compile'}
& $tester (Join-Path $bin.FullName 'fixtures')
if($LASTEXITCODE -ne 0){throw 'Skill tests failed'}

