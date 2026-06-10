#!/usr/bin/env bash
set -euo pipefail

REPO="akostt/vpn-check"
BINARY="VPNCheck"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"

# Detect OS
OS=$(uname -s)
case "$OS" in
  Linux)  OS_TAG="linux" ;;
  Darwin) OS_TAG="osx" ;;
  *)      echo "Unsupported OS: $OS" >&2; exit 1 ;;
esac

# Detect architecture
ARCH=$(uname -m)
case "$ARCH" in
  x86_64)          ARCH_TAG="x64" ;;
  aarch64 | arm64) ARCH_TAG="arm64" ;;
  *)               echo "Unsupported architecture: $ARCH" >&2; exit 1 ;;
esac

ASSET="VPNCheck-${OS_TAG}-${ARCH_TAG}.zip"

echo "Fetching latest release..."
LATEST=$(curl -fsSL -o /dev/null -w '%{url_effective}' \
  "https://github.com/$REPO/releases/latest" | sed 's|.*/||')

if [ -z "$LATEST" ]; then
  echo "Failed to fetch latest release" >&2
  exit 1
fi

URL="https://github.com/$REPO/releases/download/$LATEST/$ASSET"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

echo "Downloading $ASSET ($LATEST)..."
curl -fsSL "$URL" -o "$TMP/$ASSET"

echo "Installing..."
unzip -q "$TMP/$ASSET" -d "$TMP/out"

mkdir -p "$INSTALL_DIR"
cp "$TMP/out/$BINARY" "$INSTALL_DIR/$BINARY"
chmod +x "$INSTALL_DIR/$BINARY"

echo ""
echo "✓ VPNCheck $LATEST installed → $INSTALL_DIR/$BINARY"

# Warn if INSTALL_DIR is not in PATH
if ! echo "$PATH" | tr ':' '\n' | grep -qxF "$INSTALL_DIR"; then
  echo ""
  echo "  $INSTALL_DIR is not in PATH. Add to ~/.bashrc or ~/.zshrc:"
  echo "    export PATH=\"\$PATH:$INSTALL_DIR\""
fi

echo ""
echo "Run: VPNCheck"
