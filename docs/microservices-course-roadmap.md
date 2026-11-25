# كورس الـ Microservices على مشروع ECommerceMicroservices: الخطة والتقدم

> آخر تحديث: 2026-11-24 · مبني على `docs/PROJECT_CONTEXT.md` وعلى الكود الفعلي في الريبو وملاحظات المذاكرة.
> الملف ده نسخته في الريبو هي `docs/LEARNING_PROGRESS.md` عشان Claude Code جوه Cursor يقرأه أول كل جلسة ويحدّثه في آخرها.

---

## 0. ابدأ من هنا (لأي محادثة جديدة)

1. اقرا الملف ده كله، وبعدين `docs/adr/ADR-006-api-gateway-and-bff.md` (قرارات الموديول 9)، وملف ملاحظات آخر موديول: **"الموديول 9: البوابة (YARP Gateway) والـ BFF"** (Doc + نسخة في مستندات المشروع + Google Doc في فولدر `microservices` على Drive + `docs/notes/09-api-gateway-bff.md`).
2. الموديول 9 مقفول بالكامل. **ابدأ الموديول 10 (Identity: Keycloak و JWT) من الدرس 10.1.**
3. **مكتبة الرسائل المعتمدة: Wolverine** (ADR-003)، والـ Outbox/Inbox بـ Wolverine (ADR-004)، والبيانات عبر الخدمات والكاش (ADR-005). أي كود جديد يتكتب بنفس الأسلوب.
4. التزم بـ **أسلوب الشرح** في القسم 2 بالحرف.
5. **اقرا الكود من الريبو العام** (`git clone https://github.com/ahmednagi96/ecommercemicroservices`) قبل أي درس.

---

## 1. إنت واقف فين دلوقتي (Snapshot)

