# سياق المشروع: الهيكل والبنية التحتية

ملف مرجعي سريع يشرح شكل المشروع وطريقة تشغيله.
لو بتقرأ الكود لأول مرة، أو هتدّي المشروع لحد تاني (أو لـ AI agent)، ابدأ من هنا.
وخطة الكورس والتقدم في `docs/LEARNING_PROGRESS.md`، والقرارات في `docs/adr/`.

> آخر تحديث: 2026-11-22 · بيوصف الكود **بعد الموديول 8 كامل** (8.1 → 8.6).

---

## 1. المشروع في سطرين

متجر إلكتروني مبني بمعمارية **Microservices** على **ASP.NET Core (.NET 10)**.
كل خدمة مستقلة: لها API خاص بيها، وقاعدة بيانات **SQL Server** خاصة بيها (Database per Service)، وكونتينر خاص بيها. والخدمات بتتكلم بـ HTTP (متزامن) وبـ **RabbitMQ + Wolverine** (غير متزامن)، و Catalog عنده كاش بـ **Redis**.

| الخدمة | المسؤولية | الجداول | بورت الـ API | بورت الـ DB على الجهاز |
|---|---|---|---|---|
| **Catalog** | المنتجات والأسعار | `Users`, `Products` + Schema `wolverine` (Outbox) | `5299` | `1436` |
| **Basket** | سلة المشتريات والـ Checkout | `Users`, `ShoppingCarts`, `ShoppingCartItems` + Schema `wolverine` (Outbox + Inbox) | `5260` | `1435` |
| **Ordering** | الطلبات (DDD) | `Users`, `Orders`, `OrderItems` + Schema `wolverine` (Inbox) | `5257-5259` (لحد 3 نسخ) | `1437` |
| **RabbitMQ** | وسيط الرسائل | — | `5672` (AMQP) و `15672` (اللوحة) | — |
| **Redis** | كاش Catalog (L2) | — | `6379` | — |

Swagger لكل خدمة على `http://localhost:<port>/swagger`، والصحة على `/health/live` و `/health/ready`.

---

## 2. شكل الملفات

```
ECommerceMicroservices/
├── ECommerceMicroservices.sln
├── Directory.Build.props        ← TargetFramework (net10.0) + Nullable + ImplicitUsings لكل المشاريع
├── Directory.Packages.props     ← نسخ كل حزم NuGet في مكان واحد (Central Package Management)
├── .config/dotnet-tools.json    ← أدوات محلية: dotnet-ef و csharpier
├── docker-compose.yml           ← 3 APIs + 3 SQL Server + RabbitMQ + Redis (+ نسخ ordering-api)
├── rabbitmq.env / rabbitmq.env.example
├── demos/                       ← RabbitDemo.Publisher و RabbitDemo.Consumer (الموديول 6.3، RabbitMQ.Client الخام)
├── docs/
│   ├── PROJECT_CONTEXT.md       ← الملف ده
│   ├── LEARNING_PROGRESS.md     ← خطة الكورس والتقدم
│   ├── adr/                     ← ADR-003 (Wolverine) و ADR-004 (Outbox/Inbox) و ADR-005 (البيانات عبر الخدمات والكاش)
│   └── notes/                   ← ملفات ملاحظات كل موديول (07 و 08)
└── src/
    ├── BuildingBlocks/
    │   ├── BuildingBlocks/            ServiceDefaults (Serilog + ProblemDetails + Health + Fail-fast migration)، LocalEnvLoader، ErrorHandling
    │   ├── BuildingBlocks.Contracts/  IntegrationEvent + Basket/BasketCheckedOutIntegrationEvent + Catalog/ProductPriceChangedIntegrationEvent
    │   └── BuildingBlocks.Messaging/  باكدجات Wolverine + IEventBus المكتوب بإيدنا (مرجع تعليمي من 6.4، مش مستخدم)
    └── Services/
        ├── Catalog/   (.env، API، Application، Domain، Infrastructure)
        ├── Basket/    (.env، API، Application، Domain، Infrastructure)
        └── Ordering/  (.env، API، Application، Domain، Infrastructure)
```

### الطبقات جوه كل خدمة (Clean Architecture مبسطة)

```
API  ──►  Infrastructure  ──►  Domain
 └──────►  Application ──► Domain
```

