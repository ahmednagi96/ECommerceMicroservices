# الموديول 7: الرسائل الموثوقة (Outbox و Inbox)

Nov 20, 2026 · @Ahmed Nagi

كورس الـ (Microservices) على مشروع ECommerceMicroservices · المرحلة التالتة: الاتصال غير المتزامن والبيانات الموزعة

## مقدمة: الموديول ده حل مشكلة إيه؟

بعد الموديول ده، الـ (Checkout) **مش بيضيّع طلبات ومش بيكررها**، حتى لو (RabbitMQ) واقع، أو Ordering وقعت في النص، أو الرسالة وصلت كذا مرة. قبله، كانت رسالة واحدة ممكن تعمل 4 طلبات (واتأكدت بنفسك بالتجربة).

السؤال اللي الموديول كله بيجاوبه: **إزاي أضمن إن الرسالة متضيعش، ولو اتكررت متعملش أثر مرتين؟**

### خريطة الموديول

| الدرس | الموضوع | بيحل مشكلة إيه |
| --- | --- | --- |
| 7.1 | (Dual-write) و (At-least-once) | نفهم ليه الكود القديم بيضيّع أو بيكرر |
| 7.2 | (Transactional Outbox) بإيدنا | الحفظ والنشر يحصلوا مع بعض أو ميحصلوش خالص |
| 7.3 | الـ (Outbox) بـ (Wolverine) | نفس الفكرة من غير كود نكتبه ونصونه |
| 7.4 | (Idempotent Consumer) و (Inbox) | الرسالة لو وصلت مرتين، الطلب يتعمل مرة واحدة |
| 7.5 | الأخطاء والـ (DLQ) والترتيب و ADR-004 | نتعامل مع كل نوع خطأ صح، ونعمل (Replay) بأمان |

### الموديول في مشكلتين وحلين

| المشكلة | بتحصل فين | الحل | الدرس |
| --- | --- | --- | --- |
| رسالة **بتضيع** | Basket (اللي بيبعت) | (Outbox) | 7.2 و 7.3 |
| رسالة **بتتكرر** | Ordering (اللي بيستقبل) | (Unique Index) + (Inbox) | 7.4 |

### إزاي تذاكر الملف ده

- كل درس فيه: **المشكلة ← الفكرة ← الكود ← المقايضات ← إمتى ما تستخدمهاش**.
- بعد كل درس قسم **أسئلة وأجوبة**: الأسئلة اللي إنت سألتها فعلًا وإحنا بنذاكر، وإجاباتك وتصحيحها.
- قسم **المشاكل اللي قابلتها**، و**الملخص السريع**، و**اختبر نفسك** في الآخر.
- الكود مكتوب بنفس أسامي مشروعك: `BasketController` و `BasketCheckedOutHandler` و `OrderConfiguration`.

## الدرس 7.1: المشكلة بالتفصيل

**الخلاصة:** "احفظ" و"ابعت" عمليتين في نظامين مختلفين، ومفيش (Transaction) واحدة تجمعهم. والرسالة نفسها ممكن توصل أكتر من مرة.

### 1️⃣ الكتابة المزدوجة (Dual-write) في كودك

```csharp
// Old Checkout (before Module 7)
await bus.PublishAsync(checkoutEvent);          // write #1: RabbitMQ
dbContext.ShoppingCarts.Remove(cart);
await dbContext.SaveChangesAsync(cancellationToken); // write #2: SQL Server
```

تشبيه: كاشير بيزعق للمطبخ "واحد برجر!" وبعدين يسجّل الأوردر. لو الكهربا قطعت بينهم، المطبخ هيعمل برجر محدش حاسب عليه.

**الترتيب بيحدد نوع المصيبة:**

| الترتيب | لو الخدمة وقعت في النص | النتيجة |
| --- | --- | --- |
| ابعت ← احفظ (القديم) | الرسالة راحت، والسلة ممسحتش | العميل يدوس (Checkout) تاني ← **طلب مكرر** 🔁 |
| احفظ ← ابعت | السلة اتمسحت، والرسالة مراحتش | **طلب ضايع** 💸 |

مينفعش نحطهم في (Transaction) واحدة، لأن الـ (Transaction) بتاعة (SQL Server) مبتغطيش (RabbitMQ).

🔗 **من Laravel:** نفس مشكلة `dispatch()` جوه `DB::transaction()`. و `afterCommit()` بيحل نصها بس: لو السيرفر وقع بعد الـ (Commit) وقبل الإرسال، الرسالة ضاعت.

**إمتى الخدمة بتقع في النص فعلًا؟** (Deploy) أو (Restart)، و (OOM)، والشبكة فصلت، والداتابيز واقعة. مع آلاف الطلبات، هتحصل أكيد.

### 2️⃣ التوصيل "مرة على الأقل" (At-least-once)

| النوع | معناه | النتيجة |
| --- | --- | --- |
| (At-most-once) | امسح الرسالة الأول وبعدين اشتغل | ممكن **تضيع** |
| (At-least-once) | اشتغل وبعدين (Ack) | ممكن **تتكرر**. وده اللي (RabbitMQ) بيديهولك |
| (Exactly-once) | مرة بالظبط | **مستحيل** عبر الشبكة |

**ليه (Exactly-once) مستحيل؟**

```
Ordering                         RabbitMQ
   |  <---- message (Checkout) ----  |
   |  save Order in DB ✅            |
   |  ---- Ack ----> ❌ (lost / crash)|
   |                                 |  "No Ack... processed or not?"
   |  <---- same message again ----  |  re-deliver to be safe
   |  save Order AGAIN               |  => duplicate order
```

(RabbitMQ) مش قادر يفرّق بين "اتعالجت والـ (Ack) ضاع" و "متعالجتش". وعشان تضمن (Exactly-once) لازم "احفظ الطلب" و "ابعت (Ack)" يحصلوا في خطوة واحدة في نظامين مختلفين. يعني **نفس مشكلة الـ (Dual-write)** بس من ناحية الـ (Consumer).

**التكرار بييجي منين في مشروعنا؟**

1. Ordering وقعت بعد الحفظ وقبل الـ (Ack)، فـ (RabbitMQ) يديها لنسخة تانية من الـ 3 نسخ.
2. الـ (Retry) بتاع (Wolverine) لو حصل (Exception) بعد `SaveChangesAsync`.
3. من ناحية Basket: العميل داس (Checkout) مرتين.

**الحل اللي الموديول ماشي بيه:**

> (At-least-once) في التوصيل + (Idempotent Consumer) = النتيجة كأنها (Exactly-once)

### 🧪 تجربة: رسالة واحدة = 4 طلبات

ضفت `throw new Exception("Simulated crash after save")` بعد `SaveChangesAsync` في الـ (Handler).

- **النتيجة:** 4 طلبات (المحاولة الأولى + 3 (Retry): 1s و 5s و 10s)، والرسالة في الآخر راحت الـ (DLQ). ✅ اتعملت.

### 🙋 أسئلة وأجوبة الدرس 7.1

**❓ سؤال 1: هل `RetryWithCooldown` بيحل الـ (Dual-write)؟**

إجابتك: لأ، ملوش علاقة بمسح السلة. ✅ الـ (Retry) في Ordering والمشكلة في Basket.

⚠️ التظبيط: إنت خلطت بين آليتين:

|  | `RetryWithCooldown` | إعادة الإرسال من (RabbitMQ) |
| --- | --- | --- |
| مين بيعملها؟ | (Wolverine) جوه Ordering | (RabbitMQ) نفسه |
| إمتى؟ | الـ (Handler) رمى (Exception) | الـ (Consumer) اختفى من غير (Ack) |
| بتحصل إزاي؟ | يشغّل الـ (Handler) تاني بعد 1s ثم 5s ثم 10s | يرجّع الرسالة للطابور (مفيهاش عداد ثواني) |

