# سياق المشروع: الهيكل والبنية التحتية

ملف مرجعي سريع يشرح شكل المشروع وطريقة تشغيله.
لو بتقرأ الكود لأول مرة، أو هتدّي المشروع لحد تاني (أو لـ AI agent)، ابدأ من هنا.

> آخر تحديث: 2026-11-08، على فرع `build/upgrade-dotnet-10`

---

## 1. المشروع في سطرين

متجر إلكتروني مبني بمعمارية **Microservices** على **ASP.NET Core (.NET 10)**.
كل خدمة مستقلة: لها API خاص بيها، وقاعدة بيانات **SQL Server** خاصة بيها (Database per Service)، وكونتينر خاص بيها.

| الخدمة | المسؤولية | الجداول الحالية | بورت الـ API | بورت الـ DB على الجهاز |
|---|---|---|---|---|
| **Catalog** | عرض المنتجات | `Users`, `Products` | `5299` | `1436` |
| **Basket** | سلة المشتريات | `Users`, `ShoppingCarts`, `ShoppingCartItems` | `5260` | `1435` |
| **Ordering** | الطلبات والفواتير | `Users` (لسه في البداية) | `5257` | `1437` |

Swagger لكل خدمة على `http://localhost:<port>/swagger`.

---

## 2. شكل الملفات

```
ECommerceMicroservices/
├── ECommerceMicroservices.sln
├── Directory.Build.props        ← TargetFramework (net10.0) + Nullable + ImplicitUsings لكل المشاريع
├── Directory.Packages.props     ← نسخ كل حزم NuGet في مكان واحد (Central Package Management)
├── .config/dotnet-tools.json    ← أدوات محلية: dotnet-ef 10.0.14 و csharpier 1.3.0
├── docker-compose.yml           ← 6 كونتينرات: 3 APIs + 3 SQL Server
├── .dockerignore / .gitignore
├── docs/PROJECT_CONTEXT.md      ← الملف ده
└── src/Services/
    ├── Catalog/
    │   ├── .env / .env.example
    │   ├── Catalog.API/            Controllers, Program.cs, Dockerfile
    │   ├── Catalog.Domain/         Entities
    │   └── Catalog.Infrastructure/ Data/CatalogDbContext.cs + Migrations
    ├── Basket/
    │   ├── .env / .env.example
    │   ├── Basket.API/
    │   ├── Basket.Application/     DTOs (مثل AddItemRequestDTO)
    │   ├── Basket.Domain/          Entities + Contracts (ICatalogService)
    │   └── Basket.Infrastructure/  BasketDbContext + Migrations + Services/CatalogService.cs
    └── Ordering/
        ├── .env / .env.example
        ├── Ordering.API/
        ├── Ordering.Domain/
        └── Ordering.Infrastructure/
```

### الطبقات جوه كل خدمة (Clean Architecture مبسطة)

```
API  ──►  Infrastructure  ──►  Domain
 └──────►  Application (Basket بس) ──► Domain
```

- **Domain**: الكيانات (Entities) والعقود (Interfaces). مش بيعتمد على أي حاجة.
- **Application**: الـ DTOs. موجودة في Basket بس لحد دلوقتي.
- **Infrastructure**: EF Core و `{Service}DbContext` (زي `CatalogDbContext`) والـ Migrations، والتنفيذ الفعلي للعقود (زي `CatalogService`).
- **API**: الـ Controllers و `Program.cs` (تسجيل الخدمات في الـ DI، والـ Swagger، والـ Migration التلقائي).

---

## 3. التواصل بين الخدمات

النقطة الوحيدة اللي فيها خدمة بتكلم خدمة تانية لحد دلوقتي:

```
Client ──POST /api/basket/items──► Basket.API
                                      │  GET /api/products/{id}   (HTTP، متزامن)
                                      ▼
                                   Catalog.API ──► catalog-db
                                      │
                    Basket بياخد الاسم والسعر الحقيقي من Catalog
                                      ▼
                                   basket-db  (يحفظ نسخة من ProductName و Price)
```

