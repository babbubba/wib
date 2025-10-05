FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
WORKDIR /src
# Restore with cache before copying all sources (project-level restore)
COPY backend/WIB.API/*.csproj backend/WIB.API/
COPY backend/WIB.Application/*.csproj backend/WIB.Application/
COPY backend/WIB.Infrastructure/*.csproj backend/WIB.Infrastructure/
COPY backend/WIB.Domain/*.csproj backend/WIB.Domain/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet restore backend/WIB.API/WIB.API.csproj
# Copy sources and publish
COPY backend ./backend
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet publish backend/WIB.API/WIB.API.csproj -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","WIB.API.dll"]
