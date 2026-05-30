FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Fias.Eval/Fias.Eval.csproj", "Fias.Eval/"]
COPY ["Fias.Infrastructure/Fias.Infrastructure.csproj", "Fias.Infrastructure/"]
COPY ["Fias.Application/Fias.Application.csproj", "Fias.Application/"]
COPY ["Fias.Domain/Fias.Domain.csproj", "Fias.Domain/"]
RUN dotnet restore "Fias.Eval/Fias.Eval.csproj"
COPY . .
WORKDIR "/src/Fias.Eval"
RUN dotnet build "./Fias.Eval.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Fias.Eval.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# ENTRYPOINT без аргументов — флаги (--region-count и т.п.) дописывает `docker compose run`.
ENTRYPOINT ["dotnet", "Fias.Eval.dll"]
