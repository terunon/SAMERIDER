param(
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$InstallRoot = Split-Path -Parent $ProjectRoot
$OutputDirectory = Join-Path $ProjectRoot ".publish/$RuntimeIdentifier"
dotnet publish (Join-Path $ProjectRoot "src/SAMERIDER.App/SAMERIDER.App.csproj") `
    -c Release -r $RuntimeIdentifier --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item (Join-Path $OutputDirectory "SAMERIDER.exe") (Join-Path $InstallRoot "SAMERIDER.exe") -Force
Remove-Item (Join-Path $ProjectRoot ".publish") -Recurse -Force
