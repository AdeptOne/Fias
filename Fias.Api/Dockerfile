# syntax=docker/dockerfile:1

# ============================================================================
#  Многоэтапная сборка (multi-stage build) для Fias.Api (.NET 8 Web API)
#  Цель: тонкий рантайм-образ, non-root, с поддержкой ru_RU.UTF-8 и тюнингом GC.
# ============================================================================

# ----------------------------------------------------------------------------
#  ЭТАП 1. restore — только восстановление NuGet-пакетов.
#  Вынесен отдельно, чтобы слой с пакетами кэшировался и не пересобирался,
#  пока не меняются .csproj-файлы (а не весь исходный код).
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src

# Сначала копируем ТОЛЬКО файлы проектов (граф зависимостей решения).
# Это ключ к кэшированию: меняется код -> этот слой берётся из кэша.
COPY ["Fias.Api/Fias.Api.csproj", "Fias.Api/"]
COPY ["Fias.Application/Fias.Application.csproj", "Fias.Application/"]
COPY ["Fias.Domain/Fias.Domain.csproj", "Fias.Domain/"]
COPY ["Fias.Infrastructure/Fias.Infrastructure.csproj", "Fias.Infrastructure/"]

# Восстанавливаем зависимости для стартового проекта (тянет и зависимые проекты).
# --mount=cache держит глобальный кэш NuGet между сборками (BuildKit) -> быстрее.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "Fias.Api/Fias.Api.csproj"

# ----------------------------------------------------------------------------
#  ЭТАП 2. publish — компиляция и публикация в Release.
# ----------------------------------------------------------------------------
FROM restore AS publish
ARG BUILD_CONFIGURATION=Release

# Теперь копируем весь исходный код.
COPY . .

# publish сразу (без отдельного build) — короче и быстрее.
#   -c Release            — оптимизированная сборка.
#   --no-restore          — пакеты уже восстановлены на этапе restore.
#   /p:UseAppHost=false   — не генерировать нативный лаунчер, запуск через `dotnet Fias.Api.dll`.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "Fias.Api/Fias.Api.csproj" \
        -c $BUILD_CONFIGURATION \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ----------------------------------------------------------------------------
#  ЭТАП 3. final — рантайм-образ.
#  Берём Debian-вариант aspnet:8.0 (НЕ alpine) намеренно: для корректной локали
#  ru_RU.UTF-8 нужен glibc + пакет locales, которого в musl/alpine нет «из коробки».
#  Раз мы плотно работаем с русским текстом — это надёжнее, чем alpine.
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

# --- Локаль ru_RU.UTF-8 ---------------------------------------------------
# Ставим пакет locales и генерируем русскую локаль (нужен root, делаем до смены USER).
RUN apt-get update \
    && apt-get install -y --no-install-recommends locales \
    && sed -i 's/^# *\(ru_RU.UTF-8\)/\1/' /etc/locale.gen \
    && locale-gen ru_RU.UTF-8 \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Прокидываем локаль в окружение процесса (форматы дат/чисел, сортировки).
ENV LANG=ru_RU.UTF-8 \
    LANGUAGE=ru_RU:ru \
    LC_ALL=ru_RU.UTF-8

# --- Глобализация .NET ----------------------------------------------------
# Явно НЕ включаем invariant-режим, чтобы CultureInfo("ru-RU") работала корректно
# (без этого сравнение/регистр/сортировка русских строк деградируют до ordinal).
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# --- Тюнинг сборщика мусора (GC) под контейнер ----------------------------
ENV \
    # Server GC: многопоточный GC, выше пропускная способность для веб-нагрузки.
    DOTNET_gcServer=1 \
    # Конкурентный (фоновый) GC: меньше пауз stop-the-world.
    DOTNET_gcConcurrent=1 \
    # DATAS (.NET 8): GC сам ужимает heap под реальную нагрузку —
    # экономит RAM в контейнере, не теряя плюсов Server GC.
    DOTNET_GCDynamicAdaptationMode=1
# Рантайм .NET сам читает cgroup-лимиты контейнера (CPU/Memory) и подстраивает
# число GC-heap'ов и пулы потоков — отдельные DOTNET_PROCESSOR_COUNT не нужны.

# --- Сеть -----------------------------------------------------------------
# Kestrel слушает 8080 (HTTP) на всех интерфейсах внутри контейнера.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

WORKDIR /app
COPY --from=publish /app/publish .

# --- Безопасность: non-root -----------------------------------------------
# В образах .NET 8 уже есть непривилегированный пользователь 'app' (UID в $APP_UID).
# Переключаемся на него ПОСЛЕ установки локали и копирования файлов.
USER $APP_UID

ENTRYPOINT ["dotnet", "Fias.Api.dll"]
