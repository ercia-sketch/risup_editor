param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$editorRoot = $PSScriptRoot
$env:DOTNET_CLI_HOME = "$editorRoot\tools\cli"
$env:NUGET_PACKAGES = "$editorRoot\tools\nuget"
$env:TEMP = "$editorRoot\tools\temp"
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
$dotnet = "$editorRoot\tools\dotnet\dotnet.exe"
if ($Publish) { & $dotnet publish "$editorRoot\src\RisupEditor.csproj" -c Release -o "$editorRoot\artifacts\release" } else { & $dotnet build "$editorRoot\src\RisupEditor.csproj" -c Release }
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

