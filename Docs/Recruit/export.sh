#!/bin/bash
# 招募系列图导出脚本
# 用法：在本目录下执行 bash export.sh
# 会读 recruit-series.html，逐张导出到 招募系列/ 目录
# 依赖：本机已安装 Google Chrome

CHROME="C:/Program Files/Google/Chrome/Application/chrome.exe"
DIR="D:/PenBattle/Docs/Recruit"
OUT="$DIR/招募系列"

if [ ! -f "$CHROME" ]; then
  echo "找不到 Chrome，请修改脚本里的 CHROME 路径"
  exit 1
fi

mkdir -p "$OUT"
cd "$DIR" || exit 1

for n in 1 2 3 4 5 6; do
  "$CHROME" --headless=new --disable-gpu --hide-scrollbars \
    --screenshot="$OUT/slide-0$n.png" \
    --window-size=750,1000 --default-background-color=0xfffdf7 \
    --virtual-time-budget=5000 \
    "file:///$DIR/recruit-series.html?only=$n" 2>/dev/null
  echo "  已导出 slide-0$n.png"
done

echo "完成，输出目录：$OUT"
