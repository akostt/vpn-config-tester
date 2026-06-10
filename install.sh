#!/usr/bin/env bash
set -euo pipefail

REPO="akostt/vpn-check"
BINARY="VPNCheck"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"

OS=$(uname -s)
case "$OS" in
  Linux)  OS_TAG="linux" ;;
  Darwin) OS_TAG="osx" ;;
  *)      echo "Unsupported OS: $OS" >&2; exit 1 ;;
esac

ARCH=$(uname -m)
case "$ARCH" in
  x86_64)          ARCH_TAG="x64" ;;
  aarch64 | arm64) ARCH_TAG="arm64" ;;
  *)               echo "Unsupported architecture: $ARCH" >&2; exit 1 ;;
esac

ASSET="VPNCheck-${OS_TAG}-${ARCH_TAG}.zip"
URL="https://github.com/$REPO/releases/latest/download/$ASSET"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

echo "Downloading $ASSET..."
curl -fsSL "$URL" -o "$TMP/$ASSET"

echo "Installing..."
unzip -q "$TMP/$ASSET" -d "$TMP/out"

mkdir -p "$INSTALL_DIR"
cp "$TMP/out/$BINARY" "$INSTALL_DIR/$BINARY"
chmod +x "$INSTALL_DIR/$BINARY"

echo ""
echo "✓ VPNCheck installed → $INSTALL_DIR/$BINARY"

if ! echo "$PATH" | tr ':' '\n' | grep -qxF "$INSTALL_DIR"; then
  echo ""
  echo "  $INSTALL_DIR is not in PATH. Add to ~/.bashrc or ~/.zshrc:"
  echo "    export PATH=\"\$PATH:$INSTALL_DIR\""
fi

echo ""
echo "Run: VPNCheck"