💡 الـ (Retry) نفسه ممكن يكرر الطلب لو الـ (Exception) حصل بعد الحفظ. وده اللي شفته في التجربة.

**❓ سؤال 2: إيه اللي في الرسالة يخلّي Ordering تعرف إنها شافتها قبل كده؟**

إجابتك: `checkoutId` (اللي هو `IntegrationEvent.Id`)، لأنه نفس الحدث ومبيتغيرش. ✅ بيتولّد مرة واحدة في Basket، وأي إعادة إرسال بنفس الرقم.

⚠️ حالة مش هيلحقها: العميل داس (Checkout) مرتين، فهيبقى حدثين برقمين مختلفين.

**🎤 سؤال إنترفيو: ليه أغلب أنظمة الرسائل (At-least-once) مش (Exactly-once)؟**

إجابتك كانت بتوصف (At-most-once): "امسح من الطابور وبعدين اشتغل، فلو حصل عطل تضيع". ⚠️ الكلام صح بس بيوصف حاجة تانية.

> الإجابة النموذجية: "لأن الـ (Broker) مش قادر يفرّق بين رسالة اتعالجت والـ (Ack) بتاعها ضاع، ورسالة متعالجتش خالص. فبيبعتها تاني، لأن التكرار أهون من الضياع. والحل إني أخلي الـ (Consumer) (Idempotent)، بـ (MessageId) أو (Unique Constraint)، فتبقى النتيجة كأنها (Exactly-once)."

## الدرس 7.2: (Transactional Outbox) بإيدنا

**الخلاصة:** بدل ما نكتب في مكانين، نكتب في **قاعدة البيانات بس**. الرسالة بتتحفظ في جدول في نفس الـ (Transaction)، وعامل في الخلفية يبعتها بعدين. النسخة دي للفهم بس، وموجودة في فرع `experiment/manual-outbox`.

### 1️⃣ الفكرة

تشبيه: **صندوق الصادر في الإيميل**. النت فاصل؟ الإيميل بيقعد في (Outbox)، ويتبعت لوحده أول ما النت يرجع.

```
Checkout ──► 1) Delete basket         ┐
             2) Insert Outbox row     ├── ONE SaveChanges = ONE transaction
             3) Return 202            ┘

Background worker (every 5 seconds):
   a) Read rows where ProcessedOn IS NULL
   b) Publish each one to RabbitMQ
   c) Set ProcessedOn = now
```

| حصل إيه؟ | النتيجة |
| --- | --- |
| وقعنا **قبل** الـ (Commit) | ولا حاجة اتحفظت، والعميل يجرّب تاني ✅ |
| وقعنا **بعد** الـ (Commit) | الرسالة في الجدول، والعامل هيبعتها ✅ |
| (RabbitMQ) واقع | الرسالة مستنية، والعامل يفضل يحاول ✅ |
| العامل بعت ووقع قبل ما يعلّم | هيبعتها **تاني** 🔁 (عشان كده محتاجين 7.4) |

🔗 **من Laravel:** `dispatch()` جوه `DB::transaction()` مع `database` (Queue Driver) على نفس الداتابيز = تقريبًا (Outbox). جدول `jobs` هو الصندوق، و `queue:work` هو العامل.

### 2️⃣ الجدول: `OutboxMessage`

```csharp
// Basket.Infrastructure/Outbox/OutboxMessage.cs
public class OutboxMessage
{
    public Guid Id { get; set; }               // Same Id as the IntegrationEvent
    public string Type { get; set; } = "";     // Which C# type to rebuild later
    public string Content { get; set; } = "";  // The event itself as JSON
    public DateTime OccurredOn { get; set; }
    public DateTime? ProcessedOn { get; set; } // null = not sent yet
    public string? Error { get; set; }         // Last error if publishing failed

    public static OutboxMessage Create(Guid id, DateTime occurredOn, object message) => new()
    {
        Id = id,
        Type = message.GetType().AssemblyQualifiedName!,
        Content = JsonSerializer.Serialize(message, message.GetType()),
        OccurredOn = occurredOn
    };
}
```

- الكلاس في (Infrastructure) مش (Domain)، لأنه تفصيلة تقنية مش بيزنس.
- بياخد `object` عشان `Basket.Infrastructure` مش عامل (Reference) لـ `Contracts`.
- ضفنا `DbSet<OutboxMessage> OutboxMessages` للـ `BasketDbContext` + (Migration) اسمها `AddOutboxMessages`.

### 3️⃣ الـ (Checkout) الجديد

```csharp
// 3) Don't publish now: put the message in the Outbox table
dbContext.OutboxMessages.Add(
    OutboxMessage.Create(checkoutEvent.Id, checkoutEvent.OccurredOn, checkoutEvent));

// 4) Delete the basket (and its items)
dbContext.ShoppingCartItems.RemoveRange(cart.Items);
dbContext.ShoppingCarts.Remove(cart);

// 5) ONE SaveChanges = ONE transaction (outbox row + basket delete)
await dbContext.SaveChangesAsync(cancellationToken);
```

وشلنا `bus.PublishAsync` و `IMessageBus` من الكنترولر.

### 4️⃣ العامل: `OutboxProcessor`

```csharp
// Basket.API/Outbox/OutboxProcessor.cs (core loop)
while (!stoppingToken.IsCancellationRequested)
{
    try { await ProcessBatchAsync(stoppingToken); }
    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
    { logger.LogError(ex, "Outbox processing failed"); }

    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
}

// inside ProcessBatchAsync, for each unsent message:
var type = Type.GetType(message.Type, throwOnError: true)!;
var @event = JsonSerializer.Deserialize(message.Content, type)!;
await bus.PublishAsync(@event);
message.ProcessedOn = DateTime.UtcNow;
await db.SaveChangesAsync(ct); // after EACH message
```

| الجزء | ليه |
| --- | --- |
| `Task.Delay(5s)` | من غيره الـ (while) هتلف آلاف المرات في الثانية وتضرب الداتابيز |
| `try/catch` الكبيرة | أول خطأ ميوقّفش العامل للأبد |
| `CreateScope()` | العامل (Singleton) والـ `DbContext` (Scoped) |
| `Take(20)` | منحمّلش آلاف الرسايل في الذاكرة |
| `Type` + `Deserialize` | يرجّع الـ (JSON) لـ (Object) حقيقي |
| `SaveChangesAsync` بعد كل رسالة | لو وقع في النص، اللي اتبعتوا مش هيتبعتوا تاني |

واتسجل في `Program.cs`: `builder.Services.AddHostedService<OutboxProcessor>();`

### 5️⃣ تفصيلة مهمة: (Buffered) مقابل (Inline)

- **(Buffered)** (الافتراضي في (Wolverine)): `PublishAsync` بيحط الرسالة في طابور جوه الـ (RAM) ويرجع فورًا. لو الخدمة وقعت بعد ما علّمنا `ProcessedOn`، الرسالة **ضاعت**.
- **(Inline)**: `PublishAsync` بيستنى لحد ما (RabbitMQ) يقول "استلمت"، وبعدين بس نعلّم.

```csharp
opts.PublishMessage<BasketCheckedOutIntegrationEvent>()
    .ToRabbitExchange("basket-checked-out")
    .SendInline();
```

### 6️⃣ المقايضات: ليه هنسيب النسخة اليدوية

- **أكتر من نسخة من Basket:** كل نسخة فيها عامل، والاتنين ممكن يقروا نفس الصف ويبعتوه مرتين. الحل (Locking) في (SQL).
- **الجدول بيكبر للأبد:** العامل بيعلّم بس ومبيمسحش.
- **تأخير لحد 5 ثواني** بسبب الـ (Polling).
- **رسالة بايظة دايمًا** هتتجرب كل 5 ثواني للأبد.

### 🧪 تجربة

وقّف (RabbitMQ) ← (Checkout) يرجع 202 ← الصف في الجدول و `Error` فيه رسالة ← شغّل (RabbitMQ) ← خلال ثواني الطلب يظهر في Ordering.

