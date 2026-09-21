
$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "      LANChat Installer (Windows)         " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan


if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[+] .NET SDK не найден. Загрузка установщика..." -ForegroundColor Yellow
    $installScript = "$env:TEMP\dotnet-install.ps1"
    
    Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1" -OutFile $installScript
    & $installScript -Channel 10.0
    Remove-Item $installScript -Force

и
    $dotnetPath = "$env:USERPROFILE\.dotnet"
    $env:DOTNET_ROOT = $dotnetPath
    $env:PATH = "$env:PATH;$dotnetPath"
    
    Write-Host "[✓] .NET SDK успешно установлен." -ForegroundColor Green
} else {
    $dotnetVer = dotnet --version
    Write-Host "[✓] .NET SDK уже установлен (версия: $dotnetVer)." -ForegroundColor Green
}

Write-Host "[+] Сборка проекта LANChat..." -ForegroundColor Cyan

if (Test-Path "LANChat.slnx") {
    dotnet build LANChat.slnx -c Release
} elseif (Test-Path "LANChat.sln") {
    dotnet build LANChat.sln -c Release
} else {
    dotnet build -c Release
}

Write-Host "==========================================" -ForegroundColor Green
Write-Host "   Установка и сборка успешно завершены!  " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host "Для запуска сервера: dotnet run --project LANChat.Server"
Write-Host "Для запуска клиента: dotnet run --project LANChat.Client"
