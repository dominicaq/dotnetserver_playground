#!/bin/bash

echo "Building GameNetworking DLL..."

cd GameNetworking || {
    echo "Error: GameNetworking directory not found!"
    exit 1
}

echo "Building project..."
dotnet build -c Release

if [ $? -ne 0 ]; then
    echo "Error: Build failed!"
    exit 1
fi

if [ -z "$1" ]; then
    EXPORT_PATH="../"
    echo "Copying DLL to root..."
else
    EXPORT_PATH="../$1"
    echo "Copying DLL to $EXPORT_PATH..."
fi

cp bin/Release/*/GameNetworking.dll "$EXPORT_PATH"

if [ $? -ne 0 ]; then
    echo "Error: Failed to copy DLL to $EXPORT_PATH!"
    exit 1
fi

echo "Done!"
