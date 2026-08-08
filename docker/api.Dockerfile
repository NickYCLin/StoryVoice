FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source
COPY . .
RUN dotnet restore StoryVoice.sln
RUN dotnet publish src/StoryVoice.Api/StoryVoice.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
RUN apk add --no-cache krb5-libs
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "StoryVoice.Api.dll"]
