#!/bin/bash

set -e

echo "=========================================="
echo "    LANChat Installer (Linux / Termux)   "
echo "=========================================="

if [ -n "$PREFIX" ] && [[ "$PREFIX" == *termux* ]]; then
    OS_TYPE="termux"
elif [ -f /etc/os-release ]; then
    . /etc/os-release
    OS_TYPE=$ID
else
    OS_TYPE="unknown"
fi

echo "[+] Обнаружен тип системы: $OS_TYPE"


case "$OS_TYPE" in
    termux)
        echo "[+] Настройка пакетов Termux..."
        pkg update -y && pkg upgrade -y
        pkg install x11-repo -y
        pkg install wget curl git libicu zlib termux-x11-nightly pulseaudio -y
        ;;
    arch|manjaro|endeavouros)
        echo "[+] Настройка пакетов Arch Linux..."
        sudo pacman -Sy --noconfirm --needed wget curl git icu zlib \
            nodejs npm \
            webkit2gtk-4.1 base-devel openssl appmenu-gtk-module \
            libappindicator-gtk3 librsvg
        ;;
    debian|ubuntu|pop|mint)
        echo "[+] Настройка пакетов Debian/Ubuntu..."
        sudo apt update -y
        sudo apt install -y wget curl git libicu-dev zlib1g-dev \
            nodejs npm \
            build-essential libssl-dev libgtk-3-dev librsvg2-dev \
            libayatana-appindicator3-dev libwebkit2gtk-4.1-dev file
        ;;
    fedora|rhel|centos)
        echo "[+] Настройка пакетов Fedora/RHEL..."
        sudo dnf install -y wget curl git libicu zlib \
            nodejs npm \
            gcc gcc-c++ make openssl-devel webkit2gtk4.1-devel \
            librsvg2-devel libappindicator-gtk3 file
        ;;
    *)
        echo "[!] Неизвестный дистрибутив. Пропускаем менеджер пакетов ОС."
        echo "    Node.js/npm и системные зависимости Tauri нужно будет поставить вручную,"
        echo "    см. https://v2.tauri.app/start/prerequisites/"
        ;;
esac


if ! command -v dotnet &> /dev/null; then
    echo "[+] .NET SDK не найден. Скачивание и установка .NET SDK 10..."
    wget https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh -O dotnet-install.sh
    chmod +x dotnet-install.sh
    ./dotnet-install.sh --channel 10.0
    rm dotnet-install.sh


    DOTNET_PATH="$HOME/.dotnet"
    export DOTNET_ROOT="$DOTNET_PATH"
    export PATH="$PATH:$DOTNET_PATH:$DOTNET_PATH/tools"

 
    SHELL_RC="$HOME/.bashrc"
    if [ -n "$ZSH_VERSION" ] || [ -f "$HOME/.zshrc" ]; then
        SHELL_RC="$HOME/.zshrc"
    fi

    if ! grep -q "DOTNET_ROOT" "$SHELL_RC"; then
        echo '' >> "$SHELL_RC"
        echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> "$SHELL_RC"
        echo 'export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"' >> "$SHELL_RC"
    fi
    echo "[✓] .NET SDK успешно установлен."
else
    echo "[✓] .NET SDK уже установлен ($(dotnet --version))."
fi


if [ "$OS_TYPE" != "termux" ]; then
    if ! command -v cargo &> /dev/null; then
        echo "[+] Rust не найден. Установка через rustup..."
        curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.org | sh -s -- -y
        
        source "$HOME/.cargo/env"
        echo "[✓] Rust успешно установлен."
    else
        echo "[✓] Rust уже установлен ($(rustc --version))."
    fi
fi


echo "[+] Сборка проекта LANChat (сервер + старый клиент)..."
dotnet build LANChat.slnx -c Release || dotnet build LANChat.sln -c Release

if [ "$OS_TYPE" != "termux" ] && [ -d "lanchat-tauri" ]; then
    echo "[+] Установка зависимостей нового клиента (lanchat-tauri)..."
    (cd lanchat-tauri && npm install) || echo "[!] Не удалось установить npm-зависимости — проверьте вывод выше."

    echo "[+] Сборка нового клиента (lanchat-tauri)..."
    (cd lanchat-tauri && npm run build) || echo "[!] Сборка lanchat-tauri не удалась — можно собрать вручную позже: cd lanchat-tauri && npm run build"
fi

echo "=========================================="
echo "   Установка и сборка успешно завершены!  "
echo "=========================================="
echo "Для запуска сервера:            dotnet run --project LANChat.Server"
echo "Для запуска старого клиента:    dotnet run --project LANChat.Client"
if [ "$OS_TYPE" != "termux" ]; then
    echo "Для запуска нового клиента (dev): cd lanchat-tauri && npm run dev"
    echo "Готовый установщик нового клиента, если сборка прошла: lanchat-tauri/src-tauri/target/release/bundle/"
fi