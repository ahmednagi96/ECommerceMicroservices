# سياق المشروع: الهيكل والبنية التحتية

ملف مرجعي سريع يشرح شكل المشروع وطريقة تشغيله.
لو بتقرأ الكود لأول مرة، أو هتدّي المشروع لحد تاني (أو لـ AI agent)، ابدأ من هنا.
وخطة الكورس والتقدم في `docs/LEARNING_PROGRESS.md`، والقرارات في `docs/adr/`.

> آخر تحديث: 2026-11-20 · بيوصف `main` بعد الموديول 6 (PR #9) + تغييرات الموديول 7 (فرع `module_7`، المفروض يندمج).

---

## 1. المشروع في سطرين

متجر إلكتروني مبني بمعمارية **Microservices** على **ASP.NET Core (.NET 10)**.
كل خدمة مستقلة: لها API خاص بيها، وقاعدة بيانات **SQL Server** خاصة بيها (Database per Service)، وكونتينر خاص بيها. والخدمات بتتكلم بـ HTTP (متزامن) وبـ **RabbitMQ + Wolverine** (غير متزامن).

| الخدمة | المسؤولية | الجداول | بورت الـ API | بورت الـ DB على الجهاز |
|---|---|---|---|---|
| **Catalog** | عرض المنتجات | `Users`, `Products` | `5299` | `1436` |
| **Basket** | سلة المشتريات والـ Checkout | `Users`, `ShoppingCarts`, `ShoppingCartItems` + Schema `wolverine` (Outbox) | `5260` | `1435` |
| **Ordering** | الطلبات (DDD) | `Users`, `Orders`, `OrderItems` + Schema `wolverine` (Inbox) | `5257-5259` (لحد 3 نسخ) | `1437` |
| **RabbitMQ** | وسيط الرسائل | — | `5672` (AMQP) و `15672` (اللوحة) | — |

Swagger لكل خدمة على `http://localhost:<port>/swagger`، والصحة على `/health/live` و `/health/ready`.

---

## 2. شكل الملفات

```
ECommerceMicroservices/
├── ECommerceMicroservices.sln
├── Directory.Build.props        ← TargetFramework (net10.0) + Nullable + ImplicitUsings لكل المشاريع
├── Directory.Packages.props     ← نسخ كل حزم NuGet في مكان واحد (Central Package Management)
├── .config/dotnet-tools.json    ← أدوات محلية: dotnet-ef و csharpier
├── docker-compose.yml           ← 8 كونتينرات: 3 APIs + 3 SQL Server + RabbitMQ (+ نسخ ordering-api)
├── rabbitmq.env / rabbitmq.env.example
├── demos/                       ← RabbitDemo.Publisher و RabbitDemo.Consumer (الموديول 6.3، RabbitMQ.Client الخام)
├── docs/
│   ├── PROJECT_CONTEXT.md       ← الملف ده
│   ├── LEARNING_PROGRESS.md     ← خطة الكورس والتقدم
│   ├── adr/                     ← ADR-003 (Wolverine) و ADR-004 (Outbox/Inbox)
│   └── notes/                   ← ملفات ملاحظات كل موديول
└── src/
    ├── BuildingBlocks/
    │   ├── BuildingBlocks/            ServiceDefaults (Serilog + ProblemDetails + Health + Fail-fast migration)، LocalEnvLoader، ErrorHandling
    │   ├── BuildingBlocks.Contracts/  IntegrationEvent + BasketCheckedOutIntegrationEvent (من غير أي Package)
    │   └── BuildingBlocks.Messaging/  باكدجات Wolverine + IEventBus المكتوب بإيدنا (مرجع تعليمي من 6.4، مش مستخدم)
    └── Services/
        ├── Catalog/   (.env، API، Domain، Infrastructure)
        ├── Basket/    (.env، API، Application، Domain، Infrastructure)
        └── Ordering/  (.env، API، Application، Domain، Infrastructure)
```

### الطبقات جوه كل خدمة (Clean Architecture مبسطة)

```
API  ──►  Infrastructure  ──►  Domain
 └──────►  Application ──► Domain
```

- **Domain**: الكيانات والعقود. في Ordering فيه DDD كامل: `Order` (Aggregate) و `OrderItem` و `Money` و `Address` و `OrderId` و Domain Events و `DomainException`.
- **Application**: الـ DTOs (Basket)، و Domain Event Handlers (Ordering).
- **Infrastructure**: EF Core و `{Service}DbContext` والـ Migrations (`Data/Migrations`)، والتنفيذ الفعلي للعقود (`CatalogService`، `OrderRepository`، `DomainEventDispatcher`).
- **API**: الـ Controllers و `Program.cs`، والـ Integration Event Handlers في Ordering (`IntegrationEvents/`).

---

## 3. التواصل بين الخدمات

### أ) متزامن: Basket ← Catalog (وقت إضافة منتج للسلة)

```
Client ──POST /api/basket/items──► Basket.API ──GET /api/products/{id}──► Catalog.API
                                        │  ياخد الاسم والسعر الحقيقي
                                        ▼
                                     basket-db
```

- `ICatalogService` في `Basket.Domain/Contracts`، والتنفيذ `CatalogService` في `Basket.Infrastructure/ExternalServices/Catalog`.
- `AddHttpClient<ICatalogService, CatalogService>` + `AddStandardResilienceHandler()`، والعنوان من `CatalogClient__BaseUrl` (Options + `ValidateOnStart`).
- Catalog واقع ← **503**. المنتج مش موجود ← **404**. والعميل مبيبعتش السعر ولا الاسم.

### ب) غير متزامن: Basket ← Ordering (الـ Checkout)

```
POST /api/basket/checkout
  └─ Basket: ONE transaction (delete basket + Wolverine outbox row) → 202 { checkoutId }
       └─ after commit: send to RabbitMQ exchange "basket-checked-out" (fanout)
            └─ queue "ordering.basket-checked-out" (shared by all ordering-api instances)
                 └─ Ordering: durable inbox → BasketCheckedOutHandler → Order (CheckoutId unique)
```

- **العقد:** `BasketCheckedOutIntegrationEvent(BuyerId, Currency, ShippingAddress, Items)` في `BuildingBlocks.Contracts`. `Id` الحدث = `CheckoutId`. مفيش `Total`. و `ProductId` نوعه `Guid` في كل الخدمات.
- **Basket (Outbox):** `IDbContextOutbox` + `SaveChangesAndFlushMessagesAsync`، و `PersistMessagesWithSqlServer(cs, "wolverine")` + `UseEntityFrameworkCoreTransactions()` + `Policies.UseDurableOutboxOnAllSendingEndpoints()`. الـ DbContext بـ `optionsLifetime: Singleton` و **من غير** `EnableRetryOnFailure`.
- **Ordering (Inbox + Idempotency):** `ListenToRabbitQueue(...).UseDurableInbox()`، و `Orders.CheckoutId` عليه Unique Index (مفلتر `IS NOT NULL`). التكرار (SQL 2601/2627) ← Log + `return` (Ack).
- **الأخطاء في Ordering:** `DomainException` ← DLQ فورًا. وأي Exception تاني ← `RetryWithCooldown(1s,5s,10s)` ثم `ScheduleRetry(1m,5m)` ثم DLQ (`wolverine-dead-letter-queue`). و EF Retry صغير (2 محاولات، 2 ثانية).
- `ServiceLocationPolicy.AllowedButWarn` في Ordering بقرار (ADR-004): `DomainEventDispatcher` بياخد `IServiceProvider`.

---

## 4. البنية التحتية (Docker)

### التشغيل

```bash
docker compose up -d --build                          # تشغيل الكل
docker compose up -d --build --scale ordering-api=3   # 3 نسخ من Ordering
docker compose down                                   # إيقاف مع الاحتفاظ بالبيانات
docker compose down -v                                # إيقاف ومسح الـ volumes
```

### الكونتينرات

| Service | Container | البورت على الجهاز ← جوه الكونتينر | Volume |
|---|---|---|---|
| `catalog-db` | `ecommerce-catalog-db` | `127.0.0.1:1436 → 1433` | `ecommerce-catalog-db` |
| `catalog-api` | `ecommerce-catalog-api` | `127.0.0.1:5299 → 8080` | — |
| `basket-db` | `ecommerce-basket-db` | `127.0.0.1:1435 → 1433` | `ecommerce-basket-db` |
| `basket-api` | `ecommerce-basket-api` | `127.0.0.1:5260 → 8080` | — |
| `ordering-db` | `ecommerce-ordering-db` | `127.0.0.1:1437 → 1433` | `ecommerce-ordering-db` |
| `ordering-api` | (من غير `container_name` عشان الـ scale) | `127.0.0.1:5257-5259 → 8080` | — |
| `rabbitmq` | `rabbitmq` (`rabbitmq:4-management`) | `5672` و `15672` | `ecommerce-rabbitmq` |

- كل الكونتينرات على شبكة bridge اسمها `ecommerce`، والـ compose بيستخدم YAML anchors (`x-sqlserver` و `x-api` و `x-api-env` و `x-logging`).
- **قواعد البيانات:** `mssql/server:2022-latest` (Developer)، `1 CPU` و `2560m`، و `MSSQL_MEMORY_LIMIT_MB=512`، و healthcheck على `ready for client connections`.
- **الـ APIs:** `0.75 CPU` و `512m` و `no-new-privileges`. الـ entrypoint بيستنى القاعدة (TCP لحد 30 مرة) ويبني `ConnectionStrings__DefaultConnection`. والـ healthcheck على `/health/ready`.
- **RabbitMQ جوه Docker:** `RabbitMq__HostName: rabbitmq` في `environment` بيغطي على `localhost` اللي في الـ `.env`.
- **الـ Dockerfile:** Multi-stage (`sdk:10.0` ← `aspnet:10.0`)، والـ build context جذر الريبو عشان ملفات الـ props المركزية.

---

## 5. الإعدادات والأسرار

- كل خدمة ليها `.env` في `src/Services/<Service>/.env` (مش في git)، والمتسجل `.env.example` بس. و `rabbitmq.env` جنب الـ compose (فيه `RABBITMQ_DEFAULT_USER/PASS`).
- المفاتيح: `DB_NAME`, `DB_HOST`, `DB_PORT`, `DB_USER`, `DB_PASSWORD`, `DB_SCHEMA`. و Basket كمان `CatalogClient__BaseUrl`. و Basket و Ordering: `RabbitMq__HostName/UserName/Password`.
- `.dockerignore` بيمنع نسخ `.env` جوه الصور، والأسرار بتوصل بـ `env_file`.
- `LocalEnvLoader.LoadLocalDotEnv()` (في `BuildingBlocks`) بيقرا الـ `.env` لما تشغّل بـ `dotnet run`.

## 6. البناء والحزم

- **.NET 10** و **EF Core 10**. كل النسخ في `Directory.Packages.props`، والـ `.csproj` بيكتب اسم الحزمة بس.
- `CentralPackageTransitivePinningEnabled=true`، وتحذيرات الثغرات `NU1903` و `NU1904` بتوقف الـ build.
- **Wolverine 6.61.0 (MIT):** `WolverineFx.RabbitMQ` و `WolverineFx.RuntimeCompilation` و `WolverineFx.EntityFrameworkCore` و `WolverineFx.SqlServer`، كلهم في `BuildingBlocks.Messaging.csproj` وبنفس الإصدار. ولازم `opts.UseRuntimeCompilation()`.
- الـ Migrations بتتطبق تلقائيًا بـ `MigrateDatabaseAsync<T>()` (Fail-fast)، وجداول Wolverine بـ `AddResourceSetupOnStartup()`.
- لإضافة migration (مثال Ordering):
  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> \
    -p src/Services/Ordering/Ordering.Infrastructure \
    -s src/Services/Ordering/Ordering.API \
    -o Data/Migrations
  ```

---

## 7. ملاحظات ونواقص معروفة

- **الـ Checkout:** مفيش Idempotency Key على مستوى الـ API (العميل لو داس مرتين = حدثين). والتحقق النهائي من السعر وقت الـ Checkout لسه (الموديول 8).
- **الـ DLQ:** مفيش Alert عليها (الموديول 13).
- **`depends_on`:** basket-api و ordering-api مش مستنيين `rabbitmq: service_healthy` في الـ compose الحالي، وكمان catalog-api عنده `RabbitMq__HostName` وهو مش بيستخدم RabbitMQ. (تأكد وصلّح.)
- `BuildingBlocks.Messaging` لسه فيه `IEventBus` و `RabbitMqConsumer` المكتوبين بإيدنا (مرجع تعليمي)، والكود الحقيقي بـ Wolverine.
- كيان `User` متكرر في التلات خدمات ومعاه `UsersController`، والمفروض يتشال لما ييجي Identity (الموديول 10).
- لسه فيه ملفات `Class1.cs` فاضية، و `Basket.API/Properties/Entities/User.cs` في مكان غلط.
- `UseHttpsRedirection` و Authentication لسه. ومفيش API Gateway ولا Tests ولا CI.
- الخطة في `README.md` بتقترح PostgreSQL لـ Catalog و Redis لـ Basket. التنفيذ الحالي كله SQL Server (Redis في الموديول 8).
