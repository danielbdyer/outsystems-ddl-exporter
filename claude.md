# Developer Setup Guide

This guide helps you set up your development environment to build and run the OutSystems DDL Exporter.

## Prerequisites

This repository requires:
- **.NET 9 SDK version 9.0.307** (pinned in `global.json`)
- **C# 13 preview** compiler
- **SQL Server** 2017+ (for live profiling; optional for fixture-based development)

## Installing .NET 9 SDK

The project uses .NET 9 SDK version **9.0.307**. Follow the instructions for your platform:

### Windows

**Option 1: Using Windows Package Manager (recommended)**
```bash
winget install Microsoft.DotNet.SDK.9
```

**Option 2: Direct Download**
1. Visit https://dotnet.microsoft.com/download/dotnet/9.0
2. Download the SDK 9.0.307 installer for your architecture (x64, x86, or Arm64)
3. Run the installer and follow the prompts

### macOS

**Option 1: Using Homebrew**
```bash
brew install --cask dotnet-sdk
```

**Option 2: Direct Download**
1. Visit https://dotnet.microsoft.com/download/dotnet/9.0
2. Download the SDK 9.0.307 installer for your processor:
   - **Arm64** (Apple Silicon - M1/M2/M3)
   - **x64** (Intel)
3. Open the downloaded .pkg file and follow the installation prompts

### Linux

**Option 1: Using Microsoft Package Repository**

For Ubuntu/Debian:
```bash
# Add Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install .NET SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0
```

For other distributions, see: https://learn.microsoft.com/en-us/dotnet/core/install/linux

**Option 2: Using Install Script**
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 9.0.307
```

## Verifying Your Installation

After installation, verify that you have the correct version:

```bash
dotnet --version
```

You should see: `9.0.307`

To see all installed SDKs:
```bash
dotnet --list-sdks
```

## Building the Project

### Restore Dependencies
```bash
dotnet restore
```

### Build the Solution
```bash
dotnet build
```

### Run Tests
```bash
dotnet test
```

## Running the CLI

The main CLI entry point is in `src/Osm.Cli`. Here are the primary commands:

### 1. Extract Model (from fixtures)
```bash
dotnet run --project src/Osm.Cli \
  extract-model \
  --mock-advanced-sql tests/Fixtures/extraction/advanced-sql.manifest.json \
  --modules "AppCore,ExtBilling,Ops" \
  --out ./out/model.json
```

### 2. Build SSDT (using fixtures)
```bash
dotnet run --project src/Osm.Cli \
  build-ssdt \
  --model tests/Fixtures/model.edge-case.json \
  --profile tests/Fixtures/profiling/profile.edge-case.json \
  --out ./out
```

### 3. Full Export Pipeline (one-shot)
```bash
dotnet run --project src/Osm.Cli \
  full-export \
  --mock-advanced-sql tests/Fixtures/extraction/advanced-sql.manifest.json \
  --profile-out ./out/profiles \
  --build-out ./out/full-export \
  --modules "AppCore,ExtBilling,Ops"
```

### 4. DMM Comparison (verify parity)
```bash
dotnet run --project src/Osm.Cli \
  dmm-compare \
  --model tests/Fixtures/model.edge-case.json \
  --profile tests/Fixtures/profiling/profile.edge-case.json \
  --dmm ./out/dmm/edge-case.sql \
  --out ./out/dmm-diff.json
```

## Working with Live Databases

To connect to a real SQL Server instance (instead of using fixtures):

```bash
dotnet run --project src/Osm.Cli \
  build-ssdt \
  --model ./cache/model.json \
  --profiler-provider sql \
  --connection-string "Server=localhost;Database=OutSystems;Integrated Security=true" \
  --out ./out-live
```

## Common Issues

### "Unsupported target framework" Error
This means the .NET 9 SDK is not installed or not found. Verify:
1. Run `dotnet --version` to check your SDK version
2. Ensure `global.json` is present (it pins the SDK version)
3. Reinstall .NET 9 SDK if needed

### MSBuild Logger Crashes
This typically indicates an older SDK is being used. The project requires .NET 9 SDK 9.0.307 specifically.

### Permission Errors (SQL Server)
The profiler requires `SELECT` permissions on the target database. No DDL permissions are needed for generating scripts (only for applying them).

## Project Structure

```
OutSystemsModelToSql.sln          # Main solution file
src/
  Osm.Cli/                        # CLI entry point
  Osm.Domain/                     # DTOs: Model, Profiling, Decisions
  Osm.Json/                       # JSON provider + schema validation
  Osm.Validation/                 # Model validation + TighteningPolicy
  Osm.Smo/                        # SMO builder + DDL emitter
  Osm.Dmm/                        # DMM parser and comparator
  Osm.Pipeline/                   # Orchestrators
tests/
  Osm.*.Tests/                    # Unit and integration tests
  Fixtures/                       # Test data and golden files
```

## Next Steps

- Read the comprehensive [README.md](readme.md) for detailed architecture and usage
- Review the [design contracts](notes/design-contracts.md) for interface boundaries
- Check [tasks.md](tasks.md) for current development priorities
- Explore [test-plan.md](notes/test-plan.md) for testing strategy

## Getting Help

- Review CLI help: `dotnet run --project src/Osm.Cli -- --help`
- Check command-specific help: `dotnet run --project src/Osm.Cli -- build-ssdt --help`
- See the main README for troubleshooting and FAQ sections

## Configuration

The CLI supports configuration files to avoid repeating flags:

1. Copy `config/appsettings.example.json` to `config/appsettings.json`
2. Edit paths and connection strings
3. Use `--config config/appsettings.json` or set `OSM_CLI_CONFIG_PATH` environment variable

Example minimal config:
```json
{
  "model": {
    "path": "tests/Fixtures/model.edge-case.json"
  },
  "profile": {
    "path": "tests/Fixtures/profiling/profile.edge-case.json"
  },
  "profiler": {
    "provider": "Fixture"
  }
}
```

---

**Last Updated**: 2025-11-18
**Required .NET Version**: 9.0.307
