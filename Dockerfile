# Use Microsoft's official .NET SDK to build and run
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build

# Create app directory inside container
WORKDIR /app

# Copy everything into /app
COPY . .

# Restore dependencies and build
RUN dotnet restore
RUN dotnet publish -c Release -o out

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS runtime
WORKDIR /app

# Copy built output
COPY --from=build /app/out .

# Run the app
ENTRYPOINT ["dotnet", "MyDiscordBot.dll"]