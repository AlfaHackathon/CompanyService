FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
# Токен (read:packages) для восстановления AlfaHackaton.* из GitHub Packages
ARG GITHUB_TOKEN
WORKDIR /src

# CompanyService ссылается на локальный проект MongoContext, поэтому контекст сборки — корень репо
COPY ["MongoContext/AlfaHackaton.MongoContext/AlfaHackaton.MongoContext.csproj", "MongoContext/AlfaHackaton.MongoContext/"]
COPY ["CompanyService/Api/Api.csproj", "CompanyService/Api/"]
RUN if [ -n "$GITHUB_TOKEN" ]; then \
        dotnet nuget add source "https://nuget.pkg.github.com/AlfaHackathon/index.json" \
            --name github \
            --username AlfaHackathon \
            --password "$GITHUB_TOKEN" \
            --store-password-in-clear-text; \
    fi
RUN dotnet restore "CompanyService/Api/Api.csproj"
COPY . .
WORKDIR "/src/CompanyService/Api"
RUN dotnet build "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Api.dll"]
