FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
WORKDIR /src
# Restore with cache before copying all sources (project-level restore)
COPY backend/WIB.Worker/*.csproj backend/WIB.Worker/
COPY backend/WIB.Application/*.csproj backend/WIB.Application/
COPY backend/WIB.Infrastructure/*.csproj backend/WIB.Infrastructure/
COPY backend/WIB.Domain/*.csproj backend/WIB.Domain/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet restore backend/WIB.Worker/WIB.Worker.csproj
# Copy sources and publish
COPY backend ./backend
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet publish backend/WIB.Worker/WIB.Worker.csproj -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet","WIB.Worker.dll"]
