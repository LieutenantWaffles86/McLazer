#!/usr/bin/env bash
# Builds a self-contained McLazer for 64-bit Linux: dist/McLazer-linux-x64.tar.gz.
# The tarball holds the game, a .desktop file and install.sh, which installs to ~/.local/opt/McLazer
# and adds McLazer to the application menu. No .NET install is needed to run it.
set -euo pipefail
cd "$(dirname "$0")"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

out=dist/McLazer
rm -rf dist
dotnet publish osu.Desktop -c Release -r linux-x64 --self-contained -o "$out"
cp osu.Desktop/mclazer.ico "$out/mclazer.ico"

cat > "$out/install.sh" <<'INSTALL'
#!/usr/bin/env bash
# Installs McLazer to ~/.local/opt/McLazer and adds it to the application menu.
set -euo pipefail
target="$HOME/.local/opt/McLazer"
rm -rf "$target"
mkdir -p "$target" "$HOME/.local/share/applications"
cp -r "$(dirname "$0")"/. "$target"
sed "s|@TARGET@|$target|g" "$target/McLazer.desktop" > "$HOME/.local/share/applications/mclazer.desktop"
echo "Installed to $target"
INSTALL
chmod +x "$out/install.sh"

cat > "$out/McLazer.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=McLazer
Comment=Offline osu! practice client
Categories=Game;
Exec=@TARGET@/McLazer
Icon=@TARGET@/mclazer.ico
Terminal=false
StartupNotify=false
DESKTOP

tar -C dist -czf dist/McLazer-linux-x64.tar.gz McLazer
echo "Built dist/McLazer-linux-x64.tar.gz"
