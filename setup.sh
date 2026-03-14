#!/usr/bin/env bash
set -euo pipefail

echo "=== Agentic Marker Setup ==="
echo

# Check for .NET 10 SDK
if ! command -v dotnet &> /dev/null; then
    echo "dotnet SDK not found. Installing .NET 10 via Homebrew..."
    if ! command -v brew &> /dev/null; then
        echo "Error: Homebrew not found. Install it from https://brew.sh then re-run this script."
        exit 1
    fi
    brew install dotnet@10
else
    DOTNET_VERSION=$(dotnet --version)
    DOTNET_MAJOR=$(echo "$DOTNET_VERSION" | cut -d. -f1)
    if [ "$DOTNET_MAJOR" -lt 10 ]; then
        echo "dotnet $DOTNET_VERSION found but .NET 10+ is required."
        echo "Install via: brew install dotnet@10"
        exit 1
    fi
    echo "OK: dotnet $DOTNET_VERSION"
fi

# Restore NuGet packages
echo
echo "Restoring NuGet packages..."
dotnet restore src/AgenticMarker/AgenticMarker.csproj

# Build
echo
echo "Building..."
dotnet build src/AgenticMarker/AgenticMarker.csproj --no-restore
echo

# Create appsettings.json from example if missing
SETTINGS="src/AgenticMarker/appsettings.json"
if [ ! -f "$SETTINGS" ]; then
    cp src/AgenticMarker/appsettings.example.json "$SETTINGS"
    echo "Created $SETTINGS from template."
fi

# Check for API key
if grep -q '"ApiKey": ""' "$SETTINGS"; then
    echo "WARNING: OpenRouter API key is not set."
    echo "  Edit $SETTINGS and add your key to the ApiKey field."
    echo "  Get a key from https://openrouter.ai/keys"
    echo
fi

echo "=== Setup complete ==="
echo
echo "Usage:"
echo "  dotnet run --project src/AgenticMarker -- <question.doc> <marking-brief.doc> <answer.docx>"
echo
echo "Example:"
echo "  dotnet run --project src/AgenticMarker -- question.doc marking-brief.doc answer.docx"
