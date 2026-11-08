# المرحلة الأولى: ورشة البناء والترجمة باستخدام قالب التطوير الشامل
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# 0. ملفات الإعدادات المركزية (نسخة الإطار ونسخ الحزم) قبل الـ restore
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]

# 1. ننسخ ملفات المشاريع الموجودة فعليًا (API + Domain + Infrastructure)
COPY ["src/Services/Catalog/Catalog.API/Catalog.API.csproj", "src/Services/Catalog/Catalog.API/"]
COPY ["src/Services/Catalog/Catalog.Domain/Catalog.Domain.csproj", "src/Services/Catalog/Catalog.Domain/"]
COPY ["src/Services/Catalog/Catalog.Infrastructure/Catalog.Infrastructure.csproj", "src/Services/Catalog/Catalog.Infrastructure/"]

# 2. نحمل المكتبات لمشروع الواجهة البرمجية (وهو سيحمل مكتبات المشاريع الثلاثة المربوطة به تلقائياً)
RUN dotnet restore "src/Services/Catalog/Catalog.API/Catalog.API.csproj"

# 3. ننسخ باقي ملفات الكود البرمجي
COPY . .

# 4. ندخل إلى مجلد الواجهة البرمجية ونخرج النسخة النهائية المترجمة
WORKDIR "/app/src/Services/Catalog/Catalog.API"
RUN dotnet publish "Catalog.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ------------------------------------------------------------------------

# المرحلة الثانية: قالب التشغيل الخفيف النهائي
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

# ننسخ الملفات النهائية المترجمة من ورشة البناء
COPY --from=build /app/publish .

# أمر تشغيل خادم خدمة المعرض
ENTRYPOINT ["dotnet", "Catalog.API.dll"]