#!/bin/sh
# 构建 release 并装到 ~/Applications/perchcat. 开机自启要指向这里: 装完从这个位置启动, 再到菜单里勾选.
set -eu
cd "$(dirname "$0")/.."
swift build -c release
mkdir -p "$HOME/Applications"
# 先复制到临时文件再改名: 直接覆盖正在运行的二进制会被系统杀掉
cp .build/release/perchcat "$HOME/Applications/perchcat.new"
mv -f "$HOME/Applications/perchcat.new" "$HOME/Applications/perchcat"
echo "已安装: $HOME/Applications/perchcat"
