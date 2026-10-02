#!/usr/bin/env bash

set -e

GREEN='\033[0;32m'
CYAN='\033[0;36m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${CYAN}=====================================${NC}"
echo "Select Language / Выберите язык:"
echo "1) English"
echo "2) Русский"
echo -n "Choice / Выбор (1-2): "
read -r lang_choice

if [ "$lang_choice" = "2" ]; then
    LANG_MODE="ru"
else
    LANG_MODE="en"
fi

if [ "$LANG_MODE" = "ru" ]; then
    MSG_TITLE="=== Автоматическая установка и запуск LANChat ==="
    MSG_DETECTED="[+] Определена система:"
    MSG_CHECK_DEPS="[*] Установка системных зависимостей..."
    MSG_UNRECOGNIZED="[!] Дистрибутив не распознан автоматически. Убедитесь, что есть .NET SDK, Rust и Node.js."
    MSG_NO_BREW="[!] Homebrew не найден. Установите Homebrew или пакеты вручную: https://brew.sh"
    MSG_NODE_FIX="[!] Системный Node.js не работает или отсутствует. Ставим через NVM..."
    MSG_DOTNET_CHECK="[*] Проверка .NET SDK..."
    MSG_DOTNET_FOUND="[✓] .NET SDK уже установлен:"
    MSG_DOTNET_INSTALLING="[+] .NET SDK не найден. Устанавливаем .NET 10..."
    MSG_DOTNET_SKIP_TERMUX="[!] .NET SDK на Termux официально не поддерживается — сервер и старый клиент здесь, скорее всего, не запустятся."
    MSG_RUST_CHECK="[*] Проверка Rust..."
    MSG_RUST_FOUND="[✓] Rust уже установлен:"
    MSG_RUST_INSTALLING="[+] Rust не найден. Устанавливаем через rustup..."
    MSG_RUST_SKIP_TERMUX="[!] Пропускаем Rust на Termux (десктопный Tauri-клиент здесь не запустить)."
    MSG_BUILD_DOTNET="[*] Сборка сервера и старого клиента (.NET)..."
    MSG_BUILD_DOTNET_FAIL="[!] Сборка .NET-проектов не удалась. Проверьте вывод выше."
    MSG_BUILD_TAURI="[*] Сборка нового клиента (lanchat-tauri)..."
    MSG_BUILD_TAURI_FAIL="[!] Сборка Tauri не удалась (можно запустить в dev-режиме через пункт меню)."
    MSG_BUILD_SKIP_TERMUX="[!] Пропускаем сборку Tauri-клиента на Termux."
    MSG_MENU_TITLE="   Интерактивное меню запуска"
    MSG_OPT_1="1) Запустить сервер (LANChat.Server)"
    MSG_OPT_2="2) Запустить новый клиент (lanchat-tauri)"
    MSG_OPT_3="3) Запустить старый клиент (LANChat.Client)"
    MSG_OPT_4="4) Выйти"
    MSG_PROMPT="Выберите действие (1-4): "
    MSG_START_SERVER="[+] Запуск сервера..."
    MSG_START_TAURI_RELEASE="[+] Запуск собранного релиза нового клиента..."
    MSG_START_TAURI_DEV="[+] Релизная сборка не найдена — запускаем в dev-режиме..."
    MSG_START_OLD="[+] Запуск старого клиента..."
    MSG_NO_SERVER="[!] Проект LANChat.Server не найден или .NET SDK не установлен."
    MSG_NO_TAURI="[!] Папка lanchat-tauri не найдена, либо не установлены Node.js/Rust."
    MSG_NO_OLD="[!] Проект LANChat.Client не найден или .NET SDK не установлен."
    MSG_NO_DOTNET_RUNTIME="    Запустите установку заново или поставьте .NET SDK вручную: https://dotnet.microsoft.com"
    MSG_INVALID="[!] Неверный выбор. Попробуйте снова."
    MSG_EXIT="Завершение работы..."
