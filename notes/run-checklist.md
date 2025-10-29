# Deterministic Run & Verification Checklist

> Use this quick checklist before starting implementation work to confirm the pipeline can build and test end-to-end on the current machine or CI agent.

0. **Install the expected .NET SDK (one-time per environment)**
   - Run the helper script below to install the .NET SDK 9.0 toolchain in a deterministic location and persist the required environment variables:

```bash
#!/bin/bash
set -e

# Define the version of .NET you want to install
DOTNET_VERSION="9.0"

echo "🚧 Installing .NET SDK $DOTNET_VERSION to /root/.dotnet"

# Step 1: Install prerequisites
sudo apt-get update
sudo apt-get install -y wget

# Step 2: Download and run the official installer
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel "$DOTNET_VERSION" --install-dir /root/.dotnet

# Step 3: Export and persist environment variables
export DOTNET_ROOT=/root/.dotnet
export PATH=/root/.dotnet:$PATH

# Print them for verification
echo "📌 Environment variables set:"
echo "DOTNET_ROOT=$DOTNET_ROOT"
echo "PATH=$PATH"

# Persist to .bashrc so it's available in future shells
echo 'export DOTNET_ROOT=/root/.dotnet' >> ~/.bashrc
echo 'export PATH=/root/.dotnet:$PATH' >> ~/.bashrc

# Step 4: Confirm installation
echo "✅ Installed .NET version:"
dotnet --version

echo "✅ .NET SDK $DOTNET_VERSION setup complete"
```

1. **Validate tooling availability**
   - `dotnet --info` (ensure .NET 9 SDK—or aligned global.json version—is installed and selected).
   - `dotnet --list-sdks` (optional sanity: confirm expected SDK version is discoverable).

2. **Restore solution dependencies**
   - `dotnet restore OutSystemsModelToSql.sln`

3. **Compile once from a clean slate**
   - `dotnet build OutSystemsModelToSql.sln -c Release --no-restore`

4. **Run the full automated test suite**
   - `dotnet test OutSystemsModelToSql.sln -c Release --no-build`
   - The SQL Server integration suites (`tests/Osm.Etl.Integration.Tests` and `tests/Osm.Pipeline.Integration.Tests`) spin up containers via DotNet.Testcontainers. Ensure Docker is available locally before running these projects; if it is not, skip them and record the limitation in your status notes.

5. **(Optional) Smoke the CLI with fixtures**
   - `dotnet run --project src/Osm.Cli -- --in tests/Fixtures/model.edge-case.json` *(uses the edge-case fixture to exercise ingestion summary).* 

> Re-run the restore → build → test steps whenever dependencies change, new projects are added, or before publishing a PR to guarantee deterministic behavior.
