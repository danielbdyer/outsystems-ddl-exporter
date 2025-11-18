#!/bin/bash
set -e

echo "🚀 .NET Setup: Initializing environment..."

# Only run in remote Claude Code Cloud
if [ "$CLAUDE_CODE_REMOTE" != "true" ]; then
  echo "ℹ️  Not in Claude Code Cloud, skipping .NET setup"
  exit 0
fi

# Check if .NET is already installed and at the correct version
if command -v dotnet &> /dev/null; then
  CURRENT_VERSION=$(dotnet --version 2>/dev/null || echo "unknown")
  REQUIRED_VERSION="9.0.307"

  if [ "$CURRENT_VERSION" = "$REQUIRED_VERSION" ]; then
    echo "✅ .NET SDK $REQUIRED_VERSION already installed"
  else
    echo "⚠️  .NET SDK version mismatch: found $CURRENT_VERSION, need $REQUIRED_VERSION"
  fi
else
  echo "📦 Installing .NET SDK 9.0.307..."

  # Create installation directory
  DOTNET_INSTALL_DIR="/usr/share/dotnet"

  # Download and run .NET install script
  cd /tmp
  if [ ! -f dotnet-install.sh ]; then
    wget -q https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
    chmod +x dotnet-install.sh
  fi

  # Install .NET SDK to shared location
  ./dotnet-install.sh --channel 9.0 --install-dir "$DOTNET_INSTALL_DIR" --no-path --verbose 2>&1 | grep -v "^dotnet-install: Extracting" || true

  # Create symlink to make dotnet available system-wide
  if [ ! -L /usr/bin/dotnet ]; then
    ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/bin/dotnet 2>/dev/null || true
  fi

  # Verify installation
  if dotnet --version &> /dev/null; then
    echo "✅ .NET SDK $(dotnet --version) installed successfully"
  else
    echo "❌ .NET SDK installation failed"
    exit 1
  fi
fi

# Attempt to restore NuGet packages
echo "📥 Attempting to restore NuGet packages..."
cd "$CLAUDE_PROJECT_DIR"

if [ -f "OutSystemsModelToSql.sln" ]; then
  # Try to restore, but don't fail the hook if it doesn't work
  # (proxy authentication issues may prevent this from working)
  if dotnet restore OutSystemsModelToSql.sln --verbosity quiet 2>&1; then
    echo "✅ NuGet packages restored successfully"
  else
    echo "⚠️  NuGet restore failed (likely due to proxy authentication)"
    echo "ℹ️  You can try running 'dotnet restore' manually if needed"
    echo "ℹ️  See claude.md for workarounds and more information"
    # Don't exit with error - we still want the session to start
  fi
else
  echo "⚠️  Solution file not found, skipping package restore"
fi

echo "✨ .NET setup complete"
exit 0
