# كورس الـ Microservices على مشروع ECommerceMicroservices: الخطة والتقدم

> آخر تحديث: 2026-11-22 · مبني على `docs/PROJECT_CONTEXT.md` وعلى الكود الفعلي في الريبو وملاحظات المذاكرة.
> الملف ده نسخته في الريبو هي `docs/LEARNING_PROGRESS.md` عشان Claude Code جوه Cursor يقرأه أول كل جلسة ويحدّثه في آخرها.

---

## 0. ابدأ من هنا (لأي محادثة جديدة)

1. اقرا الملف ده كله، وبعدين `docs/adr/ADR-005-data-across-services.md` (قرارات الموديول 8)، وملف ملاحظات آخر موديول: **"الموديول 8: البيانات عبر الخدمات والكاش"** (Doc + نسخة في مستندات المشروع + Google Doc في فولدر `microservices` على Drive).
2. الموديول 8 مقفول بالكامل. ابدأ **الموديول 9** من الدرس 9.1.
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

**ثغرات معروفة لسه مفتوحة:**
- ✅ (اتقفلت) التحقق النهائي من السعر وقت الـ Checkout (8.4).
- ⚠️ مفيش Idempotency Key على مستوى الـ API لو العميل داس Checkout مرتين.
- ⚠️ مفيش حدث `ProductRemoved`: المنتج الممسوح بيفضل في السلة لحد الـ Checkout (اللي بيشيله و 409).
- ⚠️ `buyerId` في `GET /api/orders` جاي من الـ URL (الموديول 10 ياخده من الـ JWT)، ومفيش Pagination.
- ⚠️ صفحة "طلباتي" بالصور (API Composition) هتتبني في الـ BFF (الموديول 9).
- ⚠️ مفيش Unique Index على (ShoppingCartId, ProductId) في Basket (Mini Task).
- ⚠️ مفيش Alert على الـ DLQ (الموديول 13).

**إنت في:** **المرحلة الثالثة** → خلصت **الموديول 8**، والجاي **المرحلة الرابعة: الموديول 9 (YARP Gateway و BFF)**.

---

## 2. منهجية الكورس وأسلوب الشرح (مهم جدًا)

**أسلوب الشرح اللي المستخدم طالبه:**
- المستخدم **لسه بيتعلم**، وعنده خبرة سابقة في **Laravel** (Events و Listeners و Queues و `dispatch`). قارن بيها لما تفيد.
- عربي مصري بسيط، والمصطلحات الإنجليزي بين قوسين: «الـ (Exchange)».
- **التنسيق مهم جدًا:** جمل قصيرة، ومتحطش كود أو مصطلحات إنجليزي كتير ورا بعض جوه جملة عربي واحدة لأن الترتيب بيتلخبط. الأحسن خطوات مرقمة، والكود في بلوك لوحده. المستخدم بيفضّل الشرح البرمجي المباشر عن التشبيهات لما التشبيه يبقى بعيد.
- **قسّم الدرس الكبير لأجزاء صغيرة**، جزء في كل رسالة، واستنى المستخدم يقول "يلا" قبل الجزء اللي بعده.
- **اشرح الكود حتة حتة "بالبلدي"**، وابدأ بأقل كود ممكن، وأجّل التفاصيل.
- قبل كود جديد، اربطه بحاجة المستخدم عملها بإيده أو اتعلمها قبل كده (مثلًا جدول "اللي عملناه بإيدنا ↔ اللي المكتبة بتعمله").
- لكل خطوة: **ليه بنعملها، وبتحل إيه**.
- الأسئلة السريعة أثناء المذاكرة: **رد قصير 2–4 سطور** على السؤال بس.
- صحّح إجابات المستخدم بلطف: ✅ الصح، و ⚠️ أو ❌ الغلط مع السبب.
- لما المستخدم يقول "مفهمتش": اشرحها بجدول زمني (Timeline) خطوة بخطوة، ده اشتغل كويس في الموديول 8.
- **اقرا الكود الحقيقي في الريبو** قبل ما تكتب الدرس (`ahmednagi96/ECommerceMicroservices`، عام)، ومتفترضش حاجة عن المشروع. وأي حاجة مش متأكد إنها اتعملت، اسأل.
- لما خدمة تقع: **اطلب اللوج الأول** (`docker compose ps` و `docker compose logs <svc> --tail 50`) قبل أي تخمين.
- تحقق من إصدارات وتراخيص المكتبات بالبحث قبل ما تذكرها.
- المستخدم بيفضّل **أدوات مجانية/مفتوحة المصدر** تنفع في الشغل (عشان كده مفيش MassTransit).

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
**9.** YARP Gateway و BFF (ومنها صفحة "طلباتي" بالـ API Composition) · **10.** Identity (Keycloak و JWT، و `buyerId` من التوكن) · **11.** Saga (`ReserveStock` كـ Command، وبـ Wolverine) · **12.** gRPC.

