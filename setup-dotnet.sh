#!/bin/bash
# Optimized .NET 9 setup script for OutSystems DDL Exporter
# This project requires .NET 9.0.305 (pinned via global.json with rollForward disabled)
set -e

# Configuration
DOTNET_INSTALL_DIR="$HOME/.dotnet"
REQUIRED_VERSION="9.0.305"
DOTNET_CHANNEL="9.0"

# Function to check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Function to add PATH to bashrc only if not present
add_to_path_if_missing() {
    local path_entry="$1"
    local bashrc_path="$HOME/.bashrc"

    if ! grep -qF "$path_entry" "$bashrc_path" 2>/dev/null; then
        echo "$path_entry" >> "$bashrc_path"
        echo "Added to PATH: $path_entry"
    fi
}

# Function to setup PATH for current session
setup_current_path() {
    export PATH="$PATH:$DOTNET_INSTALL_DIR"
    export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
}

echo "OutSystems DDL Exporter - .NET 9 Setup"
echo "======================================="
echo ""

# Check if .NET is already installed with the correct version
if command_exists dotnet && dotnet --version >/dev/null 2>&1; then
    INSTALLED_VERSION=$(dotnet --version)
    if [ "$INSTALLED_VERSION" = "$REQUIRED_VERSION" ]; then
        echo "✅ .NET $REQUIRED_VERSION already installed and working"
        setup_current_path

        # Restore NuGet packages
        if [ -f "OutSystemsModelToSql.sln" ]; then
            echo "Restoring NuGet packages..."
            dotnet restore OutSystemsModelToSql.sln --verbosity quiet
            echo "✅ NuGet packages restored"
        fi

        echo "✅ Setup complete!"
        exit 0
    else
        echo "⚠️  Found .NET $INSTALLED_VERSION, but this project requires exactly $REQUIRED_VERSION"
        echo "    (rollForward is disabled in global.json)"
        echo "    Installing .NET $REQUIRED_VERSION..."
    fi
fi

# Install system dependencies
echo "Installing system dependencies..."
export DEBIAN_FRONTEND=noninteractive

sudo -E apt-get update -qq

# Install minimal dependencies for .NET runtime
sudo -E apt-get install -y -qq --no-install-recommends \
    -o Dpkg::Options::="--force-confdef" \
    -o Dpkg::Options::="--force-confold" \
    wget \
    ca-certificates \
    libc6 \
    libgcc1 \
    libgssapi-krb5-2 \
    libicu-dev \
    libssl-dev \
    libstdc++6 \
    zlib1g

echo "✅ System dependencies installed"

# Detect architecture
ARCH=$(dpkg --print-architecture)
case $ARCH in
    amd64) DOTNET_ARCH="x64" ;;
    arm64) DOTNET_ARCH="arm64" ;;
    armhf) DOTNET_ARCH="arm" ;;
    *) echo "❌ Unsupported architecture: $ARCH"; exit 1 ;;
esac

# Download and install .NET
echo "Downloading .NET $REQUIRED_VERSION installer..."
TEMP_DIR=$(mktemp -d)
cd "$TEMP_DIR"

wget -q https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh

echo "Installing .NET $REQUIRED_VERSION..."
./dotnet-install.sh --channel $DOTNET_CHANNEL --install-dir "$DOTNET_INSTALL_DIR" --architecture "$DOTNET_ARCH" --verbose

# Cleanup
cd /
rm -rf "$TEMP_DIR"

# Setup PATH
echo "Configuring PATH..."
add_to_path_if_missing "export PATH=\"\$PATH:$DOTNET_INSTALL_DIR\""
add_to_path_if_missing "export DOTNET_ROOT=\"$DOTNET_INSTALL_DIR\""

# Optional: Disable telemetry
add_to_path_if_missing "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
add_to_path_if_missing "export DOTNET_NOLOGO=1"

setup_current_path

# Verify installation
if ! command_exists dotnet; then
    echo "❌ Error: .NET command not found after installation"
    exit 1
fi

FINAL_VERSION=$(dotnet --version)
echo "✅ .NET $FINAL_VERSION installed successfully"

# Restore NuGet packages for the solution
if [ -f "OutSystemsModelToSql.sln" ]; then
    echo "Restoring NuGet packages..."
    dotnet restore OutSystemsModelToSql.sln --verbosity quiet
    echo "✅ NuGet packages restored"
else
    echo "⚠️  Solution file not found - run 'dotnet restore' manually from the project directory"
fi

echo ""
echo "✅ Setup complete!"
echo ""
echo "Installed:"
echo "  - .NET SDK $FINAL_VERSION"
echo "  - NuGet packages for OutSystemsModelToSql solution"
echo ""
echo "Run 'source ~/.bashrc' or restart your shell to use .NET in new sessions."
echo ""
echo "Quick start:"
echo "  dotnet --version              # Verify .NET version"
echo "  dotnet build                  # Build the solution"
echo "  dotnet test                   # Run tests"
echo "  dotnet run --project src/Osm.Cli  # Run the CLI tool"
