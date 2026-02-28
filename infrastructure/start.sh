#!/bin/bash
set -e

# Start the .NET backend in the background
dotnet /app/TaskTrackerServer2.dll &

# Start nginx in the foreground (keeps the container alive)
nginx -g "daemon off;"
