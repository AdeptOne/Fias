FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Fias.Service.Updater/Fias.Service.Updater.csproj", "Fias.Service.Updater/"]
COPY ["Fias.Application/Fias.Application.csproj", "Fias.Application/"]
COPY ["Fias.Domain/Fias.Domain.csproj", "Fias.Domain/"]
RUN dotnet restore "Fias.Service.Updater/Fias.Service.Updater.csproj"
COPY . .
WORKDIR "/src/Fias.Service.Updater"
RUN dotnet build "./Fias.Service.Updater.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Fias.Service.Updater.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Fias.Service.Updater.dll"]
