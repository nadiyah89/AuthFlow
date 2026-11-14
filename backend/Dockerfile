# ==============================================================================
# Multi-stage Dockerfile for AuthFlow Backend (.NET 10 Web API)
# Build Context: backend
# ==============================================================================

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project file and restore NuGet dependencies
COPY src/AuthFlow.Api/AuthFlow.Api.csproj src/AuthFlow.Api/
RUN dotnet restore src/AuthFlow.Api/AuthFlow.Api.csproj

# Copy remaining source code and publish Release build
COPY src/AuthFlow.Api/ src/AuthFlow.Api/
WORKDIR /app/src/AuthFlow.Api
RUN dotnet publish AuthFlow.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

# Copy published artifacts from build stage
COPY --from=build /app/publish .

# Start the ASP.NET Core application
ENTRYPOINT ["dotnet", "AuthFlow.Api.dll"]
