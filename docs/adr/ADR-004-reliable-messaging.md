# ADR-004: الرسائل الموثوقة (Outbox و Inbox و Idempotent Consumer)

## الحالة
مقبول · 2026-11-20

## السياق
- الـ Checkout في Basket كان بيعمل (Dual-write): ينشر الرسالة على RabbitMQ وبعدين يمسح السلة من SQL Server، ومفيش Transaction واحدة تجمعهم. فممكن يحصل طلب مكرر (لو وقعنا بعد النشر) أو طلب ضايع (لو وقعنا بعد المسح).
- التوصيل في RabbitMQ و Wolverine (At-least-once)، فالرسالة ممكن توصل أكتر من مرة. واتأكدنا بالتجربة: رسالة واحدة + Exception بعد الحفظ + Retry = 4 طلبات.
- عندنا 3 نسخ من `ordering-api` (Competing Consumers)، فأي حل بـ "شيّك وبعدين احفظ" مش آمن.

## القرار
1. **Outbox في Basket بـ Wolverine:**
   - `WolverineFx.EntityFrameworkCore` و `WolverineFx.SqlServer` (في `BuildingBlocks.Messaging`).
   - `PersistMessagesWithSqlServer(connectionString, "wolverine")` في قاعدة Basket نفسها.
   - `UseEntityFrameworkCoreTransactions()` و `Policies.UseDurableOutboxOnAllSendingEndpoints()` و `AddResourceSetupOnStartup()`.
   - الـ Checkout بيستخدم `IDbContextOutbox` + `Enroll(dbContext)` + `PublishAsync` + `SaveChangesAndFlushMessagesAsync`.
   - شلنا `EnableRetryOnFailure` من Basket، لأنه مش بيدعم الـ Transaction اللي Wolverine بيفتحها.
2. **Durable Inbox في Ordering:** `ListenToRabbitQueue(...).UseDurableInbox()`، وجداول Wolverine في Schema `wolverine` في قاعدة Ordering.
3. **Idempotent Consumer:** عمود `Orders.CheckoutId` (= `IntegrationEvent.Id`) عليه Unique Index (مفلتر على `IS NOT NULL`). لو الحفظ رجع خطأ `2601` أو `2627`، نكتب Log ونرجع عادي (Ack)، من غير Exception.
4. **سياسة الأخطاء في Ordering:**
   - `DomainException` ← الـ DLQ على طول.
   - أي Exception تاني ← `RetryWithCooldown(1s, 5s, 10s)` ثم `ScheduleRetry(1m, 5m)` ثم الـ DLQ.
5. **`ServiceLocationPolicy.AllowedButWarn` بيفضل:** صلّحنا الـ `DbContextOptions` بـ `optionsLifetime: Singleton`، بس `DomainEventDispatcher` بياخد `IServiceProvider` وده Service Location مقصود بالتصميم.
6. **ترتيب الرسائل:** مش هنعتمد عليه. أي حدث بيغيّر حالة (زي `ProductPriceChanged` في الموديول 8) هيبقى فيه `Version`.

## البدائل اللي اترفضت
- **Outbox بإيدنا** (جدول + BackgroundService، موجود في فرع `experiment/manual-outbox`): محتاج Locking لو فيه أكتر من نسخة، وتنضيف للجدول، و Polling. اتعمل للفهم بس.
- **CDC (زي Debezium):** أسرع، بس محتاج بنية تحتية كبيرة مش محتاجينها دلوقتي.
- **"شيّك وبعدين احفظ" (`AnyAsync`):** فيها Race Condition مع النسخ المتعددة.
- **Inbox بتاع Wolverine لوحده:** بيعتمد على رقم Wolverine، وبيحتفظ بالرسالة 5 دقايق بس، ومش في نفس الـ Transaction بتاعة الطلب.
- **Transactional Middleware في Ordering:** مؤجل. الـ Unique Index مغطي الفجوة.

## العواقب
- ➕ مفيش رسائل بتضيع، ومفيش طلبات مكررة من نفس الرسالة.
- ➕ الـ Checkout شغال حتى لو RabbitMQ واقع، والـ Replay من الـ DLQ بقى آمن.
- ➖ جداول زيادة في كل قاعدة، واعتماد أكبر على Wolverine.
- ➖ Basket خسر الـ Retry بتاع EF على مستوى الأوامر.
- ➖ الطلب بيتعمل بعد الـ 202 بشوية (Eventual Consistency).

## مفتوح للمستقبل
- **Idempotency Key على مستوى الـ API** لو العميل داس Checkout مرتين.
- **Alert على الـ DLQ** (الموديول 13).
- **Backfill** لعمود `CheckoutId` قبل أي Replay لرسائل قديمة في بيئة حقيقية.
