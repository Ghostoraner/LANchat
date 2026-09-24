#!/bin/bash
# Собирает готовые к раздаче бинарники LANChat.Server и LANChat.Client
# (self-contained, один файл, .NET runtime уже внутри — установка SDK
# на машине получателя не требуется).
#
# Использование:
#   ./publish.sh                     # соберёт linux-x64 и win-x64
#   ./publish.sh linux-x64           # только один RID
#
set -e

CONFIG="Release"
OUT_ROOT="release"
RIDS=("$@")
if [ ${#RIDS[@]} -eq 0 ]; then
    RIDS=("linux-x64" "win-x64")
fi

rm -rf "$OUT_ROOT"
mkdir -p "$OUT_ROOT"

for rid in "${RIDS[@]}"; do
    echo "=========================================="
    echo "  Сборка для $rid"
    echo "=========================================="

    server_out="$OUT_ROOT/$rid/LANChat.Server"
    client_out="$OUT_ROOT/$rid/LANChat.Client"

    dotnet publish LANChat.Server/LANChat.Server.csproj \
        -c "$CONFIG" -r "$rid" -o "$server_out" --self-contained true

    dotnet publish LANChat.Client/LANChat.Client.csproj \
        -c "$CONFIG" -r "$rid" -o "$client_out" --self-contained true

    zip_path="$OUT_ROOT/LANChat-$rid.zip"
    (cd "$OUT_ROOT/$rid" && zip -r "../../$zip_path" .)

    echo "[✓] Готово: $zip_path"
done

echo ""
echo "Все сборки лежат в папке $OUT_ROOT/"