### 🙋 أسئلة وأجوبة الدرس 7.2

**❓ سؤال 3: ليه جدول الـ (Outbox) لازم يكون في قاعدة Basket نفسها؟**

إجابتك: عشان مسح السلة وإضافة الصف يبقوا في (Transaction) واحدة. ✅ ولو في قاعدة تانية، نرجع للـ (Dual-write) بين قاعدتين. 💡 وكمان قاعدة مشتركة بتكسر (Database per Service).

**❓ سؤال 4: ليه محتاج أكتب `Type`؟**

الـ `Content` نص (JSON) بس ومبيقولش هو أنهي كلاس. العامل محتاج `Deserialize(content, type)`. و (Wolverine) بيختار الـ (Exchange) **حسب نوع الرسالة**، فلازم الكلاس الصح. والجدول هيبقى فيه أنواع كتير. `Type` هو الـ "ليبل" على الكرتونة.

**❓ سؤال 5: ليه `Id` بتاع الصف = `Id` الحدث؟**

إجابتك: رقم موحد طول الرحلة، والـ (Inbox) يعرف بيه التكرار. ✅

⚠️ التظبيط: مش الصف كله اللي بيروح الطابور، الـ `Content` بس، والـ `Id` اللي بيوصل هو اللي جوه الـ (JSON). فايدة إن عمود الجدول نفس الرقم: (Tracing) برقم واحد في كل حتة، والـ (Primary Key) بيرفض نفس الحدث مرتين. والرسالة ممكن "توصل" مرتين، بس "تتنفذ" مرة واحدة.

**❓ سؤال 6: لو نسختين من Basket، التكرار بيحصل إزاي والصف اتسجل مرة؟**

التسجيل مرة واحدة ✅. التكرار في **الإرسال**: عاملين قروا نفس الصف و `ProcessedOn` لسه `NULL`، فالاتنين بعتوه.

**❓ سؤال 7: لو شلت `Task.Delay` هيلف كل أد إيه؟**

من غير أي انتظار. الـ (BackgroundService) معناها "شغال في الخلفية" بس، **مفيهاش توقيت جاهز**. ولو `ExecuteAsync` خلصت، العامل بيقف خالص.

**❓ سؤال 8: الفرع التجريبي أعمل فيه إيه؟ والـ (PR)؟**

ارفعه على الريبو (`git push -u origin experiment/manual-outbox`) ومتدمجوش. ومتفتحش (PR)، لأن الـ (PR) معناه "عايز أدمج". وابدأ 7.3 من فرع `module_7` من (main).

## الدرس 7.3: الـ (Outbox) بـ (Wolverine)

**الخلاصة:** نفس فكرة 7.2 بالظبط، بس (Wolverine) بينفذها: بيحفظ الرسالة مع مسح السلة في (Transaction) واحدة، ويبعتها فورًا بعد الـ (Commit).

### 1️⃣ اللي عملناه بإيدنا مقابل (Wolverine)

| اللي عملناه في 7.2 | في (Wolverine) |
| --- | --- |
| جدول `OutboxMessages` | `wolverine_outgoing_envelopes` في (Schema) اسمها `wolverine` |
| عامل كل 5 ثواني | إرسال **فوري** بعد الـ (Commit) + عامل إنقاذ (Durability Agent) |
| `SendInline` | وضع (Durable) |
| مشكلة النسختين | بيتعامل معاها لوحده |
| العامل بيعلّم (`ProcessedOn`) | بيمسح الصف أول ما الرسالة توصل |

### 2️⃣ الباكدجات

في `Directory.Packages.props` (نفس إصدار باكدجات (Wolverine) التانية: 6.61.0)، وفي `BuildingBlocks.Messaging.csproj`:

```xml
<PackageReference Include="WolverineFx.EntityFrameworkCore" />
<PackageReference Include="WolverineFx.SqlServer" />
```

| الباكدج | بيعمل إيه |
| --- | --- |
| `WolverineFx.SqlServer` | يحفظ الرسايل في (SQL Server) |
| `WolverineFx.EntityFrameworkCore` | يدخل جوه نفس الـ (Transaction) بتاعة الـ (DbContext) |

### 3️⃣ `Program.cs` بتاع Basket

```csharp
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

builder.Services.AddDbContext<BasketDbContext>(
    options => options.UseSqlServer(connectionString),   // EnableRetryOnFailure removed
    optionsLifetime: ServiceLifetime.Singleton);

builder.Host.UseWolverine(opts =>
{
    opts.UseRuntimeCompilation();
    opts.UseRabbitMq(f => { /* same as before */ }).AutoProvision();

    opts.PersistMessagesWithSqlServer(connectionString, "wolverine"); // 1) outbox tables in Basket's DB
    opts.UseEntityFrameworkCoreTransactions();                        // 2) join EF Core's transaction
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();            // 3) every outgoing message is durable

    opts.PublishMessage<BasketCheckedOutIntegrationEvent>()
        .ToRabbitExchange("basket-checked-out");
});

builder.Services.AddResourceSetupOnStartup(); // 4) create Wolverine's tables at startup
```

- **`optionsLifetime: Singleton`:** (Wolverine) بيطلبها كتحسين في الأداء. الـ (DbContext) نفسه لسه (Scoped).
- **شلنا `EnableRetryOnFailure`:** (Wolverine) بيفتح (Transaction) بنفسه، والـ (Retry) بتاع EF مش بيدعم (Transaction) المستخدم فاتحها. لو سبته: `SqlServerRetryingExecutionStrategy does not support user-initiated transactions`.
- **(Schema) لوحدها `wolverine`:** تنظيم، ومفيش تعارض أسماء، وكل واحد مسؤول عن جداوله ((EF Migrations) لجداولك، و (Wolverine) لجداوله).

### 4️⃣ الـ (Checkout) بـ `IDbContextOutbox`

```csharp
[HttpPost("checkout")]
public async Task<IActionResult> Checkout(
    CheckoutRequestDTO request,
    [FromServices] IDbContextOutbox outbox,
    CancellationToken cancellationToken)
{
    // 1) Get the basket + 2) build the event (same as before)

    outbox.Enroll(dbContext);                  // 3) use OUR DbContext (same transaction)
    await outbox.PublishAsync(checkoutEvent);  // 4) kept in memory, NOT sent yet

    dbContext.ShoppingCartItems.RemoveRange(cart.Items); // 5) delete the basket
    dbContext.ShoppingCarts.Remove(cart);

    await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken); // 6) one transaction, THEN send
    return Accepted(new { checkoutId = checkoutEvent.Id });
}
```

وشلنا `IMessageBus bus` من الـ (Constructor).

| السطر | بيعمل إيه | يقابل إيه في 7.2 |
| --- | --- | --- |
| `Enroll` | "استخدم الـ (DbContext) ده" | إضافة الرسالة على نفس الـ `dbContext` |
| `outbox.PublishAsync` | يسجّل الرسالة في قايمة في الذاكرة | `OutboxMessages.Add(...)` |
| `SaveChangesAndFlushMessagesAsync` | يحفظ الاتنين في (Transaction) واحدة، وبعد الـ (Commit) يبعت فورًا | `SaveChangesAsync` + العامل |

```
Checkout
  ├─ PublishAsync      → message kept in memory (not sent)
  ├─ Remove basket     → tracked by EF (not saved)
  └─ SaveChangesAndFlushMessagesAsync
        ├─ BEGIN TRANSACTION
        │    DELETE basket
        │    INSERT INTO wolverine.wolverine_outgoing_envelopes
        ├─ COMMIT ✅
        └─ send to RabbitMQ → success? delete the row
                             → failed?  row stays, Wolverine retries later
```

### 5️⃣ مين بيبعت الرسالة فعلًا؟ طريقين