- **Domain**: الكيانات والعقود.
  - Ordering: DDD كامل: `Order` (Aggregate) و `OrderItem` و `Money` و `Address` و `OrderId` و Domain Events و `DomainException`.
  - Catalog: `Product` بـ `Price` و `PriceVersion` (`private set`) و `ChangePrice()`، و `DomainException` خاص بيه.
- **Application**: الـ DTOs (Basket و Catalog)، و Domain Event Handlers و `IOrderQueries` (Ordering).
- **Infrastructure**: EF Core و `{Service}DbContext` والـ Migrations (`Data/Migrations`)، والتنفيذ الفعلي للعقود (`CatalogService`، `OrderRepository`، `OrderQueries`، `DomainEventDispatcher`).
- **API**: الـ Controllers و `Program.cs`، والـ Integration Event Handlers (`IntegrationEvents/`) في Basket و Ordering.

---

## 3. التواصل بين الخدمات

### أ) متزامن: Basket ← Catalog (مرتين)

```
POST /api/basket/items     ──► Basket ──GET /api/products/{id}──────► Catalog (HybridCache)
POST /api/basket/checkout  ──► Basket ──GET /api/products/prices?ids=──► Catalog (no cache)
```

- `ICatalogService` في `Basket.Domain/Contracts`، والتنفيذ `CatalogService` في `Basket.Infrastructure/ExternalServices/Catalog`: `GetProductByIdAsync` و `GetPricesAsync` (Batch).
- `AddHttpClient<ICatalogService, CatalogService>` + `AddStandardResilienceHandler()`، والعنوان من `CatalogClient__BaseUrl` (Options + `ValidateOnStart`).
- **وقت الإضافة:** Basket بياخد الاسم والسعر و `PriceVersion`. Catalog واقع ← 503، المنتج مش موجود ← 404. والعميل مبيبعتش السعر ولا الاسم. ولو المنتج موجود في السلة، السعر بيتحدّث بس لو النسخة أحدث.
- **وقت الـ Checkout (التحقق النهائي):** طلب واحد لكل المنتجات (حد 100). Catalog واقع ← **503** ومفيش طلب. سعر اتغير أو منتج اتمسح ← تصليح السلة وحفظها + **409** بـ `CheckoutConflictResponseDTO(Message, PriceChanges, RemovedProducts)`.

### ب) غير متزامن: Catalog ← Basket (تغيير السعر)

```
PUT /api/products/{id}/price
  └─ Catalog: Product.ChangePrice (PriceVersion+1, concurrency token)
       └─ ONE transaction (price + Wolverine outbox row) → remove cache key
            └─ exchange "product-price-changed" → queue "basket.product-price-changed"
                 └─ Basket: durable inbox → ProductPriceChangedHandler
                      └─ ExecuteUpdateAsync: ShoppingCartItems WHERE ProductId = @id AND PriceVersion < @version
```

- **العقد:** `ProductPriceChangedIntegrationEvent(Guid ProductId, decimal NewPrice, long Version)` (Event-Carried State Transfer، من غير Currency).
- **Catalog:** `PriceVersion.IsConcurrencyToken()` ← أدمنين في نفس اللحظة = واحد ينجح والتاني **409**. نفس السعر ← 200 من غير حفظ ولا حدث. سعر ≤ 0 ← `DomainException` ← **400**.
- **Basket:** الـ Handler (Naturally Idempotent): الحدث المكرر أو القديم بيعدّل صفر صفوف. نفس سياسة الـ Retry بتاعة Ordering (من غير قاعدة `DomainException`).

### ج) غير متزامن: Basket ← Ordering (الـ Checkout)

```
POST /api/basket/checkout
  └─ Basket: price check (أ) → ONE transaction (delete basket + Wolverine outbox row) → 202 { checkoutId }
       └─ after commit: send to RabbitMQ exchange "basket-checked-out" (fanout)
            └─ queue "ordering.basket-checked-out" (shared by all ordering-api instances)
                 └─ Ordering: durable inbox → BasketCheckedOutHandler → Order (CheckoutId unique)
```

