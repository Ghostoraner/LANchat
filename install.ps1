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

    $dotnetPath = "$env:USERPROFILE\.dotnet"
    $env:DOTNET_ROOT = $dotnetPath
    $env:PATH = "$env:PATH;$dotnetPath"

    Write-Host "[✓] .NET SDK успешно установлен." -ForegroundColor Green
} else {
    $dotnetVer = dotnet --version
    Write-Host "[✓] .NET SDK уже установлен (версия: $dotnetVer)." -ForegroundColor Green
}

if (!(Get-Command cargo -ErrorAction SilentlyContinue)) {
    Write-Host "[+] Rust не найден. Загрузка rustup-init..." -ForegroundColor Yellow
    $rustupInit = "$env:TEMP\rustup-init.exe"
    Invoke-WebRequest -Uri "https://win.rustup.rs/x86_64" -OutFile $rustupInit
    & $rustupInit -y --default-toolchain stable
    Remove-Item $rustupInit -Force

    $cargoPath = "$env:USERPROFILE\.cargo\bin"
    $env:PATH = "$env:PATH;$cargoPath"

    Write-Host "[✓] Rust успешно установлен." -ForegroundColor Green
} else {
    $rustVer = rustc --version
    Write-Host "[✓] Rust уже установлен ($rustVer)." -ForegroundColor Green
}


if (!(Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Host "[!] Node.js не найден." -ForegroundColor Yellow
    Write-Host "    Автоматическая установка Node.js под Windows не выполняется этим скриптом —" -ForegroundColor Yellow
    Write-Host "    установите LTS-версию вручную: https://nodejs.org/ru/download" -ForegroundColor Yellow
    Write-Host "    После установки Node.js перезапустите этот скрипт." -ForegroundColor Yellow
} else {
    $nodeVer = node --version
    Write-Host "[✓] Node.js уже установлен ($nodeVer)." -ForegroundColor Green
}

Write-Host "[+] Сборка проекта LANChat (сервер + старый клиент)..." -ForegroundColor Cyan

if (Test-Path "LANChat.slnx") {
    dotnet build LANChat.slnx -c Release
} elseif (Test-Path "LANChat.sln") {
    dotnet build LANChat.sln -c Release
} else {
    dotnet build -c Release
}

if ((Get-Command node -ErrorAction SilentlyContinue) -and (Test-Path "lanchat-tauri")) {
    Write-Host "[+] Установка зависимостей нового клиента (lanchat-tauri)..." -ForegroundColor Cyan
    Push-Location "lanchat-tauri"
    try {
        npm install
        Write-Host "[+] Сборка нового клиента (lanchat-tauri)..." -ForegroundColor Cyan
        npm run build
    } catch {
        Write-Host "[!] Сборка lanchat-tauri не удалась: $_" -ForegroundColor Yellow
        Write-Host "    Можно собрать вручную позже: cd lanchat-tauri; npm run build" -ForegroundColor Yellow
    } finally {
        Pop-Location
    }
}

Write-Host "==========================================" -ForegroundColor Green
Write-Host "   Установка и сборка успешно завершены!  " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host "Для запуска сервера:            dotnet run --project LANChat.Server"
Write-Host "Для запуска старого клиента:    dotnet run --project LANChat.Client"
Write-Host "Для запуска нового клиента (dev): cd lanchat-tauri; npm run dev"
Write-Host "Готовый установщик нового клиента, если сборка прошла: lanchat-tauri\src-tauri\target\release\bundle\"