1. **الطريق السريع (من غير Polling):** جوه `SaveChangesAndFlushMessagesAsync` نفسها، بعد الـ (Commit) على طول، من القايمة اللي في الذاكرة.
2. **طريق الإنقاذ (بـ Polling):** عامل داخلي اسمه (Durability Agent) بيلف على الجدول ويبعت اللي فضل (الخدمة وقعت، أو (RabbitMQ) كان واقع). ده اللي يقابل `OutboxProcessor`.

الكود ده جوه المكتبة، وبيتسجل أوتوماتيك مع `UseWolverine`. و (Wolverine) **مش بيستخدم (CDC)**:

|  | (Polling) | (CDC) زي (Debezium) |
| --- | --- | --- |
| بيعرف الجديد إزاي | يسأل الجدول كل شوية | يقرا سجل العمليات (Transaction Log) |
| السرعة والحمل | أبطأ شوية | أسرع وأخف على الداتابيز |
| التكلفة | بسيط | بنية تحتية زيادة |

### ⚠️ أخطاء شائعة

1. **`bus.PublishAsync` بدل `outbox.PublishAsync`:** الرسالة تتبعت برّه الـ (Transaction)، وترجع للـ (Dual-write).
2. **`dbContext.SaveChangesAsync` بدل `SaveChangesAndFlushMessagesAsync`:** السلة تتمسح، والرسالة لا تتحفظ ولا تتبعت. طلب ضايع.

### 🧪 تجارب

- **الطبيعي:** الطلب يظهر في Ordering، والجدول يرجع فاضي.
- **(RabbitMQ) واقع:** 202 عادي، الرسالة في `wolverine_outgoing_envelopes`، أخطاء في اللوجز. شغّله تاني ← الرسالة تختفي والطلب يظهر.

### 🙋 أسئلة وأجوبة الدرس 7.3

**❓ سؤال 9: لو الرسالة مراحتش لـ (RabbitMQ)، هيظهر خطأ للعميل؟**

- مع `SendInline` في العامل اليدوي: `PublishAsync` بيرمي (Exception)، والـ `catch` بتاعنا بيكتبه في عمود `Error`.
- مع الـ (Durable Outbox): **مفيش خطأ للعميل**. الـ (Checkout) يرجع 202، والرسالة تفضل في الجدول، والخطأ في اللوجز بس. العميل هيشوف خطأ بس لو **الداتابيز** فشلت، وساعتها ولا حاجة اتحفظت.

**❓ سؤال 10: ليه جداول (Wolverine) في (Schema) لوحدها؟**

إجابتك: تنظيم ✅. ⚠️ بس إنها تشتغل في نفس الـ (Transaction) مع جداول EF سببه إنهم في **نفس الداتابيز**، مش الـ (Schema).

**❓ سؤال 11: ليه بنحتفظ بالرسالة في الذاكرة مادام الدالة بتحفظها؟**

الذاكرة هنا **قايمة انتظار**، زي `dbContext.Remove(cart)` اللي مبيمسحش غير في `SaveChanges`. من غيرها الدالة مش هتعرف تحفظ إيه. ولو حصل (Exception) قبل الحفظ، القايمة تضيع ومفيش حاجة اتكتبت. وكمان بيبعت منها فورًا من غير ما يرجع يقرا الداتابيز.

**❓ سؤال 12: الترتيب كان بيفرق، هل لسه بيفرق؟**

إجابتك: في الجديد لأ، لأنهم (Transaction) واحدة. ✅

⚠️ لو حصل (Exception) **قبل** الـ (Commit) مفيش "إعادة إرسال"، ولا حاجة اتحفظت. وقبل الموديول 7 الترتيب **كان بيفرق** في نوع المصيبة: تكرار أو ضياع.

## الدرس 7.4: (Idempotent Consumer) و (Inbox)

**الخلاصة:** مش هنمنع التكرار، هنخلي Ordering تستحمله. والحكم الوحيد المضمون هو الـ (Unique Index) في الداتابيز.

### 1️⃣ الفكرة

الـ (Handler) الـ (Idempotent): لو عالج نفس الرسالة 10 مرات، النتيجة زي مرة واحدة.

تشبيه بوابة حفلة: التذكرة عليها (QR Code) = رقم الرسالة. قايمة التذاكر اللي دخلت = (Inbox). وكل كرسي ليه رقم وميقعدش عليه غير واحد = (Unique Constraint).

### 2️⃣ الفخ: "شيّك وبعدين احفظ" (Check-then-save)

```csharp
// ❌ Looks correct, but it is NOT safe
if (await db.Orders.AnyAsync(o => o.CheckoutId == e.Id)) return;
db.Orders.Add(order);
await db.SaveChangesAsync();
```

```
ordering-api #1: AnyAsync → not found → insert order ✅
ordering-api #2: AnyAsync → not found → insert order ✅   => two orders 🔁
```

الـ 3 نسخ ممكن يقروا في نفس اللحظة قبل ما أي واحدة تكتب. الحل: نسيب الداتابيز تحكم بالـ (Unique Constraint).

### 3️⃣ الـ 3 طبقات

| الطبقة | بتعمل إيه | مبتمسكش إيه | في مشروعنا |
| --- | --- | --- | --- |
| (Durable Inbox) في (Wolverine) | يسجّل كل رسالة ويرمي اللي شافها برقم (Wolverine) | تكرار بعد 5 دقايق، والعميل داس مرتين | ✅ حماية زيادة |
| جدول (Processed Messages) | يسجّل الـ (Id) في نفس (Transaction) الطلب | العميل داس مرتين | ❌ مش محتاجينه |
| (Unique Constraint) على `CheckoutId` | الداتابيز ترفض التكرار في أي وقت | العميل داس مرتين | ✅ **شبكة الأمان الأساسية** |

📌 "العميل داس مرتين" مشكلة تانية اسمها (Idempotency Key) على مستوى الـ (API). هنرجعلها بعدين.

### 4️⃣ الكود: `CheckoutId` والـ (Unique Index)

```csharp
// Ordering.Domain/Orders/Order.cs
public Guid? CheckoutId { get; private set; }   // The checkout that created this order

public static Order Create(string buyerId, Address shippingAddress, string currency,
    Guid? checkoutId = null)
{
    // ... same checks ...
    return new Order { Id = OrderId.New(), CheckoutId = checkoutId, /* ... */ };
}
```

- `Guid?` عشان طلبات الـ (Seeder) والطلبات القديمة ملهاش رقم. والبارامتر اختياري عشان الـ (Seeder) يفضل شغال.

```csharp
// Ordering.Infrastructure/Configurations/OrderConfiguration.cs
// 7) One order per checkout (idempotency safety net)
builder.HasIndex(o => o.CheckoutId).IsUnique();
```

- EF Core مع (SQL Server) بيضيف لوحده `filter: "[CheckoutId] IS NOT NULL"`، فالـ (NULL) القديمة مش بتتحسب تكرار.
- الـ (Migration): `AddCheckoutIdToOrders`.

```csharp
// Ordering.API/IntegrationEvents/BasketCheckedOutHandler.cs
var order = Order.Create(e.BuyerId, address, e.Currency, checkoutId: e.Id);
// ... AddItem ...
orderRepository.Add(order);

try
{
    await orderRepository.SaveChangesAsync(ct);
}
catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
{
    // Same checkout already created an order: this is a duplicate message
    logger.LogWarning("Duplicate checkout {CheckoutId} ignored", e.Id);
    return;
}
```

- `2601` و `2627` = أرقام أخطاء (SQL Server) معناها "قيمة متكررة في (Unique Index)".
- **`return` مش `throw`:** لو رمينا، (Wolverine) هيعمل (Retry) ويرميها في الـ (DLQ)، وهي مش بايظة، هي اتعالجت قبل كده.

### 5️⃣ الـ (Durable Inbox) في Ordering

