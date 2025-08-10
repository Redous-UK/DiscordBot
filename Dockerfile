# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy the .csproj file and restore dependencies
COPY MyDiscordBot.csproj ./
RUN dotnet restore

# Copy the rest of the source code
COPY . ./

# Publish the application to /out folder
RUN dotnet publish -c Release -o out

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy the published app from the build stage
COPY --from=build /app/out .

# Run the bot
ENTRYPOINT ["dotnet", "MyDiscordBot.dll"]