- العقد: `ICatalogService` في `Basket.Domain/Contracts`، والتنفيذ: `CatalogService` في `Basket.Infrastructure/Services`.
- متسجل بـ `AddHttpClient<ICatalogService, CatalogService>` ومعاه `AddStandardResilienceHandler()`، يعني فيه retry و timeout و circuit breaker جاهزين.
- العنوان بييجي من `CATALOG_URL` في `src/Services/Basket/.env`. جوه Docker قيمته `http://ecommerce-catalog-api:8080`.
- لو Catalog واقع، Basket بيرجّع **503**. ولو المنتج مش موجود بيرجّع **404**.
- العميل مبيبعتش السعر ولا الاسم، بيبعت `UserName` و `ProductId` و `Quantity` بس. ده بيمنع إن حد يتلاعب في السعر.
- مفيش Foreign Keys بين الخدمات. `ProductId` في Basket مجرد رقم.

---

## 4. البنية التحتية (Docker)

### التشغيل

```bash
docker compose up -d --build   # تشغيل الكل
docker compose down            # إيقاف مع الاحتفاظ بالبيانات
docker compose down -v         # إيقاف ومسح الـ volumes (البيانات)
```

### الكونتينرات

| Service | Container | البورت على الجهاز ← جوه الكونتينر | Volume |
|---|---|---|---|
| `catalog-db` | `ecommerce-catalog-db` | `127.0.0.1:1436 → 1433` | `ecommerce-catalog-db` |
| `catalog-api` | `ecommerce-catalog-api` | `127.0.0.1:5299 → 8080` | — |
| `basket-db` | `ecommerce-basket-db` | `127.0.0.1:1435 → 1433` | `ecommerce-basket-db` |
| `basket-api` | `ecommerce-basket-api` | `127.0.0.1:5260 → 8080` | — |
| `ordering-db` | `ecommerce-ordering-db` | `127.0.0.1:1437 → 1433` | `ecommerce-ordering-db` |
| `ordering-api` | `ecommerce-ordering-api` | `127.0.0.1:5257 → 8080` | — |

- كل الكونتينرات على شبكة bridge واحدة اسمها `ecommerce`.
- البورتات مربوطة على `127.0.0.1` بس، يعني مش مفتوحة على الشبكة الخارجية.
- الـ compose بيستخدم YAML anchors (`x-sqlserver` و `x-api` و `x-api-env` و `x-logging`) عشان الإعدادات المتكررة تتكتب مرة واحدة.

### قواعد البيانات (`x-sqlserver`)

- الصورة: `mcr.microsoft.com/mssql/server:2022-latest` (Developer edition، و `linux/amd64`).
- الـ entrypoint بيتأكد إن `DB_PASSWORD` موجود قبل التشغيل، وبعدين يستخدمه كـ `MSSQL_SA_PASSWORD`.
- حدود الموارد: `1 CPU` و `2560m` رام للكونتينر. SQL Server بيرفض يشتغل بأقل من 2GB، فالسقف لازم يكون أعلى من كده. وفي نفس الوقت `MSSQL_MEMORY_LIMIT_MB=512` بيحدد استهلاكه الفعلي، عشان التلات قواعد يشتغلوا على لابتوب.
- الـ healthcheck بيدور على جملة `ready for client connections` في الـ errorlog.

### الـ APIs (`x-api`)

- كل API بيستنى لحد ما قاعدته تبقى healthy (`depends_on: condition: service_healthy`). بعد كده الـ entrypoint بيجرب يفتح اتصال TCP بالقاعدة لحد 30 مرة، كل مرة بعد ثانيتين.
- الـ entrypoint بيبني `ConnectionStrings__DefaultConnection` من متغيرات الـ `.env` وبعدين يشغل `dotnet <DLL_NAME>`.
- جوه Docker: `DB_HOST` هو اسم خدمة القاعدة (مثلاً `catalog-db`) و `DB_PORT=1433`. القيم دي بتغطي على اللي في الـ `.env`.
- حدود الموارد: `0.75 CPU` و `512m` رام، ومعاهم `no-new-privileges`.
- الـ logs بنظام `json-file` بحد أقصى 3 ملفات، كل ملف 10MB.