```csharp
opts.PersistMessagesWithSqlServer(connectionString, "wolverine");

opts.ListenToRabbitQueue("ordering.basket-checked-out")
    .UseDurableInbox();

// outside UseWolverine:
builder.Services.AddResourceSetupOnStartup();
```

```
Before: RabbitMQ → Handler → save order → Ack
After:  RabbitMQ → save in wolverine_incoming_envelopes → Ack
                        ↓
                   Handler → save order → mark envelope "Handled"
```

- الـ (Ack) بيتبعت بعد ما الرسالة تتحفظ في داتابيزنا، فلو Ordering وقعت، الرسالة مش ضايعة.
- (Wolverine) بيسيب الرسالة المتعالجة **5 دقايق** بس عشان يكتشف التكرار.
- **ليه الـ (Unique Index) لسه ضروري؟** حفظ الطلب وتعليم الرسالة "اتعالجت" مش في نفس الـ (Transaction). لو وقعنا بينهم، الرسالة تتعالج تاني، والـ (Unique Index) يمسكها. ((Transactional Middleware) في (Wolverine) بتحل ده بس مؤجلة.)
- مش محتاج تشيل `EnableRetryOnFailure` هنا، لأن (Wolverine) مش بيفتح (Transaction) مع الـ (DbContext) في Ordering.

### 6️⃣ رقمين ماشيين مع الرسالة

|  | رقم (Wolverine) | الـ (Id) بتاعنا |
| --- | --- | --- |
| مكانه | عمود `id` في جداول (Wolverine) + الـ (Headers) | جوه الـ `body` (الـ JSON) |
| مين بيعمله | (Wolverine) وقت `PublishAsync` | Basket وقت إنشاء الحدث |
| مين بيستخدمه | الـ (Inbox) | الـ (Unique Index) على `CheckoutId` |

```
Basket outbox (id = W1)  →  RabbitMQ headers (id = W1)  →  Ordering inbox (id = W1)
```

تشبيه: رقم **تتبع الشحنة** على الظرف من برّه، ورقم **الفاتورة** جوه الجواب.

### 7️⃣ تجربة (ServiceLocationPolicy)

غيّرناها لـ `NotAllowed`، والرسالة راحت الـ (DLQ) وفي (Headers) بتاعتها `InvalidServiceLocationException` بسببين:

| السبب | الحل |
| --- | --- |
| `DbContextOptions<OrderingDbContext>` متسجلة بـ (Lambda) مقفولة (Opaque) و (Scoped) | `optionsLifetime: ServiceLifetime.Singleton` ✅ |
| `DomainEventDispatcher` بياخد `IServiceProvider` | (Service Location) **مقصود** بالتصميم |

القرار: نرجّعها `AllowedButWarn`، ومبقتش "حل مؤقت" بقت قرار مكتوب في ADR-004.

```csharp
builder.Services.AddDbContext<OrderingDbContext>(
    options => options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
        maxRetryCount: 2, maxRetryDelay: TimeSpan.FromSeconds(2), errorNumbersToAdd: null)),
    optionsLifetime: ServiceLifetime.Singleton);
```

### 🧪 تجارب

- **نفس تجربة 7.1 (throw بعد الحفظ):** طلب واحد بس بدل 4، وفي اللوجز `Duplicate checkout ... ignored`، ومفيش حاجة في الـ (DLQ). ✅ اتعملت.
- **(Replay) للرسالة القديمة:** بعد ما مسحت الطلبات القديمة، اتعمل طلب واحد. ✅

```sql
-- Must return nothing
SELECT CheckoutId, COUNT(*) AS Cnt
FROM Orders
WHERE CheckoutId IS NOT NULL
GROUP BY CheckoutId
HAVING COUNT(*) > 1;
```

### 🙋 أسئلة وأجوبة الدرس 7.4

**❓ سؤال 13: ليه (Unique Constraint) بتنجح و `AnyAsync` بتفشل؟**

إجابتك: `AnyAsync` التلات نسخ ممكن يشيّكوا مع بعض ويضيفوا مع بعض، والداتابيز بتستقبل واحد بس. ✅

⚠️ الداتابيز مش بتنفذ الطلبات "واحد واحد". الـ (Unique Index) بيقفل على **القيمة نفسها** لحظة الإدخال، فالتشييك والإضافة خطوة واحدة (Atomic).

**❓ سؤال 14: ليه (Wolverine) عنده (Id) خاص بيه؟ الـ (Id) بتاعنا يكفي.**

(Wolverine) مكتبة عامة وميعرفش شكل رسايلك، وفيه رسايل ملهاش (Id) أصلًا. ✅ وكلامك صح: الـ (Id) بتاعنا يكفي، وعشان كده الطبقة الأساسية معتمدة عليه.

**❓ سؤال 15: لو عملت (Replay) للرسالة القديمة من الـ (DLQ)؟**

إجابتك: اتعملت مرة واحدة، لأنك مسحت الطلبات القديمة. ✅ 💡 في الشغل مش هتقدر تمسح، والطلبات القديمة `CheckoutId` بتاعها (NULL)، فالـ (Replay) هيعمل طلب زيادة. لازم تملا العمود للبيانات القديمة الأول (Backfill).

**❓ سؤال 16: لو هتختار طبقة واحدة؟**

إجابتك: الـ (Unique Index)، لأن الـ (Inbox) بيمسح بعد 5 دقايق والداتابيز مستحيل. ✅ 💡 والـ (Inbox) ليه فايدة تانية: بيحفظ الرسالة قبل الـ (Ack) فمبتضيعش.

**🎤 سؤال إنترفيو: الفرق بين (Inbox Pattern) و (Idempotent Consumer)؟**

إجابتك فصلتهم كأنهم حاجتين منفصلين. ⚠️ واحد **هدف** والتاني **طريقة**، والـ (Inbox) بيمنع التكرار من أي مصدر مش من الـ (Outbox) بس.

> "الـ (Idempotent Consumer) هو الهدف، والـ (Inbox) طريقة لتحقيقه. الـ (Inbox) يكفي لوحده بشرطين: الرقم يتسجل في نفس الـ (Transaction) بتاعة التغيير، ويفضل محفوظ طول فترة التكرار. ولو مش مضمونين، أضيف (Unique Constraint) على مفتاح من البيزنس."

## الدرس 7.5: الأخطاء والـ (DLQ) والترتيب و ADR-004

**الخلاصة:** نوع الخطأ بيحدد نعمل (Retry) ولا نبعت الـ (DLQ) على طول. والـ (Replay) آمن بس مع (Consumer) يكون (Idempotent). ومتعتمدش على ترتيب الرسايل.

### 1️⃣ أنواع الأخطاء

| النوع | مثال | (Retry) هيفيد؟ | الصح |
| --- | --- | --- | --- |
| مؤقت (Transient) | الداتابيز واقعة، (Timeout)، الشبكة | ✅ | جرّب تاني بعد شوية |
| دائم (Permanent) | `DomainException` (كمية بالسالب) | ❌ | الـ (DLQ) على طول |
| مش خطأ | رسالة مكررة (`2601` / `2627`) | ❌ | تجاهلها و (Ack) |

🔗 **من Laravel:** زي `$tries` و `$backoff`، و `$this->fail()` لما الخطأ مالوش حل.

### 2️⃣ أنواع الـ (Retry) في (Wolverine)

| الطريقة | بتعمل إيه | تنفع إمتى |
| --- | --- | --- |
| `RetryTimes` | تعيد على طول | خطأ لحظي جدًا |
| `RetryWithCooldown` | تستنى وقت قصير وتعيد | انتظار بالثواني |
| `ScheduleRetry` | تأجّل الرسالة لوقت بعدين | انتظار بالدقايق |
| `Requeue` | ترجّعها لآخر الطابور | تدّي فرصة لرسايل تانية |
| `Discard` | (Log) وترميها | رسايل ملهاش لازمة |
| `MoveToErrorQueue` | الـ (DLQ). وده الافتراضي لما المحاولات تخلص | الأخطاء الدائمة |

