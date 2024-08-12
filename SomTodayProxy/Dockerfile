FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
MAINTAINER Matt ter Steege <matttersteege@gmail.com>
WORKDIR /app
EXPOSE 80
EXPOSE 443

ENV DB_NAME=zermos-database
ENV DB_USER=root
ENV DB_PASSWORD=uSmENuQq
ENV DB_HOST=192.168.178.53
ENV DB_PORT=3306
ENV ZERMOS-WEB-VERSION=0.6.0.1


FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Zermos-Web/Zermos-Web.csproj", "Zermos-Web/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
RUN dotnet restore "Zermos-Web/Zermos-Web.csproj"
COPY . .
WORKDIR "/src/Zermos-Web"
RUN dotnet build "Zermos-Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Zermos-Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Zermos-Web.dll"]
VOLUME ["/dataprotection"]

#hit the [Zermos-Web/Dockerfile - orange pi] run button and all will be handled automatically