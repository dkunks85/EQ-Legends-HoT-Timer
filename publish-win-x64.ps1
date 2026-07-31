$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\EQSpellTimer\EQSpellTimer.csproj'
$output = Join-Path $PSScriptRoot 'publish\win-x64'
Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $output
Write-Host "Published to $output" -ForegroundColor Green
