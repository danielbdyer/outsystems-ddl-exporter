# .NET Development Setup for outsystems-ddl-exporter

## Overview

This repository requires .NET SDK 9.0.307 to build and run. The project uses C# preview language features and consists of multiple class libraries and test projects organized in a Visual Studio solution.

## Prerequisites

- .NET SDK 9.0.307 (exact version required due to `rollForward: disable` in global.json)
- Ubuntu 24.04 LTS (or compatible Linux distribution)
- Internet access to download NuGet packages

## Installation Instructions

### Method 1: Using Microsoft's Installation Script (Recommended)

This method works on most Linux systems and allows you to install a specific .NET SDK version:

```bash
# Download and run the .NET installation script
cd /tmp
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet

# Create a symlink to make dotnet available system-wide
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet

# Verify installation
dotnet --version
# Should output: 9.0.307
```

### Method 2: Using Package Manager (Ubuntu)

For Ubuntu systems, you can use the Microsoft package repository:

```bash
# Download and install Microsoft package repository configuration
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Update package lists and install .NET SDK 9.0
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0

# Verify installation
dotnet --version
```

### Verify SDK Installation

After installation, verify that the correct SDK is available:

```bash
dotnet --list-sdks
# Should show: 9.0.307 [/usr/share/dotnet/sdk]
```

## Building the Project

### Restore Dependencies

Before building, restore all NuGet package dependencies:

```bash
dotnet restore OutSystemsModelToSql.sln
```

### Build the Solution

To build all projects in the solution:

```bash
dotnet build OutSystemsModelToSql.sln
```

To build in Release configuration:

```bash
dotnet build OutSystemsModelToSql.sln -c Release
```

### Build a Specific Project

To build only the CLI project:

```bash
dotnet build src/Osm.Cli/Osm.Cli.csproj
```

### Run Tests

To run all tests:

```bash
dotnet test OutSystemsModelToSql.sln
```

### Run the Application

To run the CLI application:

```bash
dotnet run --project src/Osm.Cli/Osm.Cli.csproj -- [arguments]
```

## Project Structure

The solution is organized into three main folders:

- **src/**: Source code projects
  - `Osm.Domain`: Core domain models
  - `Osm.Json`: JSON serialization/deserialization
  - `Osm.Validation`: Validation logic
  - `Osm.Pipeline`: Pipeline processing
  - `Osm.Cli`: Command-line interface
  - `Osm.LoadHarness`: Load testing harness
  - `Osm.Smo`: SQL Server Management Objects integration
  - `Osm.Emission`: Code emission
  - `Osm.Dmm`: Database migration management

- **tests/**: Test projects (mirrors src/ structure with `.Tests` suffix)
  - Unit tests for each component
  - Integration tests for end-to-end scenarios

- **tools/**: Utility tools
  - `FullExportLoadHarness`: Load testing tool for full exports

## Expected Build Warnings (When Dependencies Are Available)

When building successfully in an environment with proper network access to NuGet, you may encounter the following classes of warnings:

### Common .NET 9 Warnings

1. **Nullable Reference Type Warnings (CS8600-CS8625)**
   - The project has nullable reference types enabled (`<Nullable>enable</Nullable>`)
   - You may see warnings about possible null reference assignments
   - Example: `CS8600: Converting null literal or possible null value to non-nullable type`

2. **Preview Feature Warnings (CS8936)**
   - The project uses preview C# language features (`<LangVersion>preview</LangVersion>`)
   - Preview features may generate warnings about experimental APIs
   - Example: `CS8936: Feature 'X' is in preview`

3. **Obsolete API Warnings (CS0618)**
   - Some NuGet packages may use deprecated APIs
   - Example: `CS0618: 'SomeClass.SomeMethod()' is obsolete`

4. **Missing XML Documentation (CS1591)**
   - If XML documentation generation is enabled
   - Example: `CS1591: Missing XML comment for publicly visible type or member`

5. **Package Dependency Warnings (NU1701-NU1903)**
   - NuGet may warn about package compatibility or security vulnerabilities
   - Example: `NU1701: Package was restored using .NETFramework instead of .NET`

## Known Issues

### Proxy Authentication Limitation with .NET NuGet Client

If you encounter errors like:

```
error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.
error NU1301:   The proxy tunnel request to proxy 'http://proxy:port/' failed with status code '401'.
```

This indicates that .NET's NuGet client cannot properly authenticate with the proxy server.

**Root Cause:**

While tools like `curl` and `wget` properly parse proxy credentials from `HTTP_PROXY` environment variables in the format `http://user:pass@proxy:port`, .NET's `HttpClient` (used by NuGet) does not extract and use these embedded credentials. It only uses the proxy address (`http://proxy:port/`) without authentication, resulting in a `401 Unauthorized` response.

This is a known limitation of how .NET handles proxy authentication from environment variables, particularly with non-standard authentication schemes.

**Workaround options:**
1. Use a development machine with direct internet access (no proxy required)
2. Use a proxy that doesn't require authentication
3. Configure proxy credentials in `~/.nuget/NuGet/NuGet.Config` (if proxy supports Basic auth)
4. Pre-populate a local NuGet package cache and use `--source` to point to the local cache
5. Use a corporate NuGet feed/proxy with .NET-compatible authentication

## Technology Stack

- **Language**: C# with preview features
- **Framework**: .NET 9.0
- **Key Dependencies**:
  - Microsoft.Extensions.Hosting 9.0.0-rc.1
  - Microsoft.Extensions.Logging 9.0.0-rc.1
  - System.CommandLine 2.0.0-beta4
  - System.Collections.Immutable 9.0.9

## Configuration Files

- **global.json**: Specifies the exact .NET SDK version (9.0.307) with `rollForward: disable`
- **OutSystemsModelToSql.sln**: Visual Studio solution file containing all projects
- **pipeline.json**: Pipeline configuration
- **Various .csproj files**: Individual project configurations with target framework `net9.0`

## Current Build Status

As of the last attempt in this environment:

- **SDK Installation**: ✅ Successful (9.0.307 installed)
- **NuGet Restore**: ❌ Failed (network access limitation)
- **Build**: ⏸️ Blocked by restore failure

In a standard development environment with internet access, the build should complete successfully after restoring all NuGet dependencies.

## Next Steps for Development

1. Ensure .NET SDK 9.0.307 is installed
2. Clone the repository
3. Run `dotnet restore` to download dependencies
4. Run `dotnet build` to compile all projects
5. Run `dotnet test` to verify everything works
6. Use `dotnet run --project src/Osm.Cli/Osm.Cli.csproj` to execute the CLI

## Additional Resources

- [.NET 9 Documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)
- [Installing .NET on Linux](https://learn.microsoft.com/dotnet/core/install/linux)
- [NuGet Package Manager](https://learn.microsoft.com/nuget/)
