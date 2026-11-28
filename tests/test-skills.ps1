param([string]$Destination=(Join-Path ([IO.Path]::GetTempPath()) ('Hub-skills-'+[Guid]::NewGuid().ToString('N'))),[switch]$CompileOnly)
$ErrorActionPreference='Stop'
$src=Split-Path -Parent $PSScriptRoot
$bin=New-Item -ItemType Directory -Force -Path $Destination
$tester=Join-Path $bin.FullName 'ProjectSkillTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
$files=@('SmartSettings.cs','SkillWorkspaces.cs','SkillCatalog.cs','DesktopSkillRunner.cs','GitHubProjects.cs','ProjectSharing.cs','ProjectExecution.cs','BrowserAccounts.cs','ProjectLauncher.cs','MacroSettings.cs','DeveloperGitHub.cs','DeveloperPreferences.cs','DeveloperVerification.cs','AiAccounts.cs','CodexModelComparison.cs','CodexLocal.cs','CodexActions.cs','GpuTelemetry.cs','TemperatureSensors.cs') | ForEach-Object {Join-Path $src $_}
$fixtures=@('ProjectSkillTests.cs','CloudProjectTests.cs','BrowserAccountTests.cs','DeveloperConsoleTests.cs','HardwareAiTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $csc /nologo /target:exe /platform:x64 /codepage:65001 "/out:$tester" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Web.dll /reference:System.Security.dll /reference:System.Management.dll /reference:System.Net.Http.dll "/reference:$wpf/UIAutomationClient.dll" "/reference:$wpf/UIAutomationTypes.dll" "/reference:$wpf/WindowsBase.dll" $files $fixtures
if($LASTEXITCODE -ne 0){throw 'Skill tests failed to compile'}
if($CompileOnly){Write-Output $tester;return}
& $tester (Join-Path $bin.FullName 'fixtures')
if($LASTEXITCODE -ne 0){throw 'Skill tests failed'}

