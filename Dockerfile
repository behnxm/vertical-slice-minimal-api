# Multi-stage hardened build for TaskTracker.Api
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy directory build properties, package versions, and API project
COPY Directory.Build.props Directory.Packages.props ./
COPY src/TaskTracker.Api/TaskTracker.Api.csproj src/TaskTracker.Api/

# Restore dependencies
RUN dotnet restore src/TaskTracker.Api/TaskTracker.Api.csproj

# Copy API source code and publish release
COPY src/TaskTracker.Api/ src/TaskTracker.Api/
WORKDIR /src/src/TaskTracker.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage using Ubuntu Noble Chiseled (minimal attack surface, non-root)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app

# Run as built-in non-root user (UID 1654 in chiseled images)
USER $APP_UID

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "TaskTracker.Api.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "TaskTracker.Api.dll"]