else
    MSG_TITLE="=== LANChat Automated Installer & Runner ==="
    MSG_DETECTED="[+] Detected system:"
    MSG_CHECK_DEPS="[*] Installing system dependencies..."
    MSG_UNRECOGNIZED="[!] Linux distribution not recognized automatically. Ensure .NET SDK, Rust and Node.js are present."
    MSG_NO_BREW="[!] Homebrew not found. Please install Homebrew or the required packages manually: https://brew.sh"
    MSG_NODE_FIX="[!] System Node.js is broken or missing. Installing via NVM..."
    MSG_DOTNET_CHECK="[*] Checking .NET SDK..."
    MSG_DOTNET_FOUND="[✓] .NET SDK already installed:"
    MSG_DOTNET_INSTALLING="[+] .NET SDK not found. Installing .NET 10..."
    MSG_DOTNET_SKIP_TERMUX="[!] .NET SDK is not officially supported on Termux — the server and classic client will likely not run here."
    MSG_RUST_CHECK="[*] Checking Rust..."
    MSG_RUST_FOUND="[✓] Rust already installed:"
    MSG_RUST_INSTALLING="[+] Rust not found. Installing via rustup..."
    MSG_RUST_SKIP_TERMUX="[!] Skipping Rust on Termux (the desktop Tauri client can't run here anyway)."
    MSG_BUILD_DOTNET="[*] Building the server and classic client (.NET)..."
    MSG_BUILD_DOTNET_FAIL="[!] .NET build failed. Check the output above."
    MSG_BUILD_TAURI="[*] Building the new client (lanchat-tauri)..."
    MSG_BUILD_TAURI_FAIL="[!] Tauri build failed (you can still run dev mode from the menu)."
    MSG_BUILD_SKIP_TERMUX="[!] Skipping the Tauri client build on Termux."
    MSG_MENU_TITLE="   Interactive Execution Menu"
    MSG_OPT_1="1) Start Server (LANChat.Server)"
    MSG_OPT_2="2) Start New Client (lanchat-tauri)"
    MSG_OPT_3="3) Start Legacy Client (LANChat.Client)"
    MSG_OPT_4="4) Exit"
    MSG_PROMPT="Select an option (1-4): "
    MSG_START_SERVER="[+] Launching server..."
    MSG_START_TAURI_RELEASE="[+] Launching the built release of the new client..."
    MSG_START_TAURI_DEV="[+] No release build found — launching in dev mode..."
    MSG_START_OLD="[+] Launching legacy client..."
    MSG_NO_SERVER="[!] LANChat.Server project not found, or .NET SDK is not installed."
    MSG_NO_TAURI="[!] lanchat-tauri folder not found, or Node.js/Rust are not installed."
    MSG_NO_OLD="[!] LANChat.Client project not found, or .NET SDK is not installed."
    MSG_NO_DOTNET_RUNTIME="    Re-run the installer, or install the .NET SDK manually: https://dotnet.microsoft.com"
    MSG_INVALID="[!] Invalid option. Please try again."
    MSG_EXIT="Exiting..."
fi

echo -e "\n${CYAN}${MSG_TITLE}${NC}"

OS_TYPE="unknown"

if [ -n "$TERMUX_VERSION" ] || [ -d "/data/data/com.termux" ]; then
    OS_TYPE="termux"
elif [ "$(uname)" == "Darwin" ]; then
    OS_TYPE="macos"
elif [ -f /etc/os-release ]; then
    . /etc/os-release
    OS_TYPE=$ID
fi

echo -e "${GREEN}${MSG_DETECTED} ${OS_TYPE}${NC}"


install_deps() {
    echo -e "${CYAN}${MSG_CHECK_DEPS}${NC}"

    case "$OS_TYPE" in
        termux)
            pkg update -y
            pkg install -y nodejs build-essential libffi openssl
            ;;
        arch|manjaro|endeavouros|garuda)
            
            sudo pacman -Sy --needed --noconfirm base-devel curl wget openssl \
                webkit2gtk-4.1 appmenu-gtk-module gtk3 librsvg \
                libappindicator-gtk3 nodejs npm patchelf file \
                fuse2 squashfs-tools
            ;;
        ubuntu|debian|pop|mint|kali)
            sudo apt-get update -y
            sudo apt-get install -y -qq build-essential curl wget libssl-dev \
                libgtk-3-dev libwebkit2gtk-4.1-dev libayatana-appindicator3-dev \
                librsvg2-dev nodejs npm patchelf file \
                fuse squashfs-tools
            ;;
        fedora|nobara|rhel|centos)
            sudo dnf groupinstall -y "Development Tools"
            sudo dnf install -y openssl-devel gtk3-devel webkit2gtk4.1-devel \
                libappindicator-gtk3-devel librsvg2-devel nodejs npm patchelf file \
                fuse-libs squashfs-tools
            ;;
        alpine)
            sudo apk add --no-cache build-base nodejs npm gtk+3.0-dev \
                webkit2gtk-dev patchelf file \
                fuse squashfs-tools
            ;;
        macos)
            if ! command -v brew &> /dev/null; then
                echo -e "${RED}${MSG_NO_BREW}${NC}"
            else
                brew install node
            fi
            ;;
        *)
            e
=====================================
   Интерактивное меню запуска
=====================================
1) Запустить сервер (LANChat.Server)cho -e "${RED}${MSG_UNRECOGNIZED}${NC}"
            ;;
    esac
}

install_deps


check_and_fix_node() {
    if ! node -v &>/dev/null; then
        echo -e "${RED}${MSG_NODE_FIX}${NC}"
        export NVM_DIR="$HOME/.nvm"
        if [ ! -d "$NVM_DIR" ]; then
            curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.7/install.sh | bash
        fi
        [ -s "$NVM_DIR/nvm.sh" ] && \. "$NVM_DIR/nvm.sh"
        nvm install 20
        nvm use 20
    fi
}

check_and_fix_node


