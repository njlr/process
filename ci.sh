#!/usr/bin/env bash

set -e

dotnet tool restore
dotnet paket restore
dotnet build
dotnet test
