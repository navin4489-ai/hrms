FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Wintech.Hrms.Api.csproj ./
RUN dotnet restore Wintech.Hrms.Api.csproj

COPY . .
# Cache-bust token: bump to force Render to rebuild from fresh source (avoids stale COPY layer).
ARG BUILD_REV=2026-10-25-chat-enable-2
RUN echo "build rev: ${BUILD_REV}" && dotnet publish Wintech.Hrms.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "Wintech.Hrms.Api.dll"]
