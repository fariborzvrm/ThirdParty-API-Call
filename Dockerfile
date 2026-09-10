FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY InquiryService.Api/InquiryService.Api.csproj InquiryService.Api/
RUN dotnet restore InquiryService.Api/InquiryService.Api.csproj

COPY . .
RUN dotnet publish InquiryService.Api/InquiryService.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "InquiryService.Api.dll"]