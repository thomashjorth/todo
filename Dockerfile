# The image the NAS runs. Built and probed only in CI: the development machine's Docker runs Windows
# containers, so a Linux image can be neither built nor run there (measured 2026-10-26).

# 1. Angular, built into src/Todo.Host/wwwroot as angular.json's outputPath says. The lock file
#    first, so a change of source alone does not reinstall every package.
FROM node:22-bookworm-slim AS web
WORKDIR /src/src/Todo.Web
COPY src/Todo.Web/package.json src/Todo.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/Todo.Web/ ./
RUN npm run build

# 2. The server. contracts/ is needed because the host embeds openapi.yaml for its documentation
#    page, and the frontend is embedded too, so wwwroot has to be in place before the publish.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY contracts/ contracts/
COPY src/Todo.Contracts/ src/Todo.Contracts/
COPY src/Todo.Core/ src/Todo.Core/
COPY src/Todo.Host/ src/Todo.Host/
# The exe's ApplicationIcon, which Todo.Host.csproj reads from the Angular project's public folder.
COPY src/Todo.Web/public/favicon.ico src/Todo.Web/public/favicon.ico
COPY --from=web /src/src/Todo.Host/wwwroot/ src/Todo.Host/wwwroot/
RUN dotnet publish src/Todo.Host -c Release -o /app

# 3. What runs.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL org.opencontainers.image.source=https://github.com/thomashjorth/todo
WORKDIR /app
COPY --from=server /app ./

# ASPNETCORE_URLS and not the image's own ASPNETCORE_HTTP_PORTS: TodoHost.Build binds loopback on a
# random port when the configuration key "urls" is missing, and HTTP_PORTS is another key - so the
# container would listen inside itself only, healthy and unreachable. URLS becomes "urls".
#
# TZ, because SystemClock.Today is DateTime.Now: in UTC "today" would be yesterday between 00 and 02,
# and overdue and deferred with it.
#
# LANG, because .NET takes its current culture from it on Linux and the image sets none, which means
# the invariant culture: the task list's tags are ordered with the current culture, and Æ, Ø and Å
# would sort among A and O on the NAS while they sort after Z on the desktop.
ENV ASPNETCORE_URLS=http://+:8080 \
    TZ=Europe/Copenhagen \
    LANG=da_DK.UTF-8 \
    Data__Path=/data/todo.db

# The database, its backups and the keys that encrypt the login cookie all live here, which is what
# lets a pulled image start where the last one stopped - logged in included.
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Todo.Host.dll"]