install_dotnet() {
    echo -e "${CYAN}${MSG_DOTNET_CHECK}${NC}"

    if command -v dotnet &> /dev/null; then
        echo -e "${GREEN}${MSG_DOTNET_FOUND} $(dotnet --version)${NC}"
        return
    fi

    if [ "$OS_TYPE" = "termux" ]; then
        echo -e "${RED}${MSG_DOTNET_SKIP_TERMUX}${NC}"
        return
    fi

    echo -e "${CYAN}${MSG_DOTNET_INSTALLING}${NC}"
    curl -sSL https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 || true
    rm -f /tmp/dotnet-install.sh

    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"

    SHELL_RC="$HOME/.bashrc"
    if [ -n "$ZSH_VERSION" ] || [ -f "$HOME/.zshrc" ]; then
        SHELL_RC="$HOME/.zshrc"
    fi
    if ! grep -q "DOTNET_ROOT" "$SHELL_RC" 2>/dev/null; then
        {
            echo ''
            echo 'export DOTNET_ROOT="$HOME/.dotnet"'
            echo 'export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"'
        } >> "$SHELL_RC"
    fi
}

install_dotnet


install_rust() {
    echo -e "${CYAN}${MSG_RUST_CHECK}${NC}"

    if [ "$OS_TYPE" = "termux" ]; then
        echo -e "${RED}${MSG_RUST_SKIP_TERMUX}${NC}"
        return
    fi

    if command -v cargo &> /dev/null; then
        echo -e "${GREEN}${MSG_RUST_FOUND} $(rustc --version)${NC}"
        return
    fi

    echo -e "${CYAN}${MSG_RUST_INSTALLING}${NC}"
    curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.org | sh -s -- -y
    # shellcheck source=/dev/null
    source "$HOME/.cargo/env"
}

install_rust


if [ -f "LANChat.slnx" ] && command -v dotnet &> /dev/null; then
    echo -e "${CYAN}${MSG_BUILD_DOTNET}${NC}"
    dotnet build LANChat.slnx -c Release || echo -e "${RED}${MSG_BUILD_DOTNET_FAIL}${NC}"
fi

if [ "$OS_TYPE" = "termux" ]; then
    echo -e "${RED}${MSG_BUILD_SKIP_TERMUX}${NC}"
elif [ -d "lanchat-tauri" ] && command -v npm &> /dev/null; then
    echo -e "${CYAN}${MSG_BUILD_TAURI}${NC}"
    (
        cd lanchat-tauri
        npm install --silent
      
        export APPIMAGE_EXTRACT_AND_RUN=1
        npm run tauri build
    ) || echo -e "${RED}${MSG_BUILD_TAURI_FAIL}${NC}"
fi


set +e

find_tauri_release_binary() {
    
    local candidates=(
        "lanchat-tauri/src-tauri/target/release/lanchat-tauri"
        "lanchat-tauri/src-tauri/target/release/lanchat-tauri.exe"
    )
    for c in "${candidates[@]}"; do
        if [ -x "$c" ]; then
            echo "$c"
            return 0
        fi
    done
    return 1
}

while true; do
    echo ""
    echo -e "${CYAN}=====================================${NC}"
    echo -e "${GREEN}${MSG_MENU_TITLE}${NC}"
    echo -e "${CYAN}=====================================${NC}"
    echo "$MSG_OPT_1"
    echo "$MSG_OPT_2"
    echo "$MSG_OPT_3"
    echo "$MSG_OPT_4"
    echo -n "$MSG_PROMPT"
    read -r choice

    case $choice in
        1)
            echo -e "${GREEN}${MSG_START_SERVER}${NC}"
            if [ -d "LANChat.Server" ] && command -v dotnet &> /dev/null; then
                dotnet run --project LANChat.Server -c Release
            else
                echo -e "${RED}${MSG_NO_SERVER}${NC}"
                echo -e "${RED}${MSG_NO_DOTNET_RUNTIME}${NC}"
            fi
            ;;
        2)
            if TAURI_BIN=$(find_tauri_release_binary); then
                echo -e "${GREEN}${MSG_START_TAURI_RELEASE}${NC}"
                "./$TAURI_BIN"
            elif [ -d "lanchat-tauri" ] && command -v npm &> /dev/null; then
                echo -e "${CYAN}${MSG_START_TAURI_DEV}${NC}"
                (cd lanchat-tauri && npm run tauri dev)
            else
                echo -e "${RED}${MSG_NO_TAURI}${NC}"
            fi
            ;;
        3)
            echo -e "${GREEN}${MSG_START_OLD}${NC}"
            if [ -d "LANChat.Client" ] && command -v dotnet &> /dev/null; then
                dotnet run --project LANChat.Client -c Release
            else
                echo -e "${RED}${MSG_NO_OLD}${NC}"
                echo -e "${RED}${MSG_NO_DOTNET_RUNTIME}${NC}"
            fi
            ;;
        4)
            echo "$MSG_EXIT"
            exit 0
            ;;
        *)
            echo -e "${RED}${MSG_INVALID}${NC}"
            ;;
    esac
done