⚠️ القواعد بتتقيّم **بالترتيب**، وأول واحدة تنطبق هي اللي بتتنفذ. الخاصة قبل العامة.

### 3️⃣ سياسة الأخطاء الجديدة في Ordering

```csharp
// 1) Permanent: a business rule is broken → DLQ immediately
opts.OnException<DomainException>().MoveToErrorQueue();

// 2) Anything else: short retries, then longer scheduled retries, then DLQ
opts.OnException<Exception>()
    .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
    .Then.ScheduleRetry(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5))
    .Then.MoveToErrorQueue();
```

1. **أول 16 ثانية:** 3 محاولات سريعة للأخطاء اللحظية.
2. **بعدها:** دقيقة، وبعدين 5 دقايق، لـ "الداتابيز واقعة شوية".
3. **لو لسه فاشلة:** الـ (DLQ)، وحد يبص عليها.

**الـ (Trade-off):** لو الخطأ (Bug)، الرسالة هتضيّع حوالي 6 دقايق قبل الـ (DLQ). مقبول عشان منضيّعش رسايل بسبب وقعة مؤقتة.

### 4️⃣ الـ (Retry) بيتضرب في بعضه (Retry Amplification)

في Ordering طبقتين: `EnableRetryOnFailure` في EF (بيعيد الأمر بس) و (Wolverine) (بيعيد الـ Handler كله). الافتراضي في EF لحد 6 محاولات وانتظار لحد 30 ثانية، فكل محاولة من (Wolverine) ممكن تاخد دقايق.

القرار: نسيب EF صغير وسريع للأخطاء اللحظية، والانتظار الطويل لـ (Wolverine):

```csharp
sql.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromSeconds(2), errorNumbersToAdd: null)
```

💡 **(Jitter):** الـ 3 نسخ هيعملوا (Retry) في نفس اللحظة ويضربوا الداتابيز مع بعض. الـ (Jitter) بيضيف وقت عشوائي صغير يوزّعهم. الإصدارات الحديثة من (Wolverine) فيها `WithFullJitter()` (اتأكد من إصدارك).

### 5️⃣ الـ (DLQ) والـ (Replay)

الـ (DLQ) **غرفة طوارئ** مش سلة زبالة. والرسالة اللي هتفشل مهما تجرب اسمها (Poison Message).

| الـ (Header) | بيقولك إيه |
| --- | --- |
| `exception-type` | نوع الخطأ |
| `exception-message` | رسالة الخطأ |
| `exception-stack` | السطر اللي وقع فيه |
| `original-destination` | الطابور اللي ترجّعها عليه |
| `failed-at` | وقت الفشل |

| السبب | التصرف |
| --- | --- |
| (Bug) في الكود | صلّح ← (Deploy) ← (Replay) |
| مؤقت وخلّص المحاولات | اتأكد إن المشكلة اتحلت ← (Replay) |
| بيانات غلط (`Quantity = -2`) | **متعملهاش (Replay)**. صلّح المصدر، وسجّلها وامسحها |

> 📌 **القاعدة الذهبية:** متعملش (Replay) أبدًا لـ (Consumer) مش (Idempotent).

- الـ (DLQ) مشترك (`wolverine-dead-letter-queue`)، و `original-destination` هو اللي بيقولك ترجّع كل رسالة فين.
- محدش هيفتح اللوحة كل يوم: محتاجين (Alert) في الموديول 13.

### 6️⃣ ترتيب الرسايل (Message Ordering)

| السبب | إزاي بيلخبط الترتيب |
| --- | --- |
| 3 نسخ من Ordering | نسخة بطيئة ونسخة سريعة |
| الـ (Retry) | رسالة اتأجلت واللي بعدها اتعالجت |
| الـ (Prefetch) | الرسالة المرتجعة بتوصل بعد اللي اتسحبت مسبقًا |
| الـ (DLQ) والـ (Replay) | رسالة رجعت بعد ساعة |

في (BasketCheckedOut) الترتيب **مش فارق**: كل (Checkout) مستقل. بس في الموديول 8 هيفرق:

```
Catalog sends:   PriceChanged(100 → 120)   then   PriceChanged(120 → 110)
Basket receives: 110 first ... then 120  ❌   final price = 120 (real price is 110)
```

**الحلول من الأحسن للأصعب:**

1. **صمّم بحيث الترتيب ميفرقش** (زي BasketCheckedOut).
2. **رقم (Version) جوه الرسالة** (الموديول 8): الـ (Consumer) يتجاهل الأقدم.
3. **(Consumer) واحد للطابور** ((Single Active Consumer)): بيحافظ على الترتيب بس بتخسر الـ (Scaling).

```csharp
// Preview for Module 8
if (evt.Version <= product.PriceVersion)
    return; // older event arrived late → ignore it

product.Price = evt.NewPrice;
product.PriceVersion = evt.Version;
```

### 7️⃣ ADR-004 (`docs/adr/ADR-004-reliable-messaging.md`)

```markdown
# ADR-004: الرسائل الموثوقة (Outbox و Inbox و Idempotent Consumer)

## الحالة
مقبول · 2026-11-20

## القرار (مختصر)
1. Outbox في Basket بـ Wolverine (IDbContextOutbox)، وشيلنا EnableRetryOnFailure من Basket.
2. Durable Inbox في Ordering.
3. Orders.CheckoutId + Unique Index، والتكرار (2601/2627) ← Log + return.
4. DomainException ← DLQ، والباقي ← RetryWithCooldown ثم ScheduleRetry ثم DLQ.
5. ServiceLocationPolicy.AllowedButWarn بقرار (بسبب DomainEventDispatcher).
6. مش هنعتمد على ترتيب الرسائل، والأحداث اللي بتغيّر حالة فيها Version.

## البدائل اللي اترفضت
Outbox بإيدنا، و CDC، و "شيّك وبعدين احفظ"، و Inbox لوحده.
```

(النسخة الكاملة في ملف `ADR-004-reliable-messaging.md` اللي اتبعت في الشات.)

### 🙋 أسئلة وأجوبة الدرس 7.5

**❓ سؤال 17: رسالة فيها `Quantity = -2`. كام محاولة؟ وتروح فين؟**

إجابتك: 3 محاولات وبعدين دقيقة و 5 دقايق وبعدين الـ (DLQ). ❌

الصح: **ولا محاولة**، الـ (DLQ) على طول. `DomainException` اتمسكت في القاعدة الأولى، والتانية ماتتقراش. و `-2` هتفضل `-2` لو جربت 100 مرة.

✅ السيناريو اللي قلته (كمية موجبة والداتابيز واقعة) صح، بس ده خطأ تاني (`SqlException`) وبيقع في القاعدة التانية. **نوع الخطأ** هو اللي بيحدد.

**❓ سؤال 18: أشيل `EnableRetryOnFailure` عشان الـ (Retry) مش يتضرب؟**

لأ، **قلّله**. EF بيعيد الأمر بس (أرخص)، و (Wolverine) للوقعات الأطول. خليه محاولتين بثانيتين بالكتير.

**❓ سؤال 19: (OrderCancelled) وصلت لطلب لسه موجودش. أعمل إيه؟**

إجابتك: (ج) ارمي (Exception) عادي، و (Wolverine) يعمل (Retry). ✅ الرسالة مش غلط، هي وصلت بدري. (أ) غلط لأنها مش قاعدة بيزنس اتكسرت، و(ب) غلط لأن الإلغاء هيضيع.

⚠️ الـ (Version) مش هو الحل هنا، لأن الطلب مش موجود أصلًا. 💡 بس فكرتك عن الأرقام المتسلسلة ليها اسم: (Gap Detection). لو عندك (Version 3) وجاتلك (5)، تعرف إن (4) ناقصة فتعمل (Retry) بدل ما تتجاهل.

## 🔄 رحلة الـ (Checkout) بعد الموديول 7

```
POST /api/basket/checkout                                        [Basket]
  └─ One transaction (EF Core): delete basket + insert outbox row
       └─ After commit: send now (IDbContextOutbox)
            ├─ RabbitMQ down? → row waits, Durability Agent sends later
            ▼
basket-checked-out → ordering.basket-checked-out                 [RabbitMQ]
            ▼
Durable inbox: save envelope, then Ack                           [Ordering]
  ├─ Same Wolverine id? → discarded (kept 5 minutes)
  ▼
BasketCheckedOutHandler: Order.Create(..., checkoutId: e.Id)
  ├─ DomainException → DLQ now
  ├─ other exception → retry 1s, 5s, 10s → 1m, 5m → DLQ
  ▼
CheckoutId unique?
  ├─ new id → Order saved (one order per checkout)
  └─ duplicate (2601 / 2627) → log + return → Ack, no DLQ
```

العمود الشمال هو الطريق الطبيعي، والصناديق اليمين هي اللي بتحميه لما حاجة تبوظ. أي تكرار بيعدّي من الـ (Inbox) بيقف عند الـ (Unique Index).

## 🔧 المشاكل اللي قابلتها وحلولها

أغلب مشاكل الموديول ده اتكشفت بتجارب مقصودة (نوقّع حاجة بإيدنا ونشوف النتيجة)، ومن الـ (Headers) بتاعة الرسالة في الـ (DLQ).

| # | العرض | السبب | الحل |
| --- | --- | --- | --- |
| 1 | رسالة واحدة عملت **4 طلبات** | (Exception) بعد `SaveChangesAsync`، وكل (Retry) بيعمل طلب جديد | `CheckoutId` + (Unique Index) + `catch` لـ 2601/2627 و `return` |
| 2 | (Replay) لرسالة قديمة ممكن يعمل طلب زيادة | الطلبات القديمة اتحفظت قبل العمود، فـ `CheckoutId` بتاعها (NULL) | مسحت الطلبات القديمة. في الشغل: (Backfill) قبل أي (Replay) |
| 3 | `InvalidServiceLocationException` والرسالة في الـ (DLQ) | `NotAllowed` + (DbContextOptions) بـ (Lambda) + `DomainEventDispatcher` بياخد `IServiceProvider` | `optionsLifetime: Singleton` + رجوع لـ `AllowedButWarn` + (Replay) |
| 4 | (وقائي) `does not support user-initiated transactions` في Basket | `EnableRetryOnFailure` مع الـ (Transaction) اللي (Wolverine) بيفتحها | شلنا `EnableRetryOnFailure` من Basket |
| 5 | (وقائي) العامل اليدوي ممكن يضيّع رسالة متعلّم عليها "اتبعتت" | وضع (Buffered): `PublishAsync` بيرجع قبل ما (RabbitMQ) يستلم | `.SendInline()` (وفي 7.3 وضع (Durable)) |
| 6 | (وقائي) نسختين من Basket بيبعتوا نفس الصف | كل نسخة فيها عامل بيقرا نفس الجدول | (Wolverine) بدل النسخة اليدوية |
| 7 | (وقائي) محاولات كتير جدًا على داتابيز واقعة | (Retry Amplification): EF × (Wolverine) | EF: محاولتين بثانيتين بالكتير |

**دليل مشكلة 3:** الخطأ ظهر **وقت معالجة الرسالة** مش وقت تشغيل الخدمة، لأن (Wolverine) بيبني كود الـ (Handler) أول ما أول رسالة توصل. والـ `exception-message` قالك بالظبط أنهي كلاس هو السبب.

**دليل الوقت (من الموديول 6 ولسه شغال):** رسالة راحت الـ (DLQ) في ثانية من غير محاولات = خطأ **دائم** (`DomainException`، أو (Deserialization)، أو الـ (Handler) مش عارف يتبني). ومع السياسة الجديدة، الخطأ المؤقت هياخد حوالي 6 دقايق قبل الـ (DLQ).

## 🧠 الملخص السريع (Cheat Sheet)

| المفهوم | في جملة | في المشروع |
| --- | --- | --- |
| (Dual-write) | كتابة في نظامين من غير (Transaction) تجمعهم | الـ (Checkout) القديم |
| (At-least-once) | الرسالة مش هتضيع، بس ممكن تتكرر | (RabbitMQ) و (Wolverine) والـ (Outbox) |
| (Exactly-once) | مستحيل في التوصيل، ممكن في النتيجة | (At-least-once) + (Idempotent Consumer) |
| (Transactional Outbox) | الرسالة تتحفظ في نفس الداتابيز ونفس الـ (Transaction) | `IDbContextOutbox` في Basket |
| (Message Relay) | اللي بيبعت الرسايل من الجدول | `OutboxProcessor` أو (Durability Agent) |
| (Polling) مقابل (CDC) | تسأل الجدول مقابل تقرا سجل العمليات | (Wolverine) بيستخدم (Polling) |
| (Buffered) مقابل (Inline) مقابل (Durable) | ذاكرة / استنى (RabbitMQ) / احفظ في الداتابيز | Basket: (Durable) |
| (Idempotent Consumer) | نفس الرسالة 10 مرات = نفس النتيجة | `BasketCheckedOutHandler` |
| (Inbox) | جدول بيسجل الرسايل اللي وصلت | `UseDurableInbox()` في Ordering |
| (Unique Constraint) | الداتابيز ترفض التكرار في خطوة واحدة (Atomic) | `Orders.CheckoutId` |
| (Check-then-save) | شيّك وبعدين احفظ: فيها (Race Condition) | ممنوع `AnyAsync` للمنع |
| (Poison Message) | رسالة هتفشل مهما جربت | `DomainException` ← (DLQ) |
| (Replay) | رجّع رسالة من الـ (DLQ) للطابور | (Move messages) في لوحة (RabbitMQ) |
| (Backfill) | املا عمود جديد للبيانات القديمة | قبل (Replay) رسايل قديمة |
| (Retry Amplification) | طبقات (Retry) بتتضرب في بعض | EF × (Wolverine) |
| (Jitter) | وقت عشوائي يوزّع الـ (Retry) | 3 نسخ من Ordering |
| (Version) / (Gap Detection) | تجاهل الأقدم / استنى الناقص | الموديول 8 |

### 🔑 أهم 10 قواعد

1. **عمرك ما تعمل "احفظ وابعت" من غير (Outbox).**
2. **جدول الـ (Outbox) في نفس داتابيز الخدمة**، وكل خدمة ليها جدولها.
3. **التكرار مش هيختفي**، فكل (Consumer) لازم يكون (Idempotent).
4. **الداتابيز هي الحكم:** (Unique Constraint)، مش `AnyAsync`.
5. **التكرار مش خطأ:** (Log) و `return`، مش `throw`.
6. **استخدم `outbox.PublishAsync` و `SaveChangesAndFlushMessagesAsync`** مش `bus` و `SaveChangesAsync`.
7. **نوع الخطأ بيحدد التصرف:** دائم ← (DLQ) فورًا، مؤقت ← (Retry) قصير ثم طويل. والقاعدة الخاصة قبل العامة.
8. **فكّر في مجموع الـ (Retry)** مش كل طبقة لوحدها.
9. **متعملش (Replay) لـ (Consumer) مش (Idempotent)**، وللبيانات القديمة اعمل (Backfill) الأول.
10. **متعتمدش على ترتيب الرسايل**، ولو مضطر استخدم (Version).

## 📝 اختبر نفسك

جاوب من دماغك الأول، وبعدين بص على الإجابة المختصرة.

1. **في الكود القديم، لو قلبنا الترتيب (احفظ وبعدين ابعت)، اتحلت المشكلة؟**
   - لأ. اتحوّلت من "طلب مكرر" لـ "طلب ضايع".
2. **ليه مينفعش نحط الحفظ والنشر في (Transaction) واحدة؟**
   - لأن (Transaction) الداتابيز مبتغطيش (RabbitMQ).
3. **الـ (Outbox) بيضمن (Exactly-once)؟**
   - لأ، (At-least-once). لو العامل بعت ووقع قبل ما يعلّم، هيبعت تاني.
4. **ليه العامل اليدوي محتاج `CreateScope()`؟**
   - لأنه (Singleton) والـ `DbContext` (Scoped).
5. **إيه اللي بيحصل لو كتبت `dbContext.SaveChangesAsync` بدل `SaveChangesAndFlushMessagesAsync`؟**
   - السلة تتمسح والرسالة لا تتحفظ ولا تتبعت. طلب ضايع.
6. **ليه شلنا `EnableRetryOnFailure` من Basket بس؟**
   - لأن (Wolverine) بيفتح (Transaction) مع الـ (DbContext) هناك. في Ordering لأ، فسبناه وقللناه.
7. **ليه `AnyAsync` قبل الحفظ مش آمن؟**
   - نسختين يقروا في نفس اللحظة ويلاقوا "مش موجود"، فالاتنين يضيفوا.
8. **ليه `return` مش `throw` لما نلاقي تكرار؟**
   - عشان (Wolverine) يبعت (Ack). الـ `throw` هيعمل (Retry) ويرميها في الـ (DLQ) وهي مش بايظة.
9. **الـ (Unique Index) على عمود (Nullable)، الـ (NULL) القديمة هتبقى تكرار؟**
   - لأ، EF بيضيف `filter: "[CheckoutId] IS NOT NULL"`.
10. **الـ (Inbox) بتاع (Wolverine) بيعتمد على أنهي رقم؟ وبيفضل قد إيه؟**
    - رقم (Wolverine) اللي في الـ (Headers)، وبيفضل 5 دقايق افتراضيًا.
11. **(DomainException) بتعمل كام (Retry)؟**
    - ولا واحدة، الـ (DLQ) على طول، لأن قاعدتها أول واحدة.
12. **زميلك عايز يعمل (Replay) لرسالة فيها "Quantity must be greater than zero". هتقوله إيه؟**
    - متعملهاش، هتفشل تاني. دي بيانات غلط: صلّح المصدر (Basket)، وسجّل الرسالة وامسحها.
13. **ليه ترتيب الرسايل مش مضمون مع إن الطابور (FIFO)؟**
    - 3 نسخ بسرعات مختلفة، والـ (Retry)، والـ (Prefetch)، والـ (Replay).

**🎤 أسئلة إنترفيو:**

- *"اشرح (Transactional Outbox): بيحل إيه، وإيه عيوبه؟"*
  - بيحل الـ (Dual-write) بإن الرسالة تتحفظ مع البيانات في (Transaction) واحدة، و (Relay) يبعتها. عيوبه: تأخير صغير، وجدول محتاج تنضيف، والرسالة ممكن تتكرر فالـ (Consumer) لازم يكون (Idempotent).
- *"إزاي تطبّق (Outbox) في (.NET) من غير ما تكتبه بإيدك؟"*
  - بمكتبة زي (Wolverine) مع EF Core. الشرط: جداول الـ (Outbox) في نفس داتابيز الخدمة، والنشر والحفظ في نفس الـ (Transaction).
- *"ليه أغلب أنظمة الرسائل (At-least-once)؟"* (الإجابة في الدرس 7.1)
- *"الفرق بين (Inbox) و (Idempotent Consumer)؟"* (الإجابة في الدرس 7.4)

## 📁 الملفات اللي اتغيرت في الريبو

| الملف | التغيير | الدرس |
| --- | --- | --- |
| `Directory.Packages.props` | `WolverineFx.EntityFrameworkCore` و `WolverineFx.SqlServer` (6.61.0) | 7.3 |
| `BuildingBlocks.Messaging.csproj` | الـ (PackageReference) للاتنين | 7.3 |
| `Basket.API/Program.cs` | `optionsLifetime: Singleton`، من غير `EnableRetryOnFailure`، وإعدادات الـ (Outbox) | 7.3 |
| `Basket.API/Controllers/BasketController.cs` | `IDbContextOutbox` في الـ (Checkout)، وشيل `IMessageBus` | 7.3 |
| `Ordering.Domain/Orders/Order.cs` | `CheckoutId` + بارامتر اختياري في `Create` | 7.4 |
| `Ordering.Infrastructure/Configurations/OrderConfiguration.cs` | `HasIndex(o => o.CheckoutId).IsUnique()` + (Migration) `AddCheckoutIdToOrders` | 7.4 |
| `Ordering.API/IntegrationEvents/BasketCheckedOutHandler.cs` | `checkoutId: e.Id` + `catch` للتكرار | 7.4 |
| `Ordering.API/Program.cs` | (Durable Inbox)، و `optionsLifetime: Singleton`، و EF (Retry) أصغر، وسياسة الأخطاء الجديدة | 7.4 و 7.5 |
| `docs/adr/ADR-004-reliable-messaging.md` | ملف جديد | 7.5 |
| فرع `experiment/manual-outbox` | `OutboxMessage` و `OutboxProcessor` و (Migration) `AddOutboxMessages` (مرجع بس) | 7.2 |

## ✅ قائمة مهام قفل الموديول

- [x] تجربة "رسالة واحدة = 4 طلبات" (7.1).
- [x] الـ (Outbox) اليدوي في فرع منفصل (7.2).
- [x] نفس التجربة بعد الـ (Unique Index) = طلب واحد، و (Replay) للرسالة القديمة (7.4).
- [x] تجربة `ServiceLocationPolicy.NotAllowed` (7.4).
- [ ] تجربة (RabbitMQ) واقع مع الـ (Outbox) بتاع (Wolverine) (7.3).
- [ ] تطبيق سياسة الأخطاء الجديدة و EF (Retry) الأصغر، ودورة `DomainException` ← (DLQ) ← (Replay) (7.5).
- [ ] `docs/adr/ADR-004-reliable-messaging.md` في الريبو.
- [ ] انسخ الملف ده لـ `docs/notes/07-reliable-messaging.md`.
- [ ] حدّث `docs/PROJECT_CONTEXT.md` و `docs/LEARNING_PROGRESS.md` بالنسخ الجديدة.
- [ ] (Commit) و (PR) لفرع `module_7` ودمجه في (main).

## ⚠️ الثغرات اللي لسه مفتوحة

| الثغرة | السيناريو | الحل |
| --- | --- | --- |
| العميل داس (Checkout) مرتين | حدثين بـ (Id) مختلف، فالـ (Unique Index) مش هيمسكهم | (Idempotency Key) على مستوى الـ (API) |
| السعر وقت الـ (Checkout) | التحقق بيحصل وقت الإضافة للسلة بس | الموديول 8 |
| محدش بيبص على الـ (DLQ) | رسايل فاشلة ممكن تقعد أيام | (Alert) في الموديول 13 |
| حفظ الطلب وتعليم الـ (Inbox) مش في نفس الـ (Transaction) | الـ (Unique Index) مغطيها دلوقتي | (Transactional Middleware) بعدين لو احتجنا |

## 📚 المراجع

- [microservices.io: Transactional Outbox](https://microservices.io/patterns/data/transactional-outbox.html) و [Idempotent Consumer](https://microservices.io/patterns/communication-style/idempotent-consumer.html)
- [Wolverine: EF Core Integration](https://wolverine.netlify.app/guide/durability/efcore/) و [Outbox and Inbox with EF Core](https://wolverine.netlify.app/guide/durability/efcore/outbox-and-inbox)
- [Wolverine: Idempotency](https://wolverine.netlify.app/guide/durability/idempotency) و [Error Handling](https://wolverine.netlify.app/guide/handlers/error-handling)
- [Microsoft: EF Core Connection Resiliency](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency)
- Martin Kleppmann, *Designing Data-Intensive Applications* (الفصل 11: Stream Processing)
