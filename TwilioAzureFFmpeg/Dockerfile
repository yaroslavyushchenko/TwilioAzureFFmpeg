FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["TwilioAzureFFmpeg/TwilioAzureFFmpeg.csproj", "TwilioAzureFFmpeg/"]
RUN dotnet restore "TwilioAzureFFmpeg/TwilioAzureFFmpeg.csproj"
COPY . .
WORKDIR "/src/TwilioAzureFFmpeg"
RUN dotnet build "./TwilioAzureFFmpeg.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TwilioAzureFFmpeg.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TwilioAzureFFmpeg.dll"]