**اللي خلصته فعلًا (✅):**
- تقسيم النظام لـ 3 خدمات: Catalog و Basket و Ordering، كل خدمة بطبقات Clean Architecture مبسطة (API / Application / Domain / Infrastructure).
- الـ (Database per Service): 3 قواعد SQL Server منفصلة، ومفيش Foreign Keys بين الخدمات.
- Docker: Dockerfile متعدد المراحل، و docker-compose، و YAML anchors (`x-sqlserver` و `x-api` و `x-api-env`)، و healthcheck للقواعد والـ APIs، وحدود موارد، وشبكة bridge اسمها `ecommerce`، والأسرار عن طريق `env_file`.
- .NET 10، و (Central Package Management) في `Directory.Packages.props`، و `Directory.Build.props` بيحط TargetFramework و Nullable و ImplicitUsings لكل المشاريع.
- الاتصال المتزامن من Basket لـ Catalog بـ Typed HttpClient **وقت إضافة منتج للسلة ووقت الـ Checkout** + `AddStandardResilienceHandler`.
- الموديول 4 ✅ (`BuildingBlocks/ServiceDefaults`: Health Checks و Serilog و ProblemDetails و Fail-fast migrations).
- الموديول 5 ✅ (Ordering بـ DDD: `Order` Aggregate و `Money` و `Address` و Domain Events و `OrderId` و `IOrderRepository`).
- الموديول 6 ✅ (RabbitMQ و Wolverine، PR #9، و ADR-003).
- الموديول 7 ✅ (Outbox و Inbox و Idempotent Consumer، PR #11 و #12، و ADR-004).
- الموديول 8 ✅ (الدروس 8.1 → 8.6 والتجارب كلها اتعملت، وملف الملاحظات و ADR-005 اتعملوا).
- الموديول 9 ✅ (البوابة بـ YARP و Web.Bff، الدروس 9.1 → 9.5، وملف الملاحظات و ADR-006).

**حالة الكود بعد الموديول 7 (الأساس، على `main`):**
- **RabbitMQ** في compose: `rabbitmq:4-management`، و `rabbitmq.env`، و healthcheck، و volume، وشبكة `ecommerce`.
- **`BuildingBlocks.Contracts`**: `IntegrationEvent` (`Id` و `OccurredOn` بـ `init`، abstract record) و `Basket/BasketCheckedOutIntegrationEvent(BuyerId, Currency, ShippingAddress, Items)` و `CheckoutItem(Guid ProductId, ProductName, UnitPrice, Quantity)`. **مفيش `Total`** في العقد. و `Currency` في Basket مكتوبة `"EGP"` ثابتة.
- **`ProductId` نوعه `Guid` في كل الخدمات**.
- باكدجات Wolverine في `BuildingBlocks.Messaging.csproj` (6.61.0 في CPM): `WolverineFx.RabbitMQ` و `RuntimeCompilation` و `EntityFrameworkCore` و `SqlServer`.
- **Basket (Outbox):** `PersistMessagesWithSqlServer(cs, "wolverine")` + `UseEntityFrameworkCoreTransactions()` + `UseDurableOutboxOnAllSendingEndpoints()` + `AddResourceSetupOnStartup()`. الـ DbContext بـ `optionsLifetime: Singleton` ومن غير `EnableRetryOnFailure`. الـ Checkout بـ `IDbContextOutbox` + `Enroll` + `PublishAsync` + `SaveChangesAndFlushMessagesAsync`.
- **Ordering (Inbox + Idempotency):** `ListenToRabbitQueue("ordering.basket-checked-out").UseDurableInbox()`، و `Order.CheckoutId` + Unique Index، و EF Retry صغير (2 / 2s). سياسة الأخطاء: `DomainException` ← DLQ، والباقي ← `RetryWithCooldown(1s,5s,10s).Then.ScheduleRetry(1m,5m).Then.MoveToErrorQueue()`. و `ServiceLocationPolicy.AllowedButWarn` بقرار.
- الـ compose: basket-api و ordering-api مستنيين `rabbitmq: service_healthy` (Wolverine + `AutoProvision` محتاجينه وقت الـ Startup).

**اللي اتغير في الموديول 8:**
- **العقد الجديد:** `BuildingBlocks.Contracts/Catalog/ProductPriceChangedIntegrationEvent(Guid ProductId, decimal NewPrice, long Version)` (Event-Carried State Transfer، من غير Currency ولا OldPrice).
- **Catalog:** `Product.Price` و `PriceVersion` بـ `private set` + `ChangePrice()` (بيرجّع bool، ويرمي `Catalog.Domain.Exceptions.DomainException`)، و `PriceVersion.IsConcurrencyToken()` + Migration `AddPriceVersionToProducts`. Wolverine + Outbox زي Basket (`EnableRetryOnFailure` اتشال، و Singleton options)، و `PublishMessage<ProductPriceChanged...>().ToRabbitExchange("product-price-changed")`. Endpoints: `PUT /api/products/{id}/price` (400 / 404 / 409 مع Outbox)، و `GET /api/products/prices?ids=` (Batch، حد 100، `AsNoTracking` + Select، **مش متكاش**)، و `GET /api/products/{id}` بيرجّع `ProductResponse` (DTO) من HybridCache. الـ DTOs في فولدر `DTOs` (`ChangePriceRequest` و `ProductPriceResponse` و `ProductResponse`).
- **Basket:** `ShoppingCartItem.PriceVersion` + `HasPrecision(18,2)` للسعر + Migration `AddPriceVersionToCartItems`. `CatalogProductDto` فيه `PriceVersion`، و `ICatalogService.GetPricesAsync` (Batch). `AddItemToBasket` بيحدّث السعر بس لو النسخة أحدث. `ProductPriceChangedHandler` في `Basket.API/IntegrationEvents` بـ `ExecuteUpdateAsync` بشرط `PriceVersion < e.Version` (Naturally Idempotent). Wolverine: `DeclareExchange("product-price-changed", ex => ex.BindQueue("basket.product-price-changed"))` + `ListenToRabbitQueue(...).UseDurableInbox()` + نفس سياسة الـ Retry (من غير DomainException). الـ Checkout: يسأل `GetPricesAsync` ← Catalog واقع = 503، اختلاف سعر أو منتج اتمسح = تصليح السلة + 409 بـ `CheckoutConflictResponseDTO(Message, PriceChanges, RemovedProducts)`، غير كده نفس الـ Outbox.
- **Ordering (CQRS مستوى 1):** `Ordering.Application/Orders/Queries/IOrderQueries` + `Ordering.Infrastructure/Queries/OrderQueries` (AsNoTracking + Projection لـ `OrderSummaryDto` و `OrderItemDto`، والـ Total محسوب في SQL) + `GET /api/orders?buyerId=`.
- **Redis:** خدمة `redis` في compose (`redis:8-alpine`، من غير persistence، `maxmemory 256mb`، `allkeys-lru`، healthcheck، port `127.0.0.1:6379`). Catalog بس بيستخدمه: `ConnectionStrings__Redis: redis:6379` و `depends_on: redis`. `AddStackExchangeRedisCache(InstanceName = "catalog:")` + `AddHybridCache` (L2 = 10 دقايق، L1 = 30 ثانية). `cache.RemoveAsync(ProductKey(id))` **بعد** حفظ تغيير السعر.
- **compose لـ catalog-api:** رجع `RabbitMq__HostName: rabbitmq` و `depends_on: rabbitmq` (Catalog بقى Publisher)، و `RabbitMq__UserName/Password` في `src/Services/Catalog/.env`.

**اللي اتعمل في الموديول 9 لحد دلوقتي (9.1 → 9.3، على فرع الموديول 9):**
- **مشروع البوابة:** `src/ApiGateways/Gateway.API` (قالب `web`، مضاف للـ sln). الـ csproj فيه `PackageReference Include="Yarp.ReverseProxy"` بس، والإصدار `2.3.0` في CPM (Third-party). مش بيستخدم `AddServiceDefaults` لأنها محتاجة `DbContext` (هتتظبط في 9.3).
- **`Program.cs`:** `AddReverseProxy().LoadFromConfig(...)` + `AddHealthChecks()` + `MapHealthChecks("/health/live")` + `MapReverseProxy()`. البوابة `live` بس، ومفيش `ready` معتمد على الخدمات (بقرار).
- **`appsettings.json`:** 3 Routes (`catalog-route` و `basket-route` و `ordering-route`) بـ `Match.Path = "/<svc>/{**catch-all}"` و `PathRemovePrefix`. و 3 Clusters كل واحد فيه `LoadBalancingPolicy: RoundRobin` و `HealthCheck.Active` (كل 10s، Timeout 5s، `ConsecutiveFailures`، `Path: /health/ready`). العناوين هنا `localhost:<port>` للتشغيل بـ `dotnet run` بس (`launchSettings` على 5000).
- **`appsettings.Docker.json`:** عناوين Docker. كل Cluster فيه 3 Destinations بأسماء الكونتينرات الكاملة (`http://ecommerce-ordering-api-1:8080/` ...إلخ) لأن الخدمات اتشغلت بـ `--scale <svc>=3` في التجربة.
- **compose `gateway-api`:** من غير `<<: *api` (لأنه بيستنى DB)، ومن غير `depends_on`. `ASPNETCORE_ENVIRONMENT: Docker` و `ASPNETCORE_URLS: http://+:8080`، والبورت على الجهاز **`127.0.0.1:5100:8080`** (5000/6000 كانوا مشغولين، و 6000 المتصفحات بتمنعه). healthcheck بـ curl على `/health/live`، وحدود 0.5 CPU و 256MB. Dockerfile بينسخ فولدر البوابة بس.
- **تنضيف:** اتشال `ConnectionStrings` من `appsettings.json` في الخدمات التلاتة (كان `localhost,1434` ومش مستخدم). اتقال للمستخدم يحط `127.0.0.1` قدام بورتات RabbitMQ (`5672` و `15672`).
- **9.3 (Timeout):** `HttpRequest.ActivityTimeout` لكل Cluster: Catalog و Ordering = 10s، و Basket = 35s (لأن `AddStandardResilienceHandler` في الـ Checkout ممكن ياخد لحد 30s). الافتراضي في YARP 100s. التجربة بـ `docker pause` لنسخة ← 504 بعد المهلة، وبعدها الـ Health Check شالها. فكرة مهمة: 504 = "مش عارف"، العملية ممكن تكون نجحت (مرتبطة بثغرة Idempotency Key).
- **9.3 (Rate Limiting، جوه ASP.NET من غير باكدج):** `AddRateLimiter` بـ `RejectionStatusCode = 429` (الافتراضي 503 غلط في المعنى)، و `GlobalLimiter` (FixedWindow لكل IP: 20 طلب / 10s للتجربة، و `/health` مستثنى بـ `GetNoLimiter`)، وسياسة `checkout` (3 / 30s) على Route جديد `checkout-route` (`POST /basket/api/basket/checkout`، نفس basket-cluster و PathRemovePrefix). `OnRejected` بيحط `Retry-After`. دالة `static string ClientIp(HttpContext)` في آخر `Program.cs`. سياسة `per-ip` الأولى اتشالت. ملحوظات اتشرحت: كل الطلبات من الجهاز في Docker بتبان بـ IP واحد، و `X-Forwarded-For` لما يبقى فيه LB، والعدّاد في البوابة مش في نسخ الخدمة، و 3 نسخ بوابة = 3 عدّادات (الحل Redis، مش معمول).
- **9.3 (CORS):** `AddCors` بسياسة `frontend` (`WithOrigins` من `Cors:AllowedOrigins` في appsettings = `http://localhost:3000`، و `WithMethods(GET,POST,PUT,DELETE)`، و `AllowAnyHeader`). `CorsPolicy: frontend` على الـ 4 Routes. الترتيب: `MapHealthChecks` ← `UseCors()` ← `UseRateLimiter()` ← `MapReverseProxy()` (عشان الـ Preflight ميتحسبش من الرصيد). التجربة بصفحة `cors-test.html` على `python3 -m http.server 3000` (قبل: Blocked والطلب وصل لـ Ordering برضه، بعد: OK)، و `curl -X OPTIONS` بـ Origin مسموح ومش مسموح. اتشرح: Origin، و Same-Origin Policy، و CORS بيحمي المستخدم مش الـ API، و Preflight مقابل CSRF Token و Simple Requests.
- **9.4 (BFF وصفحة "طلباتي"):** Catalog فيه Endpoint جديد `GET /api/products?ids=` (حد 100، AsNoTracking + Select) بيرجّع `ProductSummaryResponse(Id, Name, ImageUrl, Price, InStock = Stock > 0)` في `Catalog.Application/DTOs/Products/Responses` (منفصل عن `/prices` اللي بيستخدمه Basket في الـ Checkout، بقرار). مشروع جديد `src/ApiGateways/Web.Bff` (قالب web، باكدج `Microsoft.Extensions.Http.Resilience` بس): `Clients/ServiceDtos.cs` (نسخ خاصة من JSON الخدمات: `OrderDto` و `OrderItemDto` و `ProductSummaryDto`، من غير Project References)، و `OrderingClient` و `CatalogClient` (Typed HttpClients، والـ Catalog بيقسّم بـ `Chunk(100)`)، و `MyOrders/MyOrdersResponse.cs` (`MyOrdersResponse(ProductDetailsAvailable, Orders)` و `MyOrderView` و `MyOrderItemView` بحقول Catalog nullable)، و `MyOrders/MyOrdersEndpoint.cs` (Minimal API `GET /my-orders?buyerId=`: Ordering فشل ← 503، Catalog فشل ← نكمّل و `ProductDetailsAvailable=false`، والاسم والسعر المدفوع من Ordering، و `IsAvailable` null = مش عارفين / false = خلص أو اتمسح). مفيش `Task.WhenAll` لأن الطلبين معتمدين على بعض. `Program.cs`: `AddProblemDetails` + `AddHealthChecks` + `/health/live`، و Resilience: Ordering (Attempt 3s، Total 10s) و Catalog (Attempt 1s، Total 3s). `appsettings.json` فيه `Services:Ordering/Catalog` (localhost، والـ BFF على 5400 بـ dotnet run)، و `appsettings.Docker.json` بأسماء الخدمات (`ordering-api:8080`، ومش بيوزّع بالتساوي على النسخ، مؤجل للموديول 13). compose: `web-bff` **من غير ports** (جوه الشبكة بس)، `ASPNETCORE_ENVIRONMENT: Docker`، healthcheck `/health/live`. البوابة: `bff-route` (`/bff/{**catch-all}` + PathRemovePrefix + CorsPolicy frontend) و `bff-cluster` (ActivityTimeout 15s = أكبر من 10+3، و HealthCheck Path `/health/live` لأن الـ BFF مالوش ready). التجارب: الصفحة من البوابة، و 5400 مقفول، و Catalog واقف ← 200 + nulls في ~3s، و Ordering واقف ← 503. اتشرح: BFF مقابل Gateway، والشركات بتستخدمه إمتى، وطريقة "كل حاجة عبر الـ BFF" مقابل "المركّب بس" (اخترنا التانية)، وليه BFF للموبايل (أهم سبب: تحديث التطبيقات القديمة)، وأشهر غلط: منطق بيزنس في الـ BFF.
- **مؤجل:** Serilog و ProblemDetails للبوابة (ServiceDefaults محتاجة overload من غير DbContext) ← الموديول 13 أو 9.5.
- **التجارب اللي نجحت:** التوجيه من البوابة، و 404 من البوابة مقابل 404 من الخدمة، و 502 لما Catalog يقف والباقي شغال، و RoundRobin = 2/2/2 على 3 نسخ Ordering، وشيل النسخة الواقعة لوحدها.
- **مشاكل قابلها وحلولها:** (1) `address already in use` على البورت ← `dotnet run` للبوابة كان لسه شغال. (2) توزيع 4/2/0 ← سطور `ReverseProxy__...` القديمة في `environment` كانت بتغطي على `appsettings.Docker.json` وبتخلي `ordering-1` = `ordering-api` (اسم الخدمة)، فاتمسحت. (3) لخبطة البورتات: اتشرحت قاعدة "جوه Docker كله 8080 وبالأسماء، وبورتات `ports:` لجهازك بس"، والفرق بين اسم الخدمة واسم الكونتينر، وترتيب الإعدادات (القيمة اللي بتيجي آخر بتكسب: appsettings ← appsettings.{Env} ← env vars). (4) Swagger مش بيشتغل من البوابة، وده طبيعي.

**ثغرات معروفة لسه مفتوحة:**
- ✅ (اتقفلت) التحقق النهائي من السعر وقت الـ Checkout (8.4).
- ⚠️ مفيش Idempotency Key على مستوى الـ API لو العميل داس Checkout مرتين.
- ⚠️ مفيش حدث `ProductRemoved`: المنتج الممسوح بيفضل في السلة لحد الـ Checkout (اللي بيشيله و 409).
- ⚠️ `buyerId` في `GET /api/orders` جاي من الـ URL (الموديول 10 ياخده من الـ JWT)، ومفيش Pagination.
- ✅ (اتقفلت) صفحة "طلباتي" بالصور (API Composition) اتبنت في `Web.Bff` (9.4).
- ⚠️ مفيش Unique Index على (ShoppingCartId, ProductId) في Basket (Mini Task).
- ⚠️ مفيش Alert على الـ DLQ (الموديول 13).
- ⚠️ البوابة لسه مفيهاش Serilog و ProblemDetails (ServiceDefaults محتاجة overload من غير DbContext)، ولا Passive Health Check، والـ Rate Limiting عدّاداته في الذاكرة (مش موزع).

**إنت في:** **المرحلة الرابعة** → خلصت **الموديول 9**، والجاي **الموديول 10 (Identity: Keycloak و JWT، و `buyerId` من التوكن، والـ Rate Limiting لكل مستخدم)**.

---

## 2. منهجية الكورس وأسلوب الشرح (مهم جدًا)

**أسلوب الشرح اللي المستخدم طالبه:**
- المستخدم **لسه بيتعلم**، وعنده خبرة سابقة في **Laravel** (Events و Listeners و Queues و `dispatch`). قارن بيها لما تفيد.
- عربي مصري بسيط، والمصطلحات الإنجليزي بين قوسين: «الـ (Exchange)».
- **التنسيق مهم جدًا:** جمل قصيرة، ومتحطش كود أو مصطلحات إنجليزي كتير ورا بعض جوه جملة عربي واحدة لأن الترتيب بيتلخبط. الأحسن خطوات مرقمة، والكود في بلوك لوحده. المستخدم بيفضّل الشرح البرمجي المباشر عن التشبيهات لما التشبيه يبقى بعيد. **الأسئلة اللي فيها أسماء إنجليزي كتير اكتبها على سطور قصيرة منفصلة** (المستخدم اشتكى إن سؤال في 9.2 مكانش مقروء).
- **قسّم الدرس الكبير لأجزاء صغيرة**، جزء في كل رسالة، واستنى المستخدم يقول "يلا" قبل الجزء اللي بعده.
- **اشرح الكود حتة حتة "بالبلدي"**، وابدأ بأقل كود ممكن، وأجّل التفاصيل.
- قبل كود جديد، اربطه بحاجة المستخدم عملها بإيده أو اتعلمها قبل كده (مثلًا جدول "اللي عملناه بإيدنا ↔ اللي المكتبة بتعمله").
- لكل خطوة: **ليه بنعملها، وبتحل إيه**.
- الأسئلة السريعة أثناء المذاكرة: **رد قصير 2–4 سطور** على السؤال بس.
- صحّح إجابات المستخدم بلطف: ✅ الصح، و ⚠️ أو ❌ الغلط مع السبب.
- لما المستخدم يقول "مفهمتش" أو "تايه": اشرحها بجدول زمني (Timeline) خطوة بخطوة، أو جدول "مين بيعمل إيه وإمتى". الاتنين اشتغلوا كويس (الموديول 8، ولخبطة البورتات في 9.2). **ممنوع كلام عربي جوه Code Block وممنوع سلاسل أسهم ←، أي Timeline يبقى جدول Markdown** (`| الثانية | اللي بيحصل |`).
- **اقرا الكود الحقيقي في الريبو** قبل ما تكتب الدرس (`ahmednagi96/ECommerceMicroservices`، عام)، ومتفترضش حاجة عن المشروع. وأي حاجة مش متأكد إنها اتعملت، اسأل.
- لما خدمة تقع أو نتيجة غريبة: **اطلب اللوج الأول** (`docker compose ps` و `docker compose logs <svc> --tail 50`) قبل أي تخمين. في البوابة، `grep "Proxying to"` في لوجها بيوضح كل طلب راح فين.
- تحقق من إصدارات وتراخيص المكتبات بالبحث قبل ما تذكرها.
- المستخدم بيفضّل **أدوات مجانية/مفتوحة المصدر** تنفع في الشغل (عشان كده مفيش MassTransit).
- المستخدم شغال على Linux (Ubuntu، Bash).

**شكل كل درس:** المشكلة → الفكرة + رسمة/تشبيه → الكود في المشروع → المقايضات → إمتى ما تستخدمهاش → تجربة عملية → سؤال مراجعة + سؤال إنترفيو.

**تعريف الإنجاز لكل موديول:** كود شغال بـ `docker compose up`، وملف ملاحظات (الدروس + كل أسئلة المستخدم وإجاباتها + المشاكل اللي قابلها وحلولها + ملخص + اختبر نفسك)، و ADR. ملف الملاحظات بيتعمل Doc ونسخة Google Doc في فولدر `microservices` على Drive ونسخة في مستندات المشروع.

---

## 3. الخريطة الكاملة

### المرحلة الأولى: الأساسيات ✅
| # | الموديول | الحالة |
|---|---|---|
| 0 | ليه Microservices، والـ (Bounded Contexts) | ✅ |
| 1 | Docker و Docker Compose | ✅ |
| 2 | Database per Service و EF Core Migrations و CPM | ✅ |
| 3 | Sync HTTP من Basket لـ Catalog والـ Resilience | ✅ (gRPC مؤجل للموديول 12) |

### المرحلة الثانية ✅
**4.** تقوية الأساس ✅ · **5.** Ordering بـ DDD ✅

### المرحلة الثالثة: الاتصال غير المتزامن والبيانات الموزعة ✅
**6. ✅ أساسيات الرسائل: RabbitMQ و Wolverine**
- 6.1 Sync/Async و Event/Command و Pub/Sub · 6.2 Exchange و Queue و Binding و Competing Consumers · 6.3 `RabbitMQ.Client` الخام: Ack و Nack و DLQ و Prefetch · 6.4 Contracts و `IEventBus` بإيدنا وأول Checkout · 6.5 Wolverine و ADR-003.

**7. ✅ الرسائل الموثوقة (Reliable Messaging)**
- 7.1 Dual-write و At-least-once · 7.2 Outbox بإيدنا (فرع `experiment/manual-outbox`) · 7.3 Outbox بـ Wolverine · 7.4 Idempotent Consumer و Unique Index و Durable Inbox · 7.5 الأخطاء و Retry و DLQ و Replay وترتيب الرسائل و ADR-004.

**8. ✅ البيانات عبر الخدمات والكاش**
- 8.1 Stale Data والحلول التلاتة (اسأل / اتقالّي / اجمع)، والقرار: أحداث + تحقق متزامن وقت الـ Checkout.
- 8.2 `ProductPriceChanged` (ECST) و `Version` (من غير Gap Detection لأن الحدث فيه الحالة كاملة) و Optimistic Concurrency (409) و Outbox في Catalog و `PUT /price`.
- 8.3 `PriceVersion` في `ShoppingCartItem`، و Handler بـ `ExecuteUpdateAsync` مشروط (Naturally Idempotent)، وثغرة أجزاء الثانية وقت الإضافة.
- 8.4 Batch Endpoint `GET /prices` والتحقق النهائي (409 بالتفاصيل / 503)، والقرار حسب "تمن الغلط"، والمقايضة: Availability.
- 8.5 CQRS مستوى 1 (`IOrderQueries` و `GET /api/orders`)، و API Composition مقابل Read Model، و Graceful Degradation، و `Task.WhenAll`.
- 8.6 Cache-Aside بـ HybridCache (L1/L2) و Redis 8 (AGPLv3، وبديله Valkey BSD)، والمسح بعد الحفظ، والـ TTL كخطة احتياطية، و Stampede، وإيه اللي مش بيتكاش. ADR-005.

### المرحلة الرابعة: الحافة والأمان وسير العمل
**9. ✅ YARP Gateway و BFF**
- 9.1 ✅ ليه بوابة: 4 مشاكل من غيرها، ومقارنة بـ `routes/api.php` في Laravel، ومسؤوليات البوابة، وممنوع منطق بيزنس، و North-South مقابل East-West (Basket ← Catalog مش بيعدّي عليها)، والمقايضات (SPOF ← أكتر من نسخة لأنها Stateless)، وإمتى ما نستخدمهاش. YARP (MIT، 2.3.0).
- 9.2 ✅ أول بوابة: Routes و Clusters و Destinations، و `PathRemovePrefix`، و `appsettings.Docker.json`، والـ compose، وأكواد 404/502/504، و RoundRobin و Active Health Checks على 3 نسخ، و Active مقابل Passive.
- 9.3 ✅ شغل البوابة المشترك: Timeouts (504 و "مهلة أكبر من أبطأ عملية") و Rate Limiting (Global + checkout، 429) و CORS (سياسة frontend، Preflight).
- 9.4 ✅ الـ BFF وصفحة "طلباتي" (API Composition: Ordering ثم Catalog Batch، و Graceful Degradation، و Resilience بمهلات مختلفة، و BFF جوه الشبكة بس).
- 9.5 ✅ مراجعة واختبار (7 أسئلة)، و ADR-006، وملف ملاحظات الموديول 9.

**10.** Identity (Keycloak و JWT، و `buyerId` من التوكن) · **11.** Saga (`ReserveStock` كـ Command، وبـ Wolverine) · **12.** gRPC.

### المرحلة الخامسة: التشغيل والجودة
**13.** Observability و Aspire (ومنها Alert على الـ DLQ) · **14.** Testing · **15.** CI/CD و K8s (ومنها `TypeLoadMode.Static` لـ Wolverine) · **16.** Advanced و Capstone (ومنها Database Read Replicas: Primary/Secondary في SQL Server، وتوجيه القراية للـ Replica، و Replication Lag و Read-your-writes، والفرق بينها وبين الـ Data Replication بين الخدمات في الموديول 8).

---

## 4. المراجع العالمية المعتمدة
| النوع | المرجع | بنستخدمه في |
|---|---|---|
| كتاب | Sam Newman — Building Microservices (2nd ed) | الحدود، والتواصل، والنشر، والبوابة و BFF |
| كتاب + موقع | Chris Richardson — Microservices Patterns و microservices.io | الأنماط كلها (API Gateway و BFF) |
| رسمي Microsoft | .NET Microservices Architecture eBook، وريبو `dotnet/eShop` | التطبيق على .NET |
| رسمي Microsoft | توثيق YARP (learn.microsoft.com/aspnet/core/fundamentals/servers/yarp) | الموديول 9 |
| كتاب | Vlad Khononov — Learning DDD | الموديول 5 |
| كتاب | Martin Kleppmann — DDIA | البيانات والاتساق (الموديول 7 و 8) |
| مقال | Martin Fowler — What do you mean by "Event-Driven"? | ECST (الموديول 8) |
| كتاب | Hohpe & Woolf — Enterprise Integration Patterns | الرسائل |
| رسمي | RabbitMQ docs، وتوثيق Wolverine (wolverine.netlify.app) | الموديول 6 و 7 و 8 |
| رسمي Microsoft | HybridCache و EF Core Concurrency و ExecuteUpdate | الموديول 8 |
| كتاب | Google — Site Reliability Engineering | المراقبة |

---

## 5. متتبع التقدم (Progress Tracker)
| # | الموديول | الحالة | ملف الملاحظات | الـ ADR |
|---|---|---|---|---|
| 0–3 | الأساسيات | ✅ | — | — |
| 4 | Chassis و Production Baseline | ✅ | `docs/notes/04-service-baseline.md` | ADR-001 |
| 5 | Ordering DDD | ✅ | `docs/notes/05-ordering-ddd.md` | ADR-002 |
| 6 | RabbitMQ و Wolverine | ✅ | `docs/notes/06-messaging-rabbitmq.md` | ADR-003: Wolverine (MIT) |
| 7 | Outbox / Inbox | ✅ | `docs/notes/07-reliable-messaging.md` | ADR-004: Outbox + Inbox + Unique Index |
| 8 | البيانات عبر الخدمات والكاش | ✅ | `docs/notes/08-data-across-services.md` + Doc + Google Doc | ADR-005: ECST + Version + تحقق Checkout + HybridCache |
| 9 | YARP Gateway و BFF | ✅ | `docs/notes/09-api-gateway-bff.md` + Doc + Google Doc | ADR-006: YARP + Timeouts + Rate Limiting + CORS + Web.Bff |
| 10 | Identity | ⏭️ الجاي | | |
| 11 | Saga | ⬜ | | |
| 12 | gRPC | ⬜ | | |
| 13 | Observability و Aspire | ⬜ | | |
| 14 | Testing | ⬜ | | |
| 15 | CI/CD و K8s | ⬜ | | |
| 16 | Advanced و Capstone (+ Database Read Replicas) | ⬜ | | |

**سجل الجلسات:**
- 2026-11-12 · قفلنا الموديول 4، وبدأنا 5.1.
- 2026-11-17 · قفلنا الموديول 5. الدروس 6.1 و 6.2 و 6.3، و 6.4 (الأجزاء 1–3). قرار: نسيب MassTransit.
- 2026-11-18 · قفلنا 6.4 و 6.5 (Wolverine و ADR-003). اتعمل ملف ملاحظات الموديول 6، وبوست LinkedIn.
- 2026-11-19/20 · الموديول 7 كله (7.1 → 7.5)، وملف الملاحظات، و ADR-004، و PR #11 و #12.
- 2026-11-21 · الموديول 8: الدروس 8.1 و 8.2 (العقد و ChangePrice و Concurrency Token و Outbox في Catalog و `PUT /price`) و 8.3 (Handler و `PriceVersion` في السلة).
- 2026-11-22 · 8.4 (Batch + تحقق الـ Checkout، 409/503) و 8.5 (CQRS مستوى 1 و Composition) و 8.6 (HybridCache + Redis). مشكلة: basket-api في Restart loop بسبب `PendingModelChangesWarning` (Migration ناقصة، مش RabbitMQ). كل التجارب نجحت (منها Unbind للـ Queue بدل إيقاف RabbitMQ، و 4 تجارب الكاش). اتعمل ملف ملاحظات الموديول 8 (Doc + Google Doc + نسخة في المشروع) و ADR-005.
- 2026-11-23 · الموديول 9: 9.1 (ليه بوابة) و 9.2 بأجزائه التلاتة (مشروع `Gateway.API` بـ YARP، والـ compose على 5100، و RoundRobin + Active Health Checks على 3 نسخ) + الـ Mini Task للتلات Clusters. كل التجارب نجحت (2/2/2 بعد مسح env vars القديمة). أسئلة كتير عن البورتات واسم الخدمة مقابل اسم الكونتينر وترتيب الإعدادات (لازم تدخل ملف الملاحظات). اتمسح `ConnectionStrings` من appsettings في الخدمات. اتعمل Commit.
- 2026-11-24 · 9.3 كله: Timeout (504، Basket 35s)، و Rate Limiting (GlobalLimiter + checkout-route، 429 + Retry-After)، و CORS (سياسة frontend + تجربة المتصفح). مشكلة: البوابة رفضت تقوم بسبب `"Policy": " "` في HealthCheck بتاع ordering-cluster (YARP بيتحقق من الإعدادات وقت الـ Startup). المستخدم طلب إن أي Timeline يتكتب جدول مش جوه Code Block (اتعمل تعديل لمهارة arabic-english-mixed-text).
- 2026-11-24/25 · 9.4 كله: Endpoint `GET /api/products?ids=` في Catalog، ومشروع `Web.Bff` (Typed Clients + Chunk + Graceful Degradation)، والـ BFF في compose من غير ports، و `bff-route`/`bff-cluster` في البوابة. كل التجارب نجحت. أسئلة: BFF في الشركات، و BFF لخدمة واحدة، وفرق الويب والموبايل، و Chunk، و ToDictionary، و TypedResults.Problem، والفرق بين `/health/live` للبوابة و `/health/ready` للخدمات، وليه الـ Preflight للكتابة بس. اتعمل Commit.
- 2026-11-25 · 9.5: مراجعة + اختبار 7 أسئلة + ADR-006 (ملف اتبعت للمستخدم يحطه في `docs/adr/`) + ملف ملاحظات الموديول 9 (Doc + Google Doc في فولدر `microservices` + نسخة في مستندات المشروع + ملف md للريبو `docs/notes/09-api-gateway-bff.md`). الموديول 9 اتقفل.
- **اللي جاي:** الموديول 10.1 (Identity).

**قاعدة التحديث:** في آخر كل جلسة نغيّر الحالة، ونكتب سطر "إيه اللي اتعمل" وسطر "إيه اللي جاي".
