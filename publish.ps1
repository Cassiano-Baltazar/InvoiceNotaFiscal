$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $projectDir "src\InvoiceApp.Web\InvoiceApp.Web.csproj"
$outputDir = Join-Path $projectDir "dist"

Write-Host "Publishing Invoice App..." -ForegroundColor Cyan

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    -o $outputDir

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "Executable: $outputDir\InvoiceApp.exe"
Write-Host "URL: http://localhost:5099"
Write-Host ""
Write-Host "Double-click InvoiceApp.exe to start the app and open the browser."
