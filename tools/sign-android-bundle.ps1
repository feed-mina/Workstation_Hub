param(
    [Parameter(Mandatory)][string]$Bundle,
    [Parameter(Mandatory)][string]$Keystore,
    [Parameter(Mandatory)][string]$PasswordFile,
    [Parameter(Mandatory)][string]$JdkBin,
    [Parameter(Mandatory)][string]$Output,
    [string]$Alias = 'upload'
)
$ErrorActionPreference = 'Stop'
if ((Resolve-Path -LiteralPath $Bundle).Path -eq [IO.Path]::GetFullPath($Output)) { throw 'Choose a separate signed output; the original bundle is preserved.' }
if (Test-Path -LiteralPath $Output) { throw 'The signed output already exists; choose a new name.' }
$secure = (Get-Content -LiteralPath $PasswordFile -Raw).Trim() | ConvertTo-SecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$previousPassword = $env:HUB_UPLOAD_STORE_PASSWORD
try {
    $env:HUB_UPLOAD_STORE_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    $signArgs = @('-J-Duser.language=en', '-keystore', $Keystore, '-storetype', 'PKCS12', '-storepass:env', 'HUB_UPLOAD_STORE_PASSWORD', '-keypass:env', 'HUB_UPLOAD_STORE_PASSWORD', '-sigalg', 'SHA256withRSA', '-digestalg', 'SHA-256', '-signedjar', $Output, $Bundle, $Alias)
    & (Join-Path $JdkBin 'jarsigner.exe') @signArgs
    if ($LASTEXITCODE -ne 0) { throw 'Bundle signing failed.' }
    & (Join-Path $JdkBin 'jarsigner.exe') @('-J-Duser.language=en', '-verify', $Output)
    if ($LASTEXITCODE -ne 0) { throw 'Bundle signature verification failed.' }
} finally {
    $env:HUB_UPLOAD_STORE_PASSWORD = $previousPassword
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
}
Get-FileHash -LiteralPath $Output -Algorithm SHA256
