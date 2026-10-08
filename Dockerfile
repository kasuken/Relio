# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for caching restore layers
COPY Relio.slnx .
COPY global.json .
COPY Directory.Build.props .
COPY Relio.Domain/Relio.Domain.csproj Relio.Domain/
COPY Relio.Application/Relio.Application.csproj Relio.Application/
COPY Relio.Data/Relio.Data.csproj Relio.Data/
COPY Relio.Web/Relio.Web.csproj Relio.Web/

# Restore dependencies
RUN dotnet restore Relio.Web/Relio.Web.csproj

# Copy remaining source code
COPY Relio.Domain/ Relio.Domain/
COPY Relio.Application/ Relio.Application/
COPY Relio.Data/ Relio.Data/
COPY Relio.Web/ Relio.Web/
COPY CHANGELOG.md .

# Publish Relio.Web
WORKDIR /src/Relio.Web
RUN dotnet publish Relio.Web.csproj --configuration Release --output /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create a non-root user (appuser, UID 1000) for security
RUN useradd -m -u 1000 -s /bin/bash appuser

# Copy published application
COPY --from=build /app/publish .

# Create persistent data directory with non-root ownership
RUN mkdir -p /var/opt/relio /var/opt/relio/keys && chown -R appuser:appuser /var/opt/relio /app

# Switch to non-root user
USER appuser

# Expose HTTP port
EXPOSE 8080

# Environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check against /health/live. The aspnet image has no curl or wget, so this speaks
# HTTP over bash's /dev/tcp. The start period covers the migrations run at startup.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet", "Relio.Web.dll"]
