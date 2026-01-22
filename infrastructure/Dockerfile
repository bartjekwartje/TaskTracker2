# STAGE 1: The Build Environment
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# --- Step 1: Dependency Layer ---
# Copy the solution file

COPY src/backend/TaskTrackerServer2/TaskTrackerServer2.csproj TaskTrackerServer2/

RUN dotnet restore "TaskTrackerServer2/TaskTrackerServer2.csproj"

COPY src/backend/TaskTrackerServer2/. TaskTrackerServer2/
COPY ["src/html/.", "html_files/"]
COPY ["infrastructure/.", "infra_files/"]

RUN dotnet publish "TaskTrackerServer2/TaskTrackerServer2.csproj" -c Release -o /app/publish

# Manually move the HTML to the publish folder so it's all in one place

RUN cp infra_files/entrypoint.sh /app/publish/
RUN cp html_files/TaskTracker2.html /app/publish/

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

# Install time zone data
RUN apk add --no-cache tzdata krb5-libs

# IMPORTANT: Copy ONLY the published binaries from the 'build' stage
COPY --from=build /app/publish .

# Tell Docker how to start your app
ENTRYPOINT ["dotnet","TaskTrackerServer2.dll"]