- **العقد:** `BasketCheckedOutIntegrationEvent(BuyerId, Currency, ShippingAddress, Items)`. `Id` الحدث = `CheckoutId`. مفيش `Total`. و `Currency` في Basket مكتوبة `"EGP"`. و `ProductId` نوعه `Guid` في كل الخدمات.
- **Basket (Outbox):** `IDbContextOutbox` + `SaveChangesAndFlushMessagesAsync`، و `PersistMessagesWithSqlServer(cs, "wolverine")` + `UseEntityFrameworkCoreTransactions()` + `Policies.UseDurableOutboxOnAllSendingEndpoints()`. الـ DbContext بـ `optionsLifetime: Singleton` و **من غير** `EnableRetryOnFailure` (ونفس الكلام في Catalog).
- **Ordering (Inbox + Idempotency):** `ListenToRabbitQueue(...).UseDurableInbox()`، و `Orders.CheckoutId` عليه Unique Index (مفلتر `IS NOT NULL`). التكرار (SQL 2601/2627) ← Log + `return` (Ack).
- **الأخطاء في Ordering:** `DomainException` ← DLQ فورًا. وأي Exception تاني ← `RetryWithCooldown(1s,5s,10s)` ثم `ScheduleRetry(1m,5m)` ثم DLQ (`wolverine-dead-letter-queue`). و EF Retry صغير (2 محاولات، 2 ثانية).
- `ServiceLocationPolicy.AllowedButWarn` في Ordering بقرار (ADR-004).

### د) القراءة في Ordering (CQRS مستوى 1)

- `GET /api/orders?buyerId=` ← `IOrderQueries` (`Ordering.Application/Orders/Queries`) ← `OrderQueries` (`Ordering.Infrastructure/Queries`): `AsNoTracking` + Projection لـ `OrderSummaryDto` و `OrderItemDto`، والإجمالي محسوب في SQL. الكتابة لسه بـ `IOrderRepository` والـ `Order` Aggregate.
- صفحة "طلباتي" بالصور (API Composition مع Catalog) مكانها الـ BFF في الموديول 9.

### هـ) الكاش في Catalog

- `AddStackExchangeRedisCache` (`InstanceName = "catalog:"`) + `AddHybridCache` (L2 Redis = 10 دقايق، L1 ميموري = 30 ثانية).
- بيتكاش `GET /api/products/{id}` بس (بيرجّع `ProductResponse` DTO، مفتاح `product:{id}`)، والمفتاح بيتمسح **بعد** حفظ تغيير السعر.
- **مش بيتكاش:** `GET /api/products/prices` (التحقق من الحقيقة). والسلة مش في Redis (الـ Checkout محتاج Outbox في نفس Transaction الـ SQL).

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
| `redis` | `ecommerce-redis` (`redis:8-alpine`) | `127.0.0.1:6379 → 6379` | — (كاش بس، من غير persistence) |

- كل الكونتينرات على شبكة bridge اسمها `ecommerce`، والـ compose بيستخدم YAML anchors (`x-sqlserver` و `x-api` و `x-api-env` و `x-logging`).
- **قواعد البيانات:** `mssql/server:2022-latest` (Developer)، `1 CPU` و `2560m`، و `MSSQL_MEMORY_LIMIT_MB=512`، و healthcheck على `ready for client connections`.
- **الـ APIs:** `0.75 CPU` و `512m` و `no-new-privileges`. الـ entrypoint بيستنى القاعدة (TCP لحد 30 مرة) ويبني `ConnectionStrings__DefaultConnection`. والـ healthcheck على `/health/ready`.
- **`depends_on`:** الـ APIs التلاتة مستنيين `rabbitmq: service_healthy` (Wolverine + `AutoProvision` محتاجينه وقت الـ Startup)، و catalog-api كمان مستني `redis: service_healthy`. ووقت التشغيل الـ Outbox بيغطي وقعة RabbitMQ.
- **Redis:** `--save "" --appendonly no --maxmemory 256mb --maxmemory-policy allkeys-lru`، و healthcheck بـ `redis-cli ping`. بيستخدمه Catalog بس.
- **جوه Docker:** `RabbitMq__HostName: rabbitmq` و `ConnectionStrings__Redis: redis:6379` في `environment` بيغطوا على `localhost`.
- **الـ Dockerfile:** Multi-stage (`sdk:10.0` ← `aspnet:10.0`)، والـ build context جذر الريبو عشان ملفات الـ props المركزية.

---

## 5. الإعدادات والأسرار

