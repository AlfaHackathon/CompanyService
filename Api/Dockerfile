FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
# Токен (read:packages) для восстановления AlfaHackaton.* из GitHub Packages
ARG GITHUB_TOKEN
WORKDIR /src

# company-service ссылается на локальный проект MongoContext, поэтому контекст сборки — корень репо
COPY ["MongoContext/AlfaHackaton.MongoContext/AlfaHackaton.MongoContext.csproj", "MongoContext/AlfaHackaton.MongoContext/"]
COPY ["company-service/CompanyService/CompanyService.csproj", "company-service/CompanyService/"]
RUN if [ -n "$GITHUB_TOKEN" ]; then \
        dotnet nuget add source "https://nuget.pkg.github.com/AlfaHackathon/index.json" \
            --name github \
            --username AlfaHackathon \
            --password "$GITHUB_TOKEN" \
            --store-password-in-clear-text; \
    fi
RUN dotnet restore "company-service/CompanyService/CompanyService.csproj"
COPY . .
WORKDIR "/src/company-service/CompanyService"
RUN dotnet build "./CompanyService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CompanyService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CompanyService.dll"]
