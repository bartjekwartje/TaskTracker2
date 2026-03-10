# Build context: repo root (TaskTrackerSolution2/)
# Build command (from repo root):
#   docker build -t bartje2/tasktracker2:v1.0 -f infrastructure/Dockerfile .

# STAGE 1: SDK build
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY src/backend/TaskTrackerServer2/TaskTrackerServer2.csproj TaskTrackerServer2/
RUN dotnet restore "TaskTrackerServer2/TaskTrackerServer2.csproj"

COPY src/backend/TaskTrackerServer2/. TaskTrackerServer2/
RUN dotnet publish "TaskTrackerServer2/TaskTrackerServer2.csproj" -c Release -o /app/publish

# STAGE 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

RUN apk add --no-cache tzdata krb5-libs nginx bash tailscale

# Copy published .NET output
COPY --from=build /app/publish .

# Copy HTML files to /app/html (served by nginx)
COPY src/html /app/html

# Configure nginx and startup script
COPY infrastructure/nginx-prod.conf /etc/nginx/http.d/default.conf
COPY infrastructure/start.sh /start.sh
RUN chmod +x /start.sh

ENV ASPNETCORE_URLS=http://+:5000

EXPOSE 80

ENTRYPOINT ["/start.sh"]
