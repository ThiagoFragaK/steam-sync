FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/SteamSync.Shared/SteamSync.Shared.csproj", "src/SteamSync.Shared/"]
COPY ["src/SteamSync.Worker/SteamSync.Worker.csproj", "src/SteamSync.Worker/"]
RUN dotnet restore "src/SteamSync.Worker/SteamSync.Worker.csproj"
COPY src/ src/
WORKDIR /src/src/SteamSync.Worker
RUN dotnet build "./SteamSync.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SteamSync.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SteamSync.Worker.dll"]
