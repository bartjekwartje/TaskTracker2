#!/bin/bash
set -e

# Start Tailscale daemon in background
tailscaled --state=/var/lib/tailscale/tailscaled.state &
sleep 2

# Connect to Tailscale network
tailscale up --authkey="${TAILSCALE_AUTHKEY}" --hostname="tasktracker2"

# Start the .NET backend in the background
dotnet /app/TaskTrackerServer2.dll &

# Start nginx in the foreground (keeps the container alive)
nginx -g "daemon off;"