### المرحلة الخامسة: التشغيل والجودة
**13.** Observability و Aspire (ومنها Alert على الـ DLQ) · **14.** Testing · **15.** CI/CD و K8s (ومنها `TypeLoadMode.Static` لـ Wolverine) · **16.** Advanced و Capstone.

---

## 4. المراجع العالمية المعتمدة
| النوع | المرجع | بنستخدمه في |
|---|---|---|
| كتاب | Sam Newman — Building Microservices (2nd ed) | الحدود، والتواصل، والنشر |
| كتاب + موقع | Chris Richardson — Microservices Patterns و microservices.io | الأنماط كلها |
| رسمي Microsoft | .NET Microservices Architecture eBook، وريبو `dotnet/eShop` | التطبيق على .NET |
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
| 9 | YARP Gateway و BFF | ⏭️ الجاي | | |
| 10 | Identity | ⬜ | | |
| 11 | Saga | ⬜ | | |
| 12 | gRPC | ⬜ | | |
| 13 | Observability و Aspire | ⬜ | | |
| 14 | Testing | ⬜ | | |
| 15 | CI/CD و K8s | ⬜ | | |
| 16 | Advanced و Capstone | ⬜ | | |

**سجل الجلسات:**
- 2026-11-12 · قفلنا الموديول 4، وبدأنا 5.1.
- 2026-11-17 · قفلنا الموديول 5. الدروس 6.1 و 6.2 و 6.3، و 6.4 (الأجزاء 1–3). قرار: نسيب MassTransit.
- 2026-11-18 · قفلنا 6.4 و 6.5 (Wolverine و ADR-003). اتعمل ملف ملاحظات الموديول 6، وبوست LinkedIn.
- 2026-11-19/20 · الموديول 7 كله (7.1 → 7.5)، وملف الملاحظات، و ADR-004، و PR #11 و #12.
- 2026-11-21 · الموديول 8: الدروس 8.1 و 8.2 (العقد و ChangePrice و Concurrency Token و Outbox في Catalog و `PUT /price`) و 8.3 (Handler و `PriceVersion` في السلة).
- 2026-11-22 · 8.4 (Batch + تحقق الـ Checkout، 409/503) و 8.5 (CQRS مستوى 1 و Composition) و 8.6 (HybridCache + Redis). مشكلة: basket-api في Restart loop بسبب `PendingModelChangesWarning` (Migration ناقصة، مش RabbitMQ). كل التجارب نجحت (منها Unbind للـ Queue بدل إيقاف RabbitMQ، و 4 تجارب الكاش). اتعمل ملف ملاحظات الموديول 8 (Doc + Google Doc + نسخة في المشروع) و ADR-005.
- **اللي جاي:** الموديول 9.1.

**قاعدة التحديث:** في آخر كل جلسة نغيّر الحالة، ونكتب سطر "إيه اللي اتعمل" وسطر "إيه اللي جاي".