- كل خدمة ليها `.env` في `src/Services/<Service>/.env` (مش في git)، والمتسجل `.env.example` بس. و `rabbitmq.env` جنب الـ compose (فيه `RABBITMQ_DEFAULT_USER/PASS`).
- المفاتيح: `DB_NAME`, `DB_HOST`, `DB_PORT`, `DB_USER`, `DB_PASSWORD`, `DB_SCHEMA`. و Basket كمان `CatalogClient__BaseUrl`. والتلات خدمات: `RabbitMq__HostName/UserName/Password`. و Catalog: `ConnectionStrings__Redis`.
- `.dockerignore` بيمنع نسخ `.env` جوه الصور، والأسرار بتوصل بـ `env_file`.
- `LocalEnvLoader.LoadLocalDotEnv()` (في `BuildingBlocks`) بيقرا الـ `.env` لما تشغّل بـ `dotnet run`.

## 6. البناء والحزم

- **.NET 10** و **EF Core 10**. كل النسخ في `Directory.Packages.props`، والـ `.csproj` بيكتب اسم الحزمة بس.
- `CentralPackageTransitivePinningEnabled=true`، وتحذيرات الثغرات `NU1903` و `NU1904` بتوقف الـ build.
- **Wolverine 6.61.0 (MIT):** `WolverineFx.RabbitMQ` و `WolverineFx.RuntimeCompilation` و `WolverineFx.EntityFrameworkCore` و `WolverineFx.SqlServer`، كلهم في `BuildingBlocks.Messaging.csproj` وبنفس الإصدار. ولازم `opts.UseRuntimeCompilation()`. و Catalog.API بقى عامل Reference لـ `BuildingBlocks.Messaging` و `BuildingBlocks.Contracts`.
- **الكاش (MIT):** `Microsoft.Extensions.Caching.Hybrid` و `Microsoft.Extensions.Caching.StackExchangeRedis` في `Catalog.API`.
- الـ Migrations بتتطبق تلقائيًا بـ `MigrateDatabaseAsync<T>()` (Fail-fast)، وجداول Wolverine بـ `AddResourceSetupOnStartup()`. أي تغيير في الـ Model من غير Migration = `PendingModelChangesWarning` والخدمة تقفل (Restart loop).
- Migrations الموديول 8: `AddPriceVersionToProducts` (Catalog) و `AddPriceVersionToCartItems` (Basket، ومعاها `HasPrecision(18,2)` للسعر).
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

- **الـ Checkout:** مفيش Idempotency Key على مستوى الـ API (العميل لو داس مرتين = حدثين).
- **مفيش حدث `ProductRemoved`:** المنتج الممسوح بيفضل في السلة لحد الـ Checkout (اللي بيشيله ويرجّع 409).
- **ثغرة صغيرة وقت الإضافة:** لو حدث السعر وصل قبل ما المنتج يتحفظ في السلة بأجزاء من الثانية، السلة بتفضل بالسعر القديم لحد الـ Checkout.
- **مفيش Unique Index** على (ShoppingCartId, ProductId) في Basket.
- **`GET /api/orders`:** الـ `buyerId` من الـ URL (الموديول 10 هياخده من الـ JWT)، ومفيش Pagination.
- **L1 في نسخ Catalog التانية** ممكن يفضل بالسعر القديم لحد 30 ثانية بعد التغيير (الـ Checkout بيتحقق).
- **الـ DLQ:** مفيش Alert عليها (الموديول 13).
- `BuildingBlocks.Messaging` لسه فيه `IEventBus` و `RabbitMqConsumer` المكتوبين بإيدنا (مرجع تعليمي)، والكود الحقيقي بـ Wolverine.
- كيان `User` متكرر في التلات خدمات ومعاه `UsersController`، والمفروض يتشال لما ييجي Identity (الموديول 10).
- لسه فيه ملفات `Class1.cs` فاضية، و `Basket.API/Properties/Entities/User.cs` في مكان غلط.
- `UseHttpsRedirection` و Authentication لسه. ومفيش API Gateway ولا Tests ولا CI.
- الخطة في `README.md` بتقترح PostgreSQL لـ Catalog و Redis لـ Basket. التنفيذ: كله SQL Server، و Redis كاش لـ Catalog بس (السلة فضلت في SQL بقرار في ADR-005).
