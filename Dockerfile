# Based on the Microsoft template:
# https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images?view=aspnetcore-10.0
# Images: https://hub.docker.com/_/microsoft-dotnet

# ////////////// #
# Build stage    #
# ////////////// #
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project and lock file, then restore as a distinct layer (cached unless deps change).
COPY H3xBoardServer.csproj packages.lock.json ./
RUN dotnet restore --locked-mode

# Copy the rest of the source and publish.
COPY . .
RUN dotnet publish -c Release --no-restore -o /app

# ////////////// #
# Runtime stage  #
# ////////////// #
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# SQLite and uploaded files live in a dedicated, writable directory so they can be backed by a
# volume and survive container restarts. Owned by the non-root app user the base image ships with.
ENV Database__ConnectionString="Data Source=/data/h3xboard.db"
ENV Storage__FileSystem__RootPath="/data/files"
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
VOLUME /data

# Kestrel listens on 8080 by default in the .NET container images (ASPNETCORE_HTTP_PORTS=8080).
EXPOSE 8080

# Run as the non-root user provided by the base image.
USER $APP_UID

ENTRYPOINT ["dotnet", "H3xBoardServer.dll"]
