# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1) Copy project file(s) and restore (cacheable)
COPY MyDiscordBot.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore --nologo

# 2) Copy the rest and publish (reuse caches)
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/src/obj \
    dotnet publish -c Release -o /out --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out ./
# If your app doesn't need full ICU:
# ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
CMD ["dotnet", "MyDiscordBot.dll"]