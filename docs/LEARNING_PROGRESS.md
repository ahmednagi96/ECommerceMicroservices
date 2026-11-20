# كورس الـ Microservices على مشروع ECommerceMicroservices: الخطة والتقدم

> آخر تحديث: 2026-11-20 · مبني على `docs/PROJECT_CONTEXT.md` وعلى الكود الفعلي في الريبو وملاحظات المذاكرة.
> الملف ده نسخته في الريبو هي `docs/LEARNING_PROGRESS.md` عشان Claude Code جوه Cursor يقرأه أول كل جلسة ويحدّثه في آخرها.

---

## 0. ابدأ من هنا (لأي محادثة جديدة)

1. اقرا الملف ده كله، وبعدين `docs/adr/ADR-004-reliable-messaging.md` (قرارات الموديول 7)، وملف ملاحظات آخر موديول: **"الموديول 7: الرسائل الموثوقة (Outbox و Inbox)"** (Doc + نسخة في مستندات المشروع + Google Doc في فولدر `microservices` على Drive).
2. **اسأل المستخدم الأول** عن مهام قفل الموديول 7 (القسم 1 تحت)، وبعدين ابدأ **الموديول 8** من الدرس 8.1.
3. **مكتبة الرسائل المعتمدة: Wolverine** (ADR-003)، والـ Outbox/Inbox بـ Wolverine (ADR-004). أي كود رسائل جديد يتكتب بنفس الأسلوب.
4. التزم بـ **أسلوب الشرح** في القسم 2 بالحرف.

---

## 1. إنت واقف فين دلوقتي (Snapshot)

**اللي خلصته فعلًا (✅):**
- تقسيم النظام لـ 3 خدمات: Catalog و Basket و Ordering، كل خدمة بطبقات Clean Architecture مبسطة (API / Application / Domain / Infrastructure).
- الـ (Database per Service): 3 قواعد SQL Server منفصلة، ومفيش Foreign Keys بين الخدمات.
- Docker: Dockerfile متعدد المراحل، و docker-compose، و YAML anchors (`x-sqlserver` و `x-api` و `x-api-env`)، و healthcheck للقواعد والـ APIs، وحدود موارد، وشبكة bridge اسمها `ecommerce`، والأسرار عن طريق `env_file`.
- .NET 10، و (Central Package Management) في `Directory.Packages.props`، و `Directory.Build.props` بيحط TargetFramework و Nullable و ImplicitUsings لكل المشاريع.
- الاتصال المتزامن من Basket لـ Catalog بـ Typed HttpClient **وقت إضافة منتج للسلة** + `AddStandardResilienceHandler`.
- الموديول 4 ✅ (`BuildingBlocks/ServiceDefaults`: Health Checks و Serilog و ProblemDetails و Fail-fast migrations).
- الموديول 5 ✅ (Ordering بـ DDD: `Order` Aggregate و `Money` و `Address` و Domain Events و `OrderId` و `IOrderRepository`).
- الموديول 6 ✅ (RabbitMQ و Wolverine، واندمج في `main` بـ PR #9، و ADR-003 في الريبو).
- الموديول 7 ✅ (الدروس كلها اتشرحت، وملف الملاحظات اتعمل، والتفاصيل تحت).

**حالة الكود بعد الموديول 6 (الأساس، على `main`):**
- **RabbitMQ** في compose: `rabbitmq:4-management`، و `rabbitmq.env`، و healthcheck، و volume، وشبكة `ecommerce`.
- **`BuildingBlocks.Contracts`**: `IntegrationEvent` (`Id` و `OccurredOn` بـ `init`) و `BasketCheckedOutIntegrationEvent(BuyerId, Currency, ShippingAddress, Items)` و `CheckoutItem(Guid ProductId, ProductName, UnitPrice, Quantity)`. **مفيش `Total`** في العقد.
- **`ProductId` نوعه `Guid` في كل الخدمات**.
- باكدجات Wolverine متسجلة في `BuildingBlocks.Messaging.csproj` (الإصدار 6.61.0 في CPM).
- Basket: `PublishMessage<BasketCheckedOutIntegrationEvent>().ToRabbitExchange("basket-checked-out")`.
- Ordering: `DeclareExchange(...).BindQueue("ordering.basket-checked-out")` + `ListenToRabbitQueue(...)`. والـ Handler في `Ordering.API/IntegrationEvents/BasketCheckedOutHandler`.
- الـ DLQ: `wolverine-dead-letter-queue`. و ordering-api بتتعمل `--scale ordering-api=3`.

**اللي اتغير في الموديول 7 (فرع `module_7`، اتأكد من المستخدم إنه اتطبق واندمج):**
- **Basket (Outbox):** `WolverineFx.EntityFrameworkCore` + `WolverineFx.SqlServer` (6.61.0، في `BuildingBlocks.Messaging`). `PersistMessagesWithSqlServer(cs, "wolverine")` + `UseEntityFrameworkCoreTransactions()` + `Policies.UseDurableOutboxOnAllSendingEndpoints()` + `AddResourceSetupOnStartup()`. الـ DbContext بـ `optionsLifetime: Singleton` و **من غير** `EnableRetryOnFailure`. الـ Checkout بـ `[FromServices] IDbContextOutbox` + `Enroll(dbContext)` + `PublishAsync` + `SaveChangesAndFlushMessagesAsync`، و `IMessageBus` اتشال من الكنترولر.
- **Ordering (Inbox + Idempotency):** `PersistMessagesWithSqlServer(cs, "wolverine")` + `ListenToRabbitQueue(...).UseDurableInbox()` + `AddResourceSetupOnStartup()`. الـ DbContext بـ `optionsLifetime: Singleton` ومعاه `EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: 2s)` صغير عشان الـ Retry ميتضربش في بعضه (Retry Amplification). `Order.CheckoutId` (`Guid?`) + `HasIndex(o => o.CheckoutId).IsUnique()` (EF بيضيف فلتر `IS NOT NULL`) + Migration `AddCheckoutIdToOrders`. الـ Handler بيمرر `checkoutId: e.Id`، ولو `DbUpdateException` جواها `SqlException` 2601/2627 ← Log + `return`.
- **سياسة الأخطاء (7.5):** `DomainException` ← `MoveToErrorQueue()`. وأي Exception ← `RetryWithCooldown(1s,5s,10s).Then.ScheduleRetry(1m,5m).Then.MoveToErrorQueue()`.
- **`ServiceLocationPolicy.AllowedButWarn` بيفضل بقرار:** جربنا `NotAllowed` وطلع سببين: `DbContextOptions` (اتصلح بـ Singleton) و `DomainEventDispatcher` اللي بياخد `IServiceProvider` (Service Location مقصود).
- **النسخة اليدوية من الـ Outbox** (جدول `OutboxMessages` + `OutboxProcessor`) في فرع `experiment/manual-outbox` كمرجع تعليمي، من غير PR.

**مهام قفل الموديول 7 (اسأل المستخدم):**
- تطبيق سياسة الأخطاء الجديدة و EF Retry الأصغر في Ordering، وتجربة `DomainException` ← DLQ على طول ← Replay.
- تجربة RabbitMQ واقع مع الـ Outbox بتاع Wolverine.
- إضافة `docs/adr/ADR-004-reliable-messaging.md` للريبو (اتبعت ملفه في الشات).
- ✅ ملف ملاحظات الموديول 7 اتعمل (Doc + نسخة Markdown في مستندات المشروع + Google Doc في Drive). باقي: انسخه لـ `docs/notes/07-reliable-messaging.md`.
- استبدال `docs/PROJECT_CONTEXT.md` و `docs/LEARNING_PROGRESS.md` في الريبو بالنسخ المحدّثة (اتبعتوا في الشات 2026-11-20).
- Commit و PR لفرع `module_7` ودمجه في `main`.
- (من الموديول 6 ولسه مش في الريبو) `docs/notes/06-messaging-rabbitmq.md`.

**ثغرات معروفة لسه مفتوحة:**
- ⚠️ التحقق النهائي من السعر وقت الـ Checkout مش موجود (الموديول 8).
- ⚠️ مفيش Idempotency Key على مستوى الـ API لو العميل داس Checkout مرتين.
- ⚠️ مفيش Alert على الـ DLQ (الموديول 13).
- ⚠️ في الـ compose على `main`: basket-api و ordering-api مش عاملين `depends_on: rabbitmq: service_healthy`، و catalog-api عنده `RabbitMq__HostName` وهو مش بيستخدم RabbitMQ (اتأكد مع المستخدم وصلّح).

**إنت في:** **المرحلة الثالثة** → خلصت **الموديول 7**، والجاي **الموديول 8: البيانات عبر الخدمات والكاش**.

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
- **اقرا الكود الحقيقي في الريبو** قبل ما تكتب الدرس (`ahmednagi96/ECommerceMicroservices`)، ومتفترضش حاجة عن المشروع. وأي حاجة مش متأكد إنها اتعملت، اسأل.
- تحقق من إصدارات وتراخيص المكتبات بالبحث قبل ما تذكرها.
- المستخدم بيفضّل **أدوات مجانية/مفتوحة المصدر** تنفع في الشغل (عشان كده مفيش MassTransit).

**شكل كل درس:** المشكلة → الفكرة + رسمة/تشبيه → الكود في المشروع → المقايضات → إمتى ما تستخدمهاش → تجربة عملية → سؤال مراجعة + سؤال إنترفيو.

**تعريف الإنجاز لكل موديول:** كود شغال بـ `docker compose up`، وملف ملاحظات (الدروس + كل أسئلة المستخدم وإجاباتها + المشاكل اللي قابلها وحلولها + ملخص + اختبر نفسك)، و ADR. ملف الملاحظات بيتعمل Doc ونسخة Google Doc في فولدر `microservices` على Drive.

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

### المرحلة الثالثة: الاتصال غير المتزامن والبيانات الموزعة
**6. ✅ أساسيات الرسائل: RabbitMQ و Wolverine**
- 6.1 Sync/Async و Event/Command و Pub/Sub · 6.2 Exchange و Queue و Binding و Competing Consumers · 6.3 `RabbitMQ.Client` الخام: Ack و Nack و DLQ و Prefetch · 6.4 Contracts و `IEventBus` بإيدنا وأول Checkout · 6.5 Wolverine و ADR-003.

**7. ✅ الرسائل الموثوقة (Reliable Messaging)**
- 7.1 Dual-write (الترتيب بيحدد نوع المصيبة: تكرار ولا ضياع)، و At-least-once وليه Exactly-once مستحيل. تجربة: Exception بعد الحفظ = 4 طلبات.
- 7.2 Transactional Outbox بإيدنا: `OutboxMessage` (Id = Id الحدث، Type، Content، ProcessedOn، Error) + `OutboxProcessor` (BackgroundService، Polling كل 5 ثواني، Scope جديد، Batch 20، Save بعد كل رسالة)، و `SendInline` مقابل Buffered، ومشكلة النسخ المتعددة. (فرع `experiment/manual-outbox`)
- 7.3 Outbox بـ Wolverine: `IDbContextOutbox` و `SaveChangesAndFlushMessagesAsync`، والإرسال الفوري + Durability Agent (Polling للإنقاذ، مش CDC)، وشيل `EnableRetryOnFailure` من Basket.
- 7.4 Idempotent Consumer: فخ "شيّك وبعدين احفظ"، و `CheckoutId` + Unique Index، و Durable Inbox (رقم Wolverine في الـ Headers، 5 دقايق)، والفرق بين Inbox Pattern و Idempotent Consumer، وتجربة `ServiceLocationPolicy.NotAllowed`.
- 7.5 أنواع الأخطاء و Retry/ScheduleRetry و Retry Amplification و Jitter، والـ DLQ والـ Replay الآمن (Poison Message و Backfill)، وترتيب الرسائل (Version و Gap Detection)، و ADR-004.

**8. ⏭️ البيانات عبر الخدمات والكاش** — الجاي: `ProductPriceChanged` (فيه `Version`) → نسخة محلية في Basket، والتحقق النهائي من السعر وقت الـ Checkout، و CQRS Read Models، و API Composition، و Redis. (و Catalog هتحتاج Outbox زي Basket.)

### المرحلة الرابعة: الحافة والأمان وسير العمل
**9.** YARP Gateway · **10.** Identity (Keycloak و JWT) · **11.** Saga (`ReserveStock` كـ Command، وبـ Wolverine) · **12.** gRPC.

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
| كتاب | Hohpe & Woolf — Enterprise Integration Patterns | الرسائل |
| رسمي | RabbitMQ docs، وتوثيق Wolverine (wolverine.netlify.app) | الموديول 6 و 7 |
| كتاب | Google — Site Reliability Engineering | المراقبة |

---

## 5. متتبع التقدم (Progress Tracker)
| # | الموديول | الحالة | ملف الملاحظات | الـ ADR |
|---|---|---|---|---|
| 0–3 | الأساسيات | ✅ | — | — |
| 4 | Chassis و Production Baseline | ✅ | `docs/notes/04-service-baseline.md` | ADR-001 |
| 5 | Ordering DDD | ✅ | `docs/notes/05-ordering-ddd.md` | ADR-002 |
| 6 | RabbitMQ و Wolverine | ✅ | `docs/notes/06-messaging-rabbitmq.md` | ADR-003: Wolverine (MIT) |
| 7 | Outbox / Inbox | ✅ (باقي مهام القفل) | Doc + Google Doc ✅ · `docs/notes/07-reliable-messaging.md` (لسه في الريبو) | ADR-004: Outbox + Inbox + Unique Index |
| 8 | Data Replication و Redis | ⏭️ الجاي | | |
| 9 | YARP Gateway | ⬜ | | |
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
- 2026-11-19/20 · الموديول 7 كله (7.1 → 7.5). تجارب: 4 طلبات من رسالة واحدة ← طلب واحد بعد الـ Unique Index، و `NotAllowed` كشف `DbContextOptions` و `DomainEventDispatcher`. قرارات في ADR-004.
- 2026-11-20 (بعد الضهر) · أسئلة متابعة: تقليل EF Retry بدل شيله، و Gap Detection (Version ناقصة ← Retry). اتعمل ملف ملاحظات الموديول 7 (Doc + Google Doc + نسخة في المشروع) فيه رسمة رحلة الـ Checkout، واتحدّث `PROJECT_CONTEXT.md` (كان واقف عند 2026-11-08).
- **اللي جاي:** مهام قفل الموديول 7 (القسم 1)، وبعدين الموديول 8.1.

**قاعدة التحديث:** في آخر كل جلسة نغيّر الحالة، ونكتب سطر "إيه اللي اتعمل" وسطر "إيه اللي جاي".