### الـ Dockerfile (نفس الشكل في التلات خدمات)

Multi-stage build:
1. **build** (`sdk:10.0`): ينسخ `Directory.Build.props` و `Directory.Packages.props` وملفات الـ `.csproj` الأول، وبعدين يعمل `restore` عشان طبقة الـ restore تتعمل لها cache. بعد كده ينسخ باقي الكود ويعمل `publish`.
2. **final** (`aspnet:10.0`): ينسخ ناتج الـ publish بس، ويفتح بورت `8080`.

الـ build context هو جذر الريبو (`context: .`)، عشان الـ Dockerfile يقدر يوصل لملفات الـ props المركزية.

---

## 5. الإعدادات والأسرار

- كل خدمة ليها `.env` خاص بيها في `src/Services/<Service>/.env`، وده **مش متسجل في git**. المتسجل هو `.env.example` بس.
- المفاتيح: `DB_NAME`, `DB_HOST`, `DB_PORT`, `DB_USER`, `DB_PASSWORD`, `DB_SCHEMA`، وفي Basket كمان `CATALOG_URL`.
- `.dockerignore` بيمنع نسخ ملفات `.env` جوه الصور. الأسرار بتوصل للكونتينر وقت التشغيل عن طريق `env_file`.
- لما تشغل Basket محليًا بـ `dotnet run`، الدالة `LoadLocalDotEnv()` في `Program.cs` بتدور على `.env` في المجلد الحالي ولحد 5 مستويات فوقه، وتبني منه الـ connection string.

## 6. البناء والحزم

- **.NET 10** و **EF Core 10.0.14**. النسخة متحددة في `Directory.Build.props` و `Directory.Packages.props`.
- الـ `.csproj` بيكتب اسم الحزمة بس من غير نسخة. النسخ كلها في `Directory.Packages.props`.
- `CentralPackageTransitivePinningEnabled=true`، ومعاه pin لـ `System.Security.Cryptography.Pkcs` بسبب ثغرة جاية مع SqlClient.
- تحذيرات الثغرات العالية والحرجة (`NU1903` و `NU1904`) بتوقف الـ build.
- الحزم الأساسية: EF Core SqlServer و Swashbuckle 6.6.2 و `Microsoft.Extensions.Http.Resilience` (في Basket بس).
- الـ Migrations بتتطبق تلقائيًا أول ما الخدمة تشتغل (`context.Database.MigrateAsync()` في `Program.cs`).
- لإضافة migration، مثال على Catalog:
  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> \
    -p src/Services/Catalog/Catalog.Infrastructure \
    -s src/Services/Catalog/Catalog.API \
    -o Data/Migrations
  ```

---

## 7. ملاحظات ونواقص معروفة

- **Catalog و Ordering مفيهمش `LoadLocalDotEnv()`**. عشان كده `dotnet run` محليًا بيستخدم الـ connection string اللي في `appsettings.json`، وده فيه placeholders زي `${DB_NAME}` مش بتتعوض. في Docker الموضوع شغال عادي لأن الـ entrypoint بيبني الـ connection string.
- `CATALOG_URL` مش موجود في `Basket/.env.example`.
- كيان `User` متكرر بنفس الشكل في التلات خدمات، ومعاه `UsersController` في كل خدمة. ده كود تجريبي، والمفروض بعدين يبقى في خدمة Identity منفصلة.
- فيه ملفات `Class1.cs` فاضية من قوالب المشاريع لسه موجودة.
- ملف `Basket.API/Properties/Entities/User.cs` في مكان غلط (جوه `Properties`).
- لو الـ Migration فشل وقت التشغيل، الخطأ بيتسجل في الـ log بس والخدمة بتكمل تشتغل.
- `UseHttpsRedirection` متعطل، ومفيش Authentication.
- مفيش API Gateway ولا Message Broker ولا Tests ولا CI لحد دلوقتي.
- الخطة اللي في `README.md` بتقترح PostgreSQL لـ Catalog و Redis لـ Basket وخدمة Discount.gRPC. التنفيذ الحالي كله SQL Server.
