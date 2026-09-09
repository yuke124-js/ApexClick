param(
  [Parameter(Mandatory = $true)][string]$InputDir,
  [Parameter(Mandatory = $true)][string]$OutputDir,
  [string]$AssemblyName = "ApexClick.Launcher.dll"
)

$ErrorActionPreference = "Stop"
$in = [System.IO.Path]::GetFullPath($InputDir)
$out = [System.IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $out | Out-Null
$assembly = Join-Path $in $AssemblyName
if (-not (Test-Path $assembly)) { throw "Release assembly not found: $assembly" }

$config = Join-Path $env:TEMP ("ApexClick.Obfuscar." + [Guid]::NewGuid().ToString("N") + ".xml")
@"
<?xml version="1.0" encoding="utf-8"?>
<Obfuscator>
  <Var name="InPath" value="$in" />
  <Var name="OutPath" value="$out" />
  <Var name="KeepPublicApi" value="true" />
  <Var name="RenamePrivateOnly" value="true" />
  <Var name="HidePrivateApi" value="true" />
  <Module file="$assembly" />
</Obfuscator>
"@ | Set-Content -LiteralPath $config -Encoding UTF8

try {
  dotnet tool restore
  dotnet tool run obfuscar.console $config
} finally {
  Remove-Item -LiteralPath $config -Force -ErrorAction SilentlyContinue
}
