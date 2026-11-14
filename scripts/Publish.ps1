param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $sourceRoot 'out\win-x64' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output directory already exists. Choose a new output directory to preserve previous builds.' }
Push-Location $sourceRoot
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $verification = Join-Path $OutputDirectory 'verification'
    New-Item -ItemType Directory -Path $verification | Out-Null
    $test = Start-Process -FilePath (Join-Path $OutputDirectory 'KeypadLayers.exe') -ArgumentList '--self-test',('"' + $verification + '"') -Wait -PassThru -WindowStyle Hidden
    Get-Content -LiteralPath (Join-Path $verification 'self-test.txt')
    if ($test.ExitCode -ne 0) { throw 'Self-test failed.' }
    $revision = 'working-tree'
    if ((Test-Path -LiteralPath (Join-Path $sourceRoot '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
        try { $candidate = & git rev-parse HEAD 2>$null; if ($LASTEXITCODE -eq 0) { $revision = $candidate } } catch { $revision = 'working-tree' }
    }
    [xml]$project = Get-Content -LiteralPath 'KeypadLayers.csproj'
    [ordered]@{
        project = 'keypad-layers'; version = [string]$project.Project.PropertyGroup.Version
        source_revision = $revision; build_timestamp = [DateTime]::UtcNow.ToString('o')
        source_path = $sourceRoot; build_machine = $env:COMPUTERNAME
        verification_commands = @('dotnet build -c Release', 'dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true', 'KeypadLayers.exe --self-test verification')
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-info.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'README.md') -Destination $OutputDirectory
    Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -ne 'checksums.txt' } | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'checksums.txt') -Encoding ascii
    Write-Output "Portable app: $(Join-Path $OutputDirectory 'KeypadLayers.exe')"
} finally { Pop-Location }
