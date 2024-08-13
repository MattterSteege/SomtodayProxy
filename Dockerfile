FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
MAINTAINER Matt ter Steege <matttersteege@gmail.com>
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["SomtodayProxy/SomtodayProxy.csproj", "SomtodayProxy/"]
RUN dotnet restore "SomtodayProxy/SomtodayProxy.csproj"
COPY . .
WORKDIR "/src/SomtodayProxy"
RUN dotnet build "SomtodayProxy.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SomtodayProxy.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SomtodayProxy.dll"]
