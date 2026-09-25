$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "         LANChat Installer (Windows)      " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[+] .NET SDK not found. Downloading..." -ForegroundColor Yellow
    $installScript = "$env:TEMP\dotnet-install.ps1"

    Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1" -OutFile $installScript
    & $installScript -Channel 10.0
    Remove-Item $installScript -Force

    $dotnetPath = "$env:USERPROFILE\.dotnet"
    $env:DOTNET_ROOT = $dotnetPath
    $env:PATH = "$env:PATH;$dotnetPath"

    Write-Host "[OK] .NET SDK installed successfully." -ForegroundColor Green
} else {
    $dotnetVer = dotnet --version
    Write-Host "[OK] .NET SDK is already installed (version: $dotnetVer)." -ForegroundColor Green
}

if (!(Get-Command cargo -ErrorAction SilentlyContinue)) {
    Write-Host "[+] Rust not found. Downloading rustup-init..." -ForegroundColor Yellow
    $rustupInit = "$env:TEMP\rustup-init.exe"
    Invoke-WebRequest -Uri "https://win.rustup.rs/x86_64" -OutFile $rustupInit
    & $rustupInit -y --default-toolchain stable
    Remove-Item $rustupInit -Force

    $cargoPath = "$env:USERPROFILE\.cargo\bin"
    $env:PATH = "$env:PATH;$cargoPath"

    Write-Host "[OK] Rust installed successfully." -ForegroundColor Green
} else {
    $rustVer = rustc --version
    Write-Host "[OK] Rust is already installed ($rustVer)." -ForegroundColor Green
}

if (!(Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Host "[!] Node.js not found." -ForegroundColor Yellow
    Write-Host "    Install LTS version manually: https://nodejs.org/en/download" -ForegroundColor Yellow
    Write-Host "    Restart this script after installation." -ForegroundColor Yellow
} else {
    $nodeVer = node --version
    Write-Host "[OK] Node.js is already installed ($nodeVer)." -ForegroundColor Green
}

Write-Host "[+] Building LANChat project..." -ForegroundColor Cyan

if (Test-Path "LANChat.slnx") {
    dotnet build LANChat.slnx -c Release
} elseif (Test-Path "LANChat.sln") {
    dotnet build LANChat.sln -c Release
} else {
    dotnet build -c Release
}

if ((Get-Command node -ErrorAction SilentlyContinue) -and (Test-Path "lanchat-tauri")) {
    Write-Host "[+] Installing dependencies for lanchat-tauri..." -ForegroundColor Cyan
    Push-Location "lanchat-tauri"
    try {
        npm install
        Write-Host "[+] Building new client (lanchat-tauri)..." -ForegroundColor Cyan
        npm run build
    } catch {
        Write-Host "[!] Building lanchat-tauri failed: $_" -ForegroundColor Yellow
        Write-Host "    You can build it manually later: cd lanchat-tauri; npm run build" -ForegroundColor Yellow
    } finally {
        Pop-Location
    }
}

Write-Host "==========================================" -ForegroundColor Green
Write-Host "    Installation and build completed!     " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host "Run server:         dotnet run --project LANChat.Server"
Write-Host "Run client:         dotnet run --project LANChat.Client"
Write-Host "Run Tauri (dev):    cd lanchat-tauri; npm run dev"
