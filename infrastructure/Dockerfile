# Build context: parent directory of this repo (DotNetSolutions/)
# Build command (from DotNetSolutions/):
#   docker build -t bartje2/tasktracker:v2.0 -f TaskTrackerSolution2/infrastructure/Dockerfile .

# STAGE 1: SDK build
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY TaskTrackerSolution2/src/backend/TaskTrackerServer2/TaskTrackerServer2.csproj TaskTrackerServer2/
RUN dotnet restore "TaskTrackerServer2/TaskTrackerServer2.csproj"

COPY TaskTrackerSolution2/src/backend/TaskTrackerServer2/. TaskTrackerServer2/
RUN dotnet publish "TaskTrackerServer2/TaskTrackerServer2.csproj" -c Release -o /app/publish

# STAGE 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

RUN apk add --no-cache tzdata krb5-libs nginx curl bash && \
    curl -fsSL https://tailscale.com/install.sh | sh

# Copy published .NET output
COPY --from=build /app/publish .

# Copy HTML files to /app/html (served by nginx)
COPY TaskTrackerSolution2/src/html /app/html

# Configure nginx and startup script
COPY TaskTrackerSolution2/infrastructure/nginx-prod.conf /etc/nginx/http.d/default.conf
COPY TaskTrackerSolution2/infrastructure/start.sh /start.sh
RUN chmod +x /start.sh

ENV ASPNETCORE_URLS=http://+:5000

EXPOSE 80

ENTRYPOINT ["/start.sh"]
