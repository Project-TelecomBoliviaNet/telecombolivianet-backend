FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/TelecomBoliviaNet.Presentation/TelecomBoliviaNet.Presentation.csproj",   "TelecomBoliviaNet.Presentation/"]
COPY ["src/TelecomBoliviaNet.Application/TelecomBoliviaNet.Application.csproj",     "TelecomBoliviaNet.Application/"]
COPY ["src/TelecomBoliviaNet.Infrastructure/TelecomBoliviaNet.Infrastructure.csproj","TelecomBoliviaNet.Infrastructure/"]
COPY ["src/TelecomBoliviaNet.Domain/TelecomBoliviaNet.Domain.csproj",               "TelecomBoliviaNet.Domain/"]

RUN dotnet restore "TelecomBoliviaNet.Presentation/TelecomBoliviaNet.Presentation.csproj"

COPY src/ .
WORKDIR "/src/TelecomBoliviaNet.Presentation"
RUN dotnet build "TelecomBoliviaNet.Presentation.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TelecomBoliviaNet.Presentation.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish .
RUN mkdir -p wwwroot/uploads/receipts wwwroot/uploads/qr
ENTRYPOINT ["dotnet", "TelecomBoliviaNet.Presentation.dll"]
