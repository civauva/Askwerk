# SPDX-License-Identifier: Apache-2.0
# Build: docker build -t askwerk-sql-sync .
# Run:   docker run --rm -e ASKWERK_BASE_URL=… -e ASKWERK_API_KEY=… -e SQLSERVER_CONNECTION_STRING=… askwerk-sql-sync
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AskwerkSqlSync.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "AskwerkSqlSync.dll"]
