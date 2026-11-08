# المرحلة الأولى: ورشة البناء والترجمة باستخدام قالب التطوير الشامل
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# 0. ملفات الإعدادات المركزية (نسخة الإطار ونسخ الحزم) قبل الـ restore
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]

# 1. ننسخ ملفات المشاريع الموجودة فعليًا (API + Domain + Infrastructure)
COPY ["src/Services/Basket/Basket.API/Basket.API.csproj", "src/Services/Basket/Basket.API/"]
COPY ["src/Services/Basket/Basket.Domain/Basket.Domain.csproj", "src/Services/Basket/Basket.Domain/"]
COPY ["src/Services/Basket/Basket.Application/Basket.Application.csproj", "src/Services/Basket/Basket.Application/"]
COPY ["src/Services/Basket/Basket.Infrastructure/Basket.Infrastructure.csproj", "src/Services/Basket/Basket.Infrastructure/"]

# 2. نحمل المكتبات لمشروع الواجهة البرمجية (وهو سيحمل مكتبات المشاريع الثلاثة المربوطة به تلقائياً)
RUN dotnet restore "src/Services/Basket/Basket.API/Basket.API.csproj"

# 3. ننسخ باقي ملفات الكود البرمجي
COPY . .

# 4. ندخل إلى مجلد الواجهة البرمجية ونخرج النسخة النهائية المترجمة
WORKDIR "/app/src/Services/Basket/Basket.API"
RUN dotnet publish "Basket.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ------------------------------------------------------------------------

# المرحلة الثانية: قالب التشغيل الخفيف النهائي
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

# ننسخ الملفات النهائية المترجمة من ورشة البناء
COPY --from=build /app/publish .

# أمر تشغيل خادم خدمة المعرض
ENTRYPOINT ["dotnet", "Basket.API.dll"]