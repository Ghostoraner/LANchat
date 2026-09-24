# Собирает готовые к раздаче бинарники LANChat.Server и LANChat.Client
# (self-contained, один файл, .NET runtime уже внутри — установка SDK
# на машине получателя не требуется).
#
# Использование:
#   .\publish.ps1                     # соберёт linux-x64 и win-x64
#   .\publish.ps1 -Rids win-x64       # только один RID
#
param(
    [string[]]$Rids = @("linux-x64", "win-x64")
)

$ErrorActionPreference = "Stop"
$Config = "Release"
$OutRoot = "release"

if (Test-Path $OutRoot) { Remove-Item -Recurse -Force $OutRoot }
New-Item -ItemType Directory -Path $OutRoot | Out-Null

foreach ($rid in $Rids) {
    Write-Host "=========================================="
    Write-Host "  Сборка для $rid"
    Write-Host "=========================================="

    $serverOut = Join-Path $OutRoot "$rid/LANChat.Server"
    $clientOut = Join-Path $OutRoot "$rid/LANChat.Client"

    dotnet publish LANChat.Server/LANChat.Server.csproj `
        -c $Config -r $rid -o $serverOut --self-contained true

    dotnet publish LANChat.Client/LANChat.Client.csproj `
        -c $Config -r $rid -o $clientOut --self-contained true

    $zipPath = Join-Path $OutRoot "LANChat-$rid.zip"
    Compress-Archive -Path (Join-Path $OutRoot "$rid/*") -DestinationPath $zipPath -Force

    Write-Host "[OK] Готово: $zipPath"
}

Write-Host ""
Write-Host "Все сборки лежат в папке $OutRoot/"