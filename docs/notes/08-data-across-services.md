# الموديول 8: البيانات عبر الخدمات والكاش

Nov 22, 2026 · @Ahmed Nagi

كورس الـ (Microservices) على مشروع ECommerceMicroservices · المرحلة التالتة: الاتصال غير المتزامن والبيانات الموزعة

## مقدمة: الموديول ده حل مشكلة إيه؟

بعد الموديول ده، **مفيش طلب بيتعمل بسعر قديم**، والسلة بتحدّث أسعارها لوحدها من غير ما تسأل (Catalog) كل مرة. قبله، (Basket) كان بياخد صورة من السعر وقت الإضافة، ولو السعر اتغير بعدها الطلب بيتعمل بالسعر القديم.

السؤال اللي الموديول كله بيجاوبه: **إزاي خدمة تعرف بيانات خدمة تانية، وتفضل البيانات دي صحيحة بشكل كافي، من غير ما نكسر استقلال الخدمات؟**

### خريطة الموديول

| الدرس | الموضوع | النتيجة في المشروع |
| --- | --- | --- |
| 8.1 | مشكلة البيانات القديمة (Stale Data) والحلول التلاتة | فهمنا المشكلة واخترنا الحل |
| 8.2 | حدث `ProductPriceChanged` من (Catalog) | (Catalog) بيبلّغ بـ (Outbox) و `PriceVersion` و (Optimistic Concurrency) |
| 8.3 | نسخة السعر في (Basket) | (Basket) بيحدّث نفسه بـ `ExecuteUpdateAsync` مشروط |
| 8.4 | التحقق النهائي وقت الـ (Checkout) | (Batch Endpoint) و 409 و 503. **الثغرة اتقفلت** |
| 8.5 | (CQRS) والـ (API Composition) و (Read Model) | `GET /api/orders` في (Ordering)، والـ (Composition) اتأجل للموديول 9 |
| 8.6 | الكاش بـ (Redis) | (HybridCache) لتفاصيل المنتج، مع المسح وقت تغيير السعر |

### الموديول في جملتين

الأحداث بتخلي السلة **غالبًا صح**، والتحقق الأخير بيخلي الطلب **دايمًا صح**. وكل قرار (Sync ولا Async، كاش ولا لأ، 503 ولا نكمل) بيتاخد حسب **تمن الغلط** في المكان ده.

### إزاي تذاكر الملف ده

- كل درس فيه: **المشكلة ← الفكرة ← الكود ← المقايضات ← إمتى ما تستخدمهاش**.
- بعد كل درس قسم **أسئلة وأجوبة**: الأسئلة اللي إنت سألتها فعلًا وإحنا بنذاكر، وإجاباتك وتصحيحها.
- في الآخر: **المشاكل اللي قابلتها**، و**الملخص السريع**، و**اختبر نفسك**، و**ADR-005**.
- الكود مكتوب بنفس أسامي مشروعك: `ProductsController` و `BasketController` و `ShoppingCartItem` و `OrdersController`.

## الدرس 8.1: مشكلة البيانات القديمة والحلول التلاتة

**الخلاصة:** السعر اللي في السلة صورة (Snapshot) اتاخدت وقت الإضافة. (Catalog) هو صاحب السعر الحقيقي (Source of Truth)، ومحدش بيقول (Basket) إن نسخته بقت قديمة.

### 1. المشكلة في مشروعنا

```
10:00  العميل ضاف لابتوب للسلة ← Basket حفظ السعر 500
11:00  الأدمن غيّر السعر في Catalog ← بقى 700
12:00  العميل داس Checkout ← Basket بعت UnitPrice = 500 ← طلب بـ 500 (خسارة 200)
```

في (Laravel) المونوليث كنت هتعمل `Product::find($id)->price` وقت الـ (Checkout). هنا (Basket) ممنوع يقرا من `catalog-db` (Database per Service).

### 2. إيه الداتا اللي بتتغير؟

| الداتا | ناخد منها نسخة؟ | ليه |
| --- | --- | --- |
| السعر | أيوه | بيتغير أحيانًا، والنسخة بتتحدّث بالأحداث |
| الحالة (متاح ولا اتشال) | أيوه | نفس فكرة السعر |
| المخزون | لأ | بيتغير مع كل طلب، ومحتاج **حجز** مش نسخة (الموديول 11) |

### 3. الحلول التلاتة

| الطريقة | الفكرة | مثالها عندنا |
| --- | --- | --- |
| **اسأل** (Sync) | (Basket) يسأل (Catalog) وقت ما يحتاج | التحقق وقت الـ (Checkout) (8.4) |
| **اتقالّي** (Events) | (Catalog) يبلّغ، و (Basket) يحدّث نسخته | `ProductPriceChanged` (8.2 و 8.3) |
| **اجمع** (Composition) | حد تالت يجمع الداتا وقت العرض | صفحة "طلباتي" (8.5) |

**قرارنا: نجمع الاتنين الأولانيين.** الأحداث لتجربة المستخدم (السلة غالبًا صح)، والسؤال المباشر وقت الـ (Checkout) للصحة (الطلب دايمًا صح).

### إمتى ما تستخدمش نسخة محلية؟

لو الداتا لازم تبقى صح في نفس اللحظة (مخزون قبل الحجز، رصيد)، أو لو محدش بيقراها كتير فالسؤال المباشر كفاية.

### أسئلة وأجوبة الدرس 8.1

**سؤال 1: لو إنت اللي هتصلح المشكلة، إيه الحلول؟**

إجابتك كانت 4 أفكار:

1. "نشوف إيه الداتا المتغيرة: السعر والحالة والمخزون". صح في السعر والحالة. المخزون مختلف: محتاج حجز مش نسخة.
2. "(Integration Event) بـ (Wolverine) و (BackgroundService) يحدّث السلال". الحدث صح وده الحل الأساسي. بس مش محتاجين (BackgroundService): الـ (Handler) أصلًا شغال في الخلفية زي الـ (Listener) في (Laravel).
3. "وقت الـ (Checkout) أتأكد إن الـ (Inbox) كله اتعالج". مش هتنفع: الـ (Inbox) بيشوف اللي وصله بس. الحدث ممكن يكون لسه في (Outbox) الـ (Catalog) أو في (RabbitMQ). الحل نسأل (Catalog) مباشرة.
4. "(Redis) مشترك بين الخدمات". إحساسك صح إنها مش مظبوطة، بس السبب الأهم مش المساحة. دي **داتابيز مشتركة متنكرة**: مين صاحب الداتا؟ ولو (Catalog) غيّر شكل المفتاح (Basket) يتكسر.

**سؤال 2: ليه منسألش (Catalog) بس كل مرة العميل يفتح السلة؟ وليه لازم نسأله وقت الـ (Checkout)؟**

إجابتك: وقت الـ (Checkout) لأنه صاحب الحقيقة، والحدث ممكن يكون لسه في الـ (Outbox). صح.

وفي الجزء الأول النتيجة صح بس السبب اتعدل: السؤال الدايم كان هيعرض السعر الجديد برضه. المشكلة الحقيقية:

- **الاعتمادية:** لو (Catalog) واقع، صفحة السلة كلها تقع.
- **الحمل:** 10 منتجات × آلاف العملاء = طلبات كتير على الفاضي.

## الدرس 8.2: حدث `ProductPriceChanged` من (Catalog)

**الخلاصة:** (Catalog) بيبعت حدث فيه السعر الجديد نفسه ورقم نسخة بيزيد. الحدث بيعدي على (Outbox) عشان ميضيعش، والنسخة محمية بـ (Optimistic Concurrency) عشان متتكررش بسعرين.

### 1. العقد

```csharp
// BuildingBlocks.Contracts/Catalog/ProductPriceChangedIntegrationEvent.cs
namespace BuildingBlocks.Contracts.Catalog;

public sealed record ProductPriceChangedIntegrationEvent(
    Guid ProductId,
    decimal NewPrice,
    long Version) : IntegrationEvent;
```

| القرار | السبب |
| --- | --- |
| بنبعت `NewPrice` نفسه (Event-Carried State Transfer) | (Basket) يحدّث من غير ما يسأل (Catalog) تاني |
| `Version` مش `OccurredOn` | ساعات السيرفرات بتختلف، والـ (Version) رقم بيزيد و (Catalog) لوحده بيتحكم فيه |
| مفيش (Gap Detection) | كل حدث فيه السعر كامل، فالأحدث يكسب (مش Delta زي "Stock -1") |
| مفيش `OldPrice` | (Basket) مش محتاجه، وإضافة حقل بعدين مش بتكسر حد |
| مفيش `Currency` | (Catalog) معندوش عملة، و (Basket) كاتب `"EGP"` ثابتة |

**قاعدة الـ (Version) في المستقبل:** طبّق الحدث بس لو الـ `Version` بتاعه **أكبر** من اللي عندك. غير كده تجاهله.

### 2. الـ `Product` يحمي سعره

```csharp
// Catalog.Domain/Entities/Product.cs (changes only)
public decimal Price { get; private set; }
public long PriceVersion { get; private set; }

public bool ChangePrice(decimal newPrice)
{
    if (newPrice <= 0)
        throw new DomainException("Price must be greater than zero.");

    if (newPrice == Price)
        return false; // same price: nothing changed, no event

    Price = newPrice;
    PriceVersion++;
    UpdatedAt = DateTime.UtcNow;
    return true;
}
```

```csharp
// Catalog.Domain/Exceptions/DomainException.cs (Catalog has its own, not Ordering's)
public class DomainException(string message) : Exception(message);
```

- باب واحد لتغيير السعر: بيغيّر السعر ويزوّد النسخة مع بعض، فمحدش ينسى خطوة.
- بيرجّع `bool` عشان الـ (Controller) يعرف يبعت حدث ولا لأ.

### 3. (Optimistic Concurrency) على `PriceVersion`

```csharp
// Catalog.Infrastructure/Data/CatalogDbContext.cs
modelBuilder.Entity<Product>()
    .Property(p => p.PriceVersion)
    .IsConcurrencyToken();
```

```sql
UPDATE Products SET Price = 650, PriceVersion = 5
WHERE Id = @id AND PriceVersion = 4   -- the version we read
```

أدمنين قروا نسخة 4 في نفس اللحظة: الأول يحفظ بنسخة 5 ويبعت حدث. التاني يلاقي صفر صفوف ← `DbUpdateConcurrencyException` ← **409**، وتعديله متحفظش خالص. من غير الـ (Token)، الاتنين هيحفظوا نسخة 5 بسعرين، و (Basket) يقبل واحد ويتجاهل التاني.

من (Laravel): زي `Product::where('id', $id)->where('version', $old)->update([...])` وتشيّك لو رجعت 0.

### 4. (Wolverine) والـ (Outbox) في (Catalog)

نفس إعدادات (Basket) في 7.3: شيل `EnableRetryOnFailure`، و `optionsLifetime: Singleton`، و `PersistMessagesWithSqlServer`، و `UseEntityFrameworkCoreTransactions`، و `UseDurableOutboxOnAllSendingEndpoints`، و `AddResourceSetupOnStartup`. ومفيش `UseDurableInbox` لأن (Catalog) بيبعت بس.

```csharp
opts.PublishMessage<ProductPriceChangedIntegrationEvent>()
    .ToRabbitExchange("product-price-changed");
```

وفي الـ (compose): رجع `RabbitMq__HostName: rabbitmq` لـ `catalog-api`، و `depends_on` على `rabbitmq: service_healthy`، و `RabbitMq__UserName` و `RabbitMq__Password` في `src/Services/Catalog/.env`. (اتشال في PR #12 لأنه مكانش مستخدم، ورجع بسبب حقيقي.)

### 5. الـ (Endpoint)

```csharp
// Catalog.API/Controllers/ProductsController.cs
[HttpPut("{id:guid}/price")]
public async Task<IActionResult> ChangePrice(
    Guid id, ChangePriceRequest request, CancellationToken cancellationToken)
{
    var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    if (product is null)
        return NotFound($"المنتج برقم {id} غير موجود في المعرض.");

    bool changed;
    try { changed = product.ChangePrice(request.NewPrice); }
    catch (DomainException ex)
    { return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message); }

    if (!changed)
        return Ok(new { product.Id, product.Price, product.PriceVersion });

    outbox.Enroll(dbContext);
    await outbox.PublishAsync(new ProductPriceChangedIntegrationEvent(
        product.Id, product.Price, product.PriceVersion));

    try { await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken); }
    catch (DbUpdateConcurrencyException)
    {
        return Problem(statusCode: StatusCodes.Status409Conflict,
            title: "السعر اتغير من حد تاني. اعمل Refresh وحاول تاني.");
    }

    await cache.RemoveAsync(ProductKey(id), cancellationToken); // added in 8.6
    return Ok(new { product.Id, product.Price, product.PriceVersion });
}
```

- `ChangePriceRequest(decimal NewPrice)` في فولدر `DTOs` (مش `Contracts`، لأن `Contracts` في الريبو معناها (Interfaces) وعقود الرسايل).
- لو حصل 409، الـ (Transaction) كلها بتتلغي: لا سعر اتحفظ ولا حدث "يتيم".

### المقايضات وإمتى ما تستخدمهاش

| المميزات | العيوب |
| --- | --- |
| أي خدمة تعرف تغيير السعر من غير ما (Catalog) يعرفها | (Catalog) بقى محتاج (RabbitMQ) وقت التشغيل |
| الحدث مستحيل يضيع | جداول (Wolverine) زيادة في `catalog-db` |
| (Version) + (Concurrency) = مفيش لخبطة ترتيب | كود أكتر من `product.Price = x` |

الحدث بيتعمل عشان فيه **نسخة محتاجة تتحدّث**. لو محدش محتفظ بنسخة، ملوش لازمة.

### أسئلة وأجوبة الدرس 8.2

**سؤال 3: (Basket) عنده نسخة 7، وصله 7 تاني، وبعدين 9. يعمل إيه؟**

إجابتك: 7 يتجاهلها، و 9 يطبقها حتى لو 8 مجاتش لأن فيها السعر الكامل. صح.

**سؤال 4: في الموديول 7 عملنا `DomainException` عشان يروح الـ (DLQ) على طول. مهم هنا؟**

سياسة الـ (DLQ) بتشتغل على (Handlers) الرسايل بس، و `ChangePrice` بيتنادى من (HTTP). بس الـ (Custom Exception) مهم لسبب تاني: `ArgumentOutOfRangeException` هيطلع 500 من `GlobalExceptionHandler`، لكن `DomainException` نمسكها ونرجّع 400. وكل خدمة ليها `DomainException` خاصة بيها.

**سؤال 5: إيه دور `IsConcurrencyToken()` بالظبط؟ وهل الأدمنين الاتنين مش هيعدلوا؟**

بيضيف شرط `AND PriceVersion = <اللي قريته>` للـ `UPDATE`. واحد بس بيعدّل، والتاني ياخد 409 وتعديله مبيتحفظش ومبيتحولش لنسخة 6 لوحده. لازم يعمل (Refresh) ويشوف السعر الجديد، يمكن يغيّر رأيه.

**سؤال 6: لو شلنا `IsConcurrencyToken()`، الـ `Version` لوحده هيحمينا؟**

إجابتك: لأ، هيسمح بنسخ متكررة بأسعار مختلفة. صح. الـ `Version` بيحمي من الترتيب الغلط، والـ (Token) بيضمن إن كل نسخة ليها سعر واحد.

**سؤال 7: المفروض اسم الفولدر `DTOs` مش `Contracts`؟**

صح. ومشروع `Catalog.Application` مش لازم دلوقتي، بس لو عملته يبقى زي (Basket): `DTOs/Products/Requests`.

**سؤال 8: ليه `PublishAsync` قبل `SaveChangesAndFlushMessagesAsync`؟**

إجابتك: بيحفظ الحدث في الميموري عشان الحفظ يبقى (Transaction) واحدة مع السعر. صح.

**سؤال 9: لو (RabbitMQ) واقع وغيّرت السعر، الـ (API) يرجّع إيه؟**

أول إجابة كانت واصفة النظام من غير (Outbox) (Retry وبعدين Error). مع الـ (Outbox): **200**، والرسالة في `wolverine_outgoing_envelopes`، والـ (Durability Agent) يبعتها لما (RabbitMQ) يرجع. وصحّحتها بنفسك.

**سؤال إنترفيو 10: الفرق بين (Thin Event) و (Event-Carried State Transfer)؟**

> "الـ (Thin Event) بيقول إن حاجة اتغيرت والمستقبل يسأل بنفسه، فبيعمل ربط وضغط على المصدر. الـ (ECST) بيبعت الداتا نفسها، فالمستقبل يحدّث نسخته لوحده. بختار التاني لما المستقبل محتفظ بنسخة، والأول لما الداتا كبيرة أو حساسة."

إجابتك اختارت (ECST) عشان الخدمتين ميكلموش بعض. صح، وزوّد جملة إمتى **مش** هتختاره: الإنترفيور بيحب يسمع إنك عارف التمن.

## الدرس 8.3: (Basket) يستقبل الحدث ويحدّث السلال

**الخلاصة:** كل منتج في السلة بقى شايل سعره ونسخته (`PriceVersion`). الـ (Handler) بيعمل `UPDATE` واحد بشرط "نسختي أقدم"، فالمكرر والقديم بيعدّلوا صفر صفوف.

### 1. نحفظ النسخة فين؟

|  | (أ) عمود في `ShoppingCartItem` ✅ | (ب) جدول `CatalogProducts` لوحده |
| --- | --- | --- |
| الفكرة | كل منتج في السلة شايل سعره ونسخته | جدول فيه كل المنتجات، والسلة بتاخد منه |
| السهولة | عمود واحد | جدول جديد وتعديل في حساب السلة |
| إمتى أحسن | السلة بس اللي محتاجة السعر | (Basket) يعرض بيانات منتجات من غير ما يسأل (ده الـ Read Model، 8.5) |

### 2. النسخة وقت الإضافة

```csharp
// Basket.Domain/Entities/ShoppingCartItem.cs
public decimal Price { get; set; }
public long PriceVersion { get; set; }   // which Catalog version this price came from

// Basket.Domain/Contracts/ICatalogService.cs
public record CatalogProductDto(Guid Id, string Name, decimal Price, long PriceVersion);
```

```csharp
// BasketController.AddItemToBasket (changes only)
if (existingItem is not null)
{
    existingItem.Quantity += request.Quantity;
    if (product.PriceVersion > existingItem.PriceVersion)   // never overwrite a newer price
    {
        existingItem.Price = product.Price;
        existingItem.PriceVersion = product.PriceVersion;
    }
}
else
{
    cart.Items.Add(new ShoppingCartItem
    {
        ProductId = product.Id, ProductName = product.Name,
        Price = product.Price, PriceVersion = product.PriceVersion,
        Quantity = request.Quantity
    });
}
```

**القاعدة:** أي حد بيكتب السعر لازم يحترم الـ `Version`. الـ (Migration): `AddPriceVersionToCartItems`، والمنتجات القديمة بتاخد 0 فأول حدث يتطبق عليها.

### 3. الـ (Handler)

```csharp
// Basket.API/IntegrationEvents/ProductPriceChangedHandler.cs
public sealed class ProductPriceChangedHandler(
    BasketDbContext dbContext, ILogger<ProductPriceChangedHandler> logger)
{
    public async Task Handle(ProductPriceChangedIntegrationEvent e, CancellationToken ct)
    {
        // One SQL UPDATE: only this product, only if our version is older
        var updatedItems = await dbContext.ShoppingCartItems
            .Where(i => i.ProductId == e.ProductId && i.PriceVersion < e.Version)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Price, e.NewPrice)
                .SetProperty(i => i.PriceVersion, e.Version), ct);

        logger.LogInformation("Price of {ProductId} → {Price} (v{Version}). Updated {Count} cart items",
            e.ProductId, e.NewPrice, e.Version, updatedItems);
    }
}
```

| الشرط في الـ `WHERE` | بيحل إيه |
| --- | --- |
| `ProductId = @id` | يحدّث المنتج ده بس، في كل السلال |
| `PriceVersion < 6` وحدث قديم وصل | نسخة 5 جت بعد 6 ← صفر صفوف ← اتجاهلت |
| `PriceVersion < 6` ونفس الحدث اتكرر | صفر صفوف ← مفيش ضرر |

الـ (Handler) ده **(Naturally Idempotent)**: `UPDATE` بشرط، فمش محتاج (Unique Index) زي 7.4 (هناك كان `INSERT`).

**ليه `ExecuteUpdateAsync` مش (Load + foreach + SaveChanges)؟** الأداء (مش هنحمّل 10,000 صف)، والشيك والتعديل بيحصلوا في نفس اللحظة جوه الداتابيز (مفيش فخ "شيّك وبعدين احفظ").

### 4. الربط بالـ (Queue)

```csharp
// Basket.API/Program.cs inside UseWolverine
opts.UseRabbitMq(f => { /* same */ })
    .DeclareExchange("product-price-changed", ex => ex.BindQueue("basket.product-price-changed"))
    .AutoProvision();

opts.ListenToRabbitQueue("basket.product-price-changed").UseDurableInbox();

opts.OnException<Exception>()
    .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
    .Then.ScheduleRetry(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5))
    .Then.MoveToErrorQueue();
```

- `UseDurableInbox` مش عشان التكرار، عشان **الضياع**: الرسالة تتحفظ في `basket-db` قبل المعالجة.
- مفيش `OnException<DomainException>` هنا: دي موجودة في (Ordering) بس.
- (Basket) بقى الاتنين: بيبعت (`basket-checked-out`) وبيستقبل (`product-price-changed`)، فعنده (Outbox) و (Inbox).

### 5. ثغرة صغيرة وقت الإضافة (مقبولة)

| الوقت | طلب العميل (AddItem) | الـ (Handler) | `basket-db` |
| --- | --- | --- | --- |
| 10:00:00.000 | سأل (Catalog) ← 500، نسخة 5 | — | المنتج مش موجود |
| 10:00:00.020 | — | وصل حدث 700، نسخة 6 | مش موجود |
| 10:00:00.030 | — | ملقاش المنتج ← صفر صفوف | مش موجود |
| 10:00:00.040 | حفظ 500، نسخة 5 | — | المنتج بـ 500 (قديم) |

الحدث اتستهلك بدري، ومش هييجي تاني. الفترة أجزاء من الثانية، واللي بيلقطها هو التحقق وقت الـ (Checkout) في 8.4.

### المقايضات وإمتى ما تستخدمهاش

| المميزات | العيوب |
| --- | --- |
| السلة بتتحدّث لوحدها | السعر بيتأخر شوية (Eventual Consistency) |
| (Basket) شغال لو (Catalog) واقع | ثغرة صغيرة وقت الإضافة |
| الـ (Handler) (Idempotent) من نفسه | عمود زيادة في كل منتج في السلة |

متستخدمهاش لداتا لازم تبقى صح في نفس اللحظة (رصيد، مخزون قبل الحجز). النسخة مقبولة للعرض، مش للقرار النهائي.

### أسئلة وأجوبة الدرس 8.3

**سؤال 11: مفهمتش ثغرة "الـ (Handler) ملقاش المنتج".**

جوه (Basket) حاجتين شغالين في نفس الوقت ومش عارفين عن بعض: طلب الـ (HTTP) والـ (Handler). الجدول اللي فوق بيمشيها بالملّي ثانية.

**سؤال 12: لو (Catalog) شال `PriceVersion` من الـ `GET`، (Basket) يحصله إيه؟**

إجابتك: هيحصل (Exception). غلط: الـ (JSON) الناقص بيدي القيمة الافتراضية (0) بهدوء، فالسعر مش هيتحدّث وقت الإضافة أبدًا. **(Bug) صامت**. الدرس: مترجّعش الـ (Entity) من الـ (API)، رجّع (DTO) صريح زي `ProductResponse` (اتعمل في 8.6).

**سؤال 13: إعدادات (Wolverine) في (Basket) كده صح؟**

صح، وترتيب السطور مش فارق. ما عدا `OnException<DomainException>`: (Basket) معندوش `DomainException`، والـ (Reference) لـ (Ordering) هيربط الخدمتين. اتمسح.

**سؤال 14: نفس الحدث وصل مرتين، `updatedItems` هيطلع كام؟**

صفر. الـ (Handler) بيتنفذ عادي والـ `UPDATE` بيتبعت، بس الشرط مبيلاقيش صفوف. مفيش (Exception)، فـ (Wolverine) يعمل (Ack). "صفر صفوف" معناها "إنت محدّث"، مش "حصل غلط".

**سؤال 15: 950 (نسخة 7) و 1000 (نسخة 8) وصلوا بالعكس. السلة بكام؟**

إجابتك: 950. الصح **1000**: السلة بتاخد النسخة الأكبر، مش آخر رسالة وصلت. تجربتك طلعت 950 لأنك غيّرت لـ 1000 الأول وبعدين 950، فالـ 950 كانت الأحدث. ومع (Queue) و (Consumer) واحد الترتيب الغلط صعب تشوفه بإيدك.

**سؤال 16: ليه `ExecuteUpdateAsync`؟ (سببين)**

إجابتك: يتحقق ويعدّل في نفس اللحظة لو حصلت إضافة مع الحدث، والأداء. صح.

**سؤال إنترفيو 17: إزاي تحمي نسخة محلية من الرسايل المتكررة والمتلخبطة؟**

> "أولًا: المصدر بيدي كل تغيير رقم نسخة بيزيد ومحمي بـ (Optimistic Concurrency). ثانيًا: الحدث شايل الحالة كاملة فالأحدث يكسب. ثالثًا: المستقبل بيعمل (Update) بشرط `version < incoming` في خطوة واحدة. رابعًا: (Durable Inbox) عشان الرسالة متضيعش."

إجابتك قالت التالتة. الإنترفيور بيدوّر على الأربعة.

## الدرس 8.4: التحقق النهائي من السعر وقت الـ (Checkout)

**الخلاصة:** قبل ما نبعت الطلب، (Basket) بيسأل (Catalog) عن أسعار كل المنتجات في **طلب واحد**. لو فيه اختلاف: يصلّح السلة ويرجّع 409. لو (Catalog) واقع: 503. الثغرة اللي في الخطة من الموديول 3 اتقفلت.

### 1. القرارات

| السؤال | القرار | السبب |
| --- | --- | --- |
| نسأل عن كل منتج لوحده؟ | لأ، (Batch Endpoint) واحد | 5 طلبات مقابل 1 (Chatty Communication). زي `whereIn` بدل `find` جوه (Loop) |
| السعر اتغير؟ | نوقف، نصلّح السلة، نرجّع 409 | العميل ميدفعش أكتر من غير ما يوافق، والشركة متخسرش |
| (Catalog) واقع؟ | 503 ومنكملش | الصحة أهم من إن الطلب يعدي. طلب بسعر غلط = فلوس حقيقية |

لاحظ: في عرض السلة اخترنا العكس (السلة تشتغل لو (Catalog) واقع). القرار بيتاخد حسب **تمن الغلط** في كل مكان.

### 2. الـ (Batch Endpoint) في (Catalog)

```csharp
public sealed record ProductPriceResponse(Guid Id, decimal Price, long PriceVersion);

[HttpGet("prices")]   // GET /api/products/prices?ids=a&ids=b
public async Task<IActionResult> GetPrices([FromQuery] Guid[] ids, CancellationToken cancellationToken)
{
    if (ids.Length is 0 or > 100)
        return Problem(statusCode: StatusCodes.Status400BadRequest,
                       title: "ids must contain between 1 and 100 items.");

    var prices = await dbContext.Products
        .AsNoTracking()
        .Where(p => ids.Contains(p.Id))
        .Select(p => new ProductPriceResponse(p.Id, p.Price, p.PriceVersion))
        .ToListAsync(cancellationToken);

    return Ok(prices);   // only the products that exist
}
```

- `"prices"` مش هيتلخبط مع `{id:guid}` لأنه مش (Guid).
- الحد 100 حماية لـ (Catalog)، و `AsNoTracking` و `Select` عشان نقرا 3 أعمدة بس.
- **الـ (Endpoint) ده مش متكاش أبدًا** (8.6).

### 3. الـ (Client) في (Basket)

```csharp
// Basket.Domain/Contracts/ICatalogService.cs
public record CatalogPriceDto(Guid Id, decimal Price, long PriceVersion);
Task<IReadOnlyList<CatalogPriceDto>> GetPricesAsync(
    IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);

// Basket.Infrastructure/ExternalServices/Catalog/CatalogService.cs
var query = string.Join("&", productIds.Select(id => $"ids={id}"));
var response = await httpClient.GetAsync($"/api/products/prices?{query}", cancellationToken);
response.EnsureSuccessStatusCode();   // Catalog down or error → exception
return await response.Content.ReadFromJsonAsync<List<CatalogPriceDto>>(cancellationToken: cancellationToken) ?? [];
```

الـ `CatalogService` متسجل بـ `AddStandardResilienceHandler()`، فالطلب الجديد عليه (Retry) و (Timeout) و (Circuit Breaker) ببلاش. والـ (Retry) آمن لأنه `GET`.

### 4. الـ (Checkout) الجديد

```csharp
// 2) NEW: ask Catalog for the real prices (one call)
IReadOnlyList<CatalogPriceDto> catalogPrices;
try
{
    var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
    catalogPrices = await catalogService.GetPricesAsync(productIds, cancellationToken);
}
catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
{
    logger.LogWarning(ex, "Catalog unavailable during checkout of {UserName}", cart.UserName);
    return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "مش قادرين نتأكد من الأسعار دلوقتي، حاول بعد لحظات.");
}

// 3) NEW: compare the cart with the truth
var currentPrices = catalogPrices.ToDictionary(p => p.Id);
var priceChanges = new List<PriceChangeDTO>();
var removedItems = new List<ShoppingCartItem>();

foreach (var item in cart.Items)
{
    if (!currentPrices.TryGetValue(item.ProductId, out var current))
    { removedItems.Add(item); continue; }               // product is gone from Catalog

    if (current.Price != item.Price)
    {
        priceChanges.Add(new PriceChangeDTO(item.ProductId, item.ProductName, item.Price, current.Price));
        item.Price = current.Price;                      // fix the cart with the real price
        item.PriceVersion = current.PriceVersion;
    }
}

// 4) NEW: something changed → fix the cart, stop, tell the customer
if (priceChanges.Count > 0 || removedItems.Count > 0)
{
    dbContext.ShoppingCartItems.RemoveRange(removedItems);
    await dbContext.SaveChangesAsync(cancellationToken);
    return Conflict(new CheckoutConflictResponseDTO(
        Message: "بعض المنتجات في سلتك اتغيرت. راجع السلة ودوس Checkout تاني.",
        PriceChanges: priceChanges,
        RemovedProducts: removedItems.Select(i => i.ProductName).ToList()));
}

// 5) Everything matches → the same checkout as before (Outbox → Ordering)
```

```csharp
// Basket.Application/DTOs/Checkout/Responses/CheckoutConflictResponseDTO.cs
public sealed record PriceChangeDTO(Guid ProductId, string ProductName, decimal OldPrice, decimal NewPrice);
public sealed record CheckoutConflictResponseDTO(
    string Message, IReadOnlyList<PriceChangeDTO> PriceChanges, IReadOnlyList<string> RemovedProducts);
```

- بنقارن **السعر** مش الـ (Version): لو السعر رجع زي ما هو (900 ← 1000 ← 900) العميل مش محتاج يتزعج.
- بنحفظ السلة المتصلحة قبل الـ 409، فالمرة الجاية تعدي.
- `Conflict(object)` مش `Problem(...)` لأننا بنبعت ليستات، مش رسالة (Error) بعنوان.

### المقايضات وإمتى ما تستخدمهاش

| المميزات | العيوب |
| --- | --- |
| مفيش طلب بسعر غلط | **(Availability):** الـ (Checkout) مربوط بـ (Catalog). لو واقع محدش يشتري |
| العميل عارف إيه اللي اتغير بالظبط | طلب (HTTP) زيادة |
| السلة بتصلّح نفسها | العميل ممكن يدوس (Checkout) مرتين |

التحقق المباشر بيتعمل في اللحظة اللي الغلط فيها مكلف. اسم المنتج أو صورته في السلة مش مستاهلين طلب زيادة.

### أسئلة وأجوبة الدرس 8.4

**سؤال 18: الأدمن غيّر السعر بعد التحقق بأجزاء من الثانية، وقبل ما الحدث يتبعت. (Bug)؟**

إجابتك افترضت إن التحقق هيلقطه. التحقق حصل **قبل** التغيير، فالطلب هيعدي بالسعر القديم. **وده مقبول**: العميل وافق على السعر في لحظة الـ (Checkout) (زي فاتورة اتطبعت). وقفلها 100% محتاج (Lock) عبر الخدمات. أي تحقق في نظام موزّع ليه فترة صغيرة بعده، والهدف إنها تبقى مقبولة للبيزنس.

**سؤال 19: ليه الـ (Batch Endpoint) يرجّع اللي لقاه بس بدل 404؟**

إجابتك لمست النقطة المهمة: المنتج اللي اتمسح من (Catalog) بيفضل في السلة. الـ 404 هتقول "فيه حاجة ناقصة" بس، لكن الليستة بتقول **مين** ناقص وأسعار الباقي، فنرد على العميل في رد واحد. الـ (Endpoint) يرجّع الحقيقة، و (Basket) يقرر.

**سؤال 20: ليه السلة يبقى فيها نفس المنتج في سطرين؟ ولو شلنا `Distinct()`؟**

`AddItemToBasket` بيدمج المكرر، بس مفيش (Unique Index)، فضغطتين في نفس اللحظة ممكن يعملوا سطرين (فخ "شيّك وبعدين احفظ"). من غير `Distinct()` ده **هدر بس**: (Catalog) بيرجّع المنتج مرة واحدة. (Mini Task): `HasIndex("ShoppingCartId", nameof(ShoppingCartItem.ProductId)).IsUnique()`.

**سؤال 21: إيه اللي بيضمن إن المرة التانية هتعدي؟ وإمتى 409 تاني؟**

إجابتك: اتمسح الناقص واتظبط السعر. صح. والـ 409 التاني ملوش علاقة بالأحداث (التحقق بـ HTTP مباشر): بيحصل بس لو حاجة اتغيرت تاني في (Catalog) بين المحاولتين.

**سؤال 22: (RabbitMQ) واقع، ليه الـ (Checkout) نجح مع إن (Basket) لازم يبعت حدث؟**

إجابتك: الحدث بيتخزن في الـ (Outbox) لحد ما يرجع. صح.

**سؤال 23: (Catalog) واقع = 503، وفي 8.3 قلنا (Basket) يشتغل لو (Catalog) واقع. تناقض؟**

إجابتك: لأ، العرض مفيهوش ضرر لكن الشراء لازم يبقى صح. صح: القرار حسب تمن الغلط.

**سؤال إنترفيو 24: إزاي تضمن إن الطلب ميتعملش بسعر قديم؟ وإيه المقايضة؟**

إجابتك ذكرت التأخير. المقايضة الأكبر هي الـ (Availability).

> "النسخة المحلية بتتحدث بالأحداث وبتخدم العرض. وقت الـ (Checkout) بسأل صاحب السعر مباشرة في طلب (Batch)، ولو فيه اختلاف بصلّح السلة وأرجّع 409. ضحيت بجزء من الـ (Availability) مقابل إن الطلب دايمًا بالسعر الصح، وفيه فترة أجزاء من الثانية بعد التحقق مقبولة."

## الدرس 8.5: (CQRS) والـ (API Composition) والـ (Read Model)

**الخلاصة:** مفيش `JOIN` بين الخدمات. شاشة محتاجة داتا من خدمتين إما حد يجمّع وقت الطلب (Composition)، أو جدول جاهز بيتملي بالأحداث (Read Model). وجوه الخدمة الواحدة، القراءة ليها طريق منفصل عن الكتابة (CQRS).

### 1. المشكلة

(Ordering) كان فيه `confirm` و `cancel` بس، **ومفيش ولا `GET`**. وصفحة "طلباتي" محتاجة الحالة والإجمالي (Ordering) + صورة المنتج وسعره الحالي (Catalog).

### 2. (API Composition) مقابل (Read Model)

|  | (API Composition) | (Read Model) |
| --- | --- | --- |
| الفكرة | يسأل الخدمتين ويدمج في الميموري | جدول جاهز للشاشة بيتملي بالأحداث |
| السهولة | سهل | أحداث و (Handler) وجدول |
| الداتا | دايمًا حديثة | متأخرة شوية |
| لو خدمة واقعة | جزئيًا | شغال |
| فلترة وترتيب عبر الخدمات | صعب | استعلام عادي |

**القاعدة:** ابدأ بالـ (Composition). انقل للـ (Read Model) لما الفلترة أو الحد (Limit) محتاج داتا الخدمتين مع بعض، أو الأداء والاعتمادية مهمين. والسعر و `PriceVersion` في `ShoppingCartItem` (8.3) هما (Read Model) صغير.

### 3. (CQRS) بالبلدي

افصل الكتابة (Command: `Confirm` و `Cancel` و `Checkout`) عن القراءة (Query: "هات طلباتي"). الكتابة محتاجة الـ `Order` (Aggregate) بالقواعد، والقراءة محتاجة داتا مسطحة وسريعة.

| المستوى | الفكرة | عندنا |
| --- | --- | --- |
| 1 | نفس الداتابيز، كود القراءة منفصل | ✅ `IOrderQueries` |
| 2 | جداول أو داتابيز منفصلة للقراءة، بتتملي بالأحداث | الـ (Read Model) |

(CQRS) مش لازم داتابيز تانية، ومش (Event Sourcing)، ومش محتاج (MediatR) (اللي بقى مدفوع). من (Laravel): (Eloquent Model) بالـ (Methods) للكتابة، و `DB::table()->select(...)` للتقارير.

### 4. طريق القراءة في (Ordering)

```csharp
// Ordering.Application/Orders/Queries/IOrderQueries.cs
public sealed record OrderItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderSummaryDto(Guid Id, string Status, DateTime CreatedAt,
    string Currency, decimal Total, IReadOnlyList<OrderItemDto> Items);

public interface IOrderQueries
{
    Task<IReadOnlyList<OrderSummaryDto>> GetByBuyerAsync(string buyerId, CancellationToken ct);
}
```

```csharp
// Ordering.Infrastructure/Queries/OrderQueries.cs
public sealed class OrderQueries(OrderingDbContext db) : IOrderQueries
{
    public async Task<IReadOnlyList<OrderSummaryDto>> GetByBuyerAsync(string buyerId, CancellationToken ct) =>
        await db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryDto(
                o.Id.Value, o.Status.ToString(), o.CreatedAt, o.Currency,
                o.Items.Sum(i => i.UnitPrice.Amount * i.Quantity),   // computed in SQL
                o.Items.Select(i => new OrderItemDto(
                    i.ProductId, i.ProductName, i.UnitPrice.Amount, i.Quantity)).ToList()))
            .ToListAsync(ct);
}
```

```csharp
// Program.cs
builder.Services.AddScoped<IOrderQueries, OrderQueries>();

// OrdersController: GET api/orders?buyerId=ahmed
[HttpGet]
public async Task<IActionResult> GetByBuyer([FromQuery] string buyerId, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(buyerId))
        return Problem(statusCode: StatusCodes.Status400BadRequest, title: "buyerId is required.");
    return Ok(await orderQueries.GetByBuyerAsync(buyerId, cancellationToken));
}
```

- الـ (DTO) فيه `decimal` و `string`. مفيش `Money` ولا `OrderId`: الشاشة مش محتاجة القواعد.
- الكتابة ← `orderRepository`، والقراءة ← `orderQueries`. ده (CQRS) في أبسط صوره.
- مؤجل: `buyerId` من الـ (JWT) في الموديول 10، و (Pagination) بـ `Skip` و `Take` (Mini Task).

### 5. الـ (Composition) لصفحة "طلباتي" (هيتبني في الموديول 9)

مكانه الـ (Gateway) أو الـ (BFF) (Backend For Frontend)، مش الـ (Frontend) (طلبات كتير على الموبايل) ولا (Ordering) (يتربط بـ Catalog عشان صور).

```csharp
// BFF endpoint: GET /my-orders
var orders = await orderingClient.GetByBuyerAsync(buyerId, ct);          // REQUIRED
var productIds = orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList();

var products = new Dictionary<Guid, ProductSummary>();
try { products = (await catalogClient.GetSummariesAsync(productIds, ct)).ToDictionary(p => p.Id); }
catch (Exception ex) when (!ct.IsCancellationRequested)
{ logger.LogWarning(ex, "Catalog unavailable, returning orders without images"); }   // OPTIONAL
```

| الفخ | الحل |
| --- | --- |
| طلب لكل منتج (N+1) | `Distinct` وطلب (Batch) واحد |
| خدمة واحدة توقّع الصفحة | داتا ضرورية (Error) وداتا اختيارية (نكمل من غيرها): (Graceful Degradation) |
| الطلبات ورا بعض | لو مش معتمدين على بعض ← `Task.WhenAll` |

قارنها بـ 8.4: هناك (Catalog) واقع = 503 (السعر ضروري). هنا = صفحة من غير صور (الصورة اختيارية).

### إمتى ما تستخدمهاش

- **(Read Model):** لو الـ (Composition) شغال ومفيش فلترة عبر الخدمات.
- **فصل القراءة:** لو الخدمة (CRUD) بسيطة زي (Catalog) دلوقتي.

### أسئلة وأجوبة الدرس 8.5

**سؤال 25: "أكتر 10 منتجات اتباعت، مترتبين حسب سعرهم الحالي". (Composition) ولا (Read Model)؟**

إجابتك: (Read Model) "من غير نقاش". المبدأ صح، بس هنا (Ordering) يحسب أكتر 10 لوحده، وطلب (Batch) واحد لـ 10 أسعار، ونرتّب 10 في الميموري. الـ (Composition) سهل. الـ (Read Model) يلزم لما الفلترة قبل الحد: "أغلى 10 من كل اللي اتباع".

**سؤال 26: مش المفروض `OrderId` و `OrderStatus` في فولدر `ValueObjects`؟**

لأ، مكانهم صح. `OrderStatus` (Enum) مش (Value Object). و `OrderId` (Value Object) بس خاص بالـ `Order`. التقسيم حسب **مين بيستخدمها**: `ValueObjects` للمشترك (`Money` و `Address`)، و `Orders` لكل حاجة تخص الطلب.

**سؤال 27: ليه مستخدمناش الـ (Repository) و `order.Total`؟**

إجابتك: الـ (Repository) للكتابة وبيحمّل الـ `Order` كامل (حمل كبير)، و `Total` في الميموري بس ومش هينفع مع (DTO) جاي من الداتابيز. صح. وسبب تالت: منقدرش نرتّب أو نفلتر بـ `Total` في (SQL).

**سؤال 28: لو (Ordering) واقع أو (Catalog) واقع، الـ (BFF) يرجّع إيه؟ وصفحة "حسابي" (Basket + Ordering) ورا بعض ولا مع بعض؟**

إجابتك صح: (Ordering) أساسي ← 503، و (Catalog) اختياري ← 200 والصور `null`. والصفحة التانية مع بعض بـ `Task.WhenAll`: الوقت = الأبطأ مش المجموع، وكل واحد يفشل لوحده.

**سؤال إنترفيو 29: الفرق بين (API Composition) و (CQRS Read Model)؟**

> "الـ (Composition) بيسأل الخدمات وقت الطلب ويجمّع في الميموري: سهل والداتا حديثة، بس بيعتمد على إن الخدمات شغالة وضعيف في الفلترة عبر الخدمات. الـ (Read Model) جدول جاهز بيتملي بالأحداث: سريع وشغال لوحده وبيعرف يفلتر، بس الداتا متأخرة شوية. ببدأ بالـ (Composition) وبنقل لما أحتاج فلترة عبر الخدمات أو أداء عالي."

## الدرس 8.6: الكاش بـ (HybridCache) و (Redis)

**الخلاصة:** تفاصيل المنتج بتتقرا كتير وبتتغير نادرًا، فبنكاشها (Cache-Aside) في الميموري (L1) و (Redis) (L2)، ونمسحها بعد تغيير السعر. واللي بنتأكد بيه من الحقيقة **مش بيتكاش**.

### 1. (Cache-Aside)

```
GET /api/products/{id}
  1. شوف في الكاش ← موجود (Hit): رجّعه
  2. مش موجود (Miss) ← هاته من الداتابيز ← خزّنه لمدة ← رجّعه
لما السعر يتغير ← امسحه من الكاش (Invalidation)
```

من (Laravel): `Cache::remember("product:$id", 600, fn () => Product::find($id))`.

### 2. نكاش إيه؟

| الحاجة | نكاشها؟ | ليه |
| --- | --- | --- |
| `GET /api/products/{id}` | أيوه | بيتقرا كتير وبيتغير نادرًا |
| `GET /api/products/prices` | **أبدًا** | ده التحقق النهائي. لو كاشناه رجّعنا ثغرة 8.4 |
| السلة كلها في (Redis) | لأ (دلوقتي) | الـ (Checkout) بيعتمد على (Outbox) في نفس (Transaction) الـ (SQL)، و (Redis) مينفعش يدخل معاها (Dual-write) |
| طلبات (Ordering) | بعدين | مفيش ضغط لسه |

**القاعدة الذهبية:** متكاشش الحاجة اللي بتستخدمها عشان تتأكد من الحقيقة.

### 3. (Redis) ولا (Valkey)؟

(Redis) لحد 7.2 كان (BSD)، ومن 7.4 بقى رخصة مش مفتوحة، ومع (Redis 8) اتضافت (AGPLv3) المفتوحة ([redis.io](https://redis.io/legal/licenses/)). (Valkey) نسخة المجتمع برخصة (BSD 3-Clause) تحت (Linux Foundation). الاتنين مجانيين وبيتكلموا نفس البروتوكول، و (AGPL) بتلزمك بس لو عدّلت في (Redis) نفسه. **القرار:** `redis:8-alpine`، والتحويل لـ (Valkey) = اسم الصورة بس.

### 4. (Redis) في الـ (compose)

```yaml
redis:
  image: redis:8-alpine
  container_name: ecommerce-redis
  hostname: redis
  # Cache only: no disk persistence, max 256MB, drop least-used keys when full
  command: ["redis-server", "--save", "", "--appendonly", "no",
            "--maxmemory", "256mb", "--maxmemory-policy", "allkeys-lru"]
  ports: ["127.0.0.1:6379:6379"]
  networks: [ecommerce]
  healthcheck:
    test: ["CMD", "redis-cli", "ping"]
    interval: 10s
    timeout: 3s
    retries: 5
  restart: unless-stopped

# catalog-api: ConnectionStrings__Redis: redis:6379 + depends_on redis (service_healthy)
```

- مفيش حفظ على الهارد: ده كاش، الداتا في الداتابيز.
- `allkeys-lru`: لو اتملى يمسح الأقل استخدامًا (رد على قلق "هيتملي" في 8.1).
- (Redis) هنا لـ (Catalog) بس، ومفاتيحه بتبدأ بـ `catalog:`.

### 5. التسجيل والكود

الباكدجات (MIT): `Microsoft.Extensions.Caching.Hybrid` و `Microsoft.Extensions.Caching.StackExchangeRedis` (الإصدارات في `Directory.Packages.props`).

```csharp
// Catalog.API/Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "catalog:";
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),           // how long in Redis
        LocalCacheExpiration = TimeSpan.FromSeconds(30)  // how long in memory (short on purpose)
    };
});
```

```csharp
// ProductsController
public sealed record ProductResponse(
    Guid Id, string Name, string Description, decimal Price, long PriceVersion, string ImageUrl);

private static string ProductKey(Guid id) => $"product:{id}";

[HttpGet("{id:guid}")]
public async Task<IActionResult> GetProductById(Guid id, CancellationToken cancellationToken)
{
    var product = await cache.GetOrCreateAsync(
        ProductKey(id),
        async ct => await dbContext.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Description, p.Price, p.PriceVersion, p.ImageUrl))
            .FirstOrDefaultAsync(ct),
        cancellationToken: cancellationToken);

    return product is null ? NotFound($"المنتج برقم {id} غير موجود في المعرض.") : Ok(product);
}

// ChangePrice: AFTER SaveChangesAndFlushMessagesAsync succeeds
await cache.RemoveAsync(ProductKey(id), cancellationToken);
```

- المسح **بعد** الحفظ: لو قبله، طلب يدخل بين المسح والحفظ يرجّع السعر القديم للكاش 10 دقايق.
- `ProductKey` في مكان واحد: حرف غلط في المسح = المسح مش بيشتغل ومحدش ياخد باله.
- الـ (DTO) مش الـ (Entity): عقد صريح مع (Basket) (درس سؤال 12).

### 6. L1 و L2

الترتيب: L1 (ميموري النسخة) ← L2 (Redis) ← الداتابيز، واللي يتجاب بيتخزن في الأقرب.

|  | L1 (ميموري) | L2 (Redis) |
| --- | --- | --- |
| السرعة | الأسرع (مفيش شبكة) | سريع (فيه شبكة) |
| مشترك بين النسخ | لأ | أيوه: الداتابيز تتسأل مرة لكل النسخ |
| بيعيش بعد الـ (Restart) | لأ | أيوه |

L1 قصير (30 ثانية) لأن المسح بيشيل من (Redis) ومن ميموري النسخة اللي غيّرت بس، والنسخ التانية ممكن تفضل بالقديم لحد ما ينتهي.

### 7. الفخاخ

| الفخ | التعامل |
| --- | --- |
| (Cache Stampede): مفتاح انتهى و 500 طلب في نفس الثانية | (HybridCache) بيخلي الطلبات على نفس المفتاح تستنى عملية واحدة جوه النسخة: 3 نسخ = 3 استعلامات بالكتير |
| كاش الـ "مش موجود" | الـ `null` بيتخزن 10 دقايق. لما يتعمل (Endpoint) إضافة منتج، يمسح المفتاح |
| داتا خاصة بالمستخدم | المفتاح لازم يبقى فيه (Id) العميل |
| المسح فشل أو اتنسى | مدة الانتهاء (10 دقايق) خطة احتياطية |

### المقايضات وإمتى ما تستخدمهوش

| المميزات | العيوب |
| --- | --- |
| ضغط أقل على الداتابيز ورد أسرع | الداتا ممكن تبقى قديمة لحد المسح أو الانتهاء |
| (Catalog) بيقرا المشهور حتى لو الداتابيز وقعت شوية | حاجة جديدة تتراقب، ولازم تمسح في كل مكان بيغيّر الداتا |

متستخدمهوش لداتا بتتغير كتير (المخزون)، أو لازم تبقى صح 100%، أو لو مفيش ضغط أصلًا. قيس الأول.

### التجارب (الأربعة اتعملوا وطلعوا زي المتوقع)

1. **(Miss) ثم (Hit):** `redis-cli KEYS "catalog:*"` و `TTL` حوالي 600.
2. **`catalog-db` واقعة:** المنتج المتكاش 200، واللي مش متكاش 503.
3. **المسح:** غيّر السعر ← السعر الجديد على طول.
4. **(Redis) واقع:** (Catalog) كمّل من الداتابيز عادي.

### أسئلة وأجوبة الدرس 8.6

**سؤال 30: لو كاشنا المنتج 10 دقايق ومحدش مسح الكاش، السلة يحصلها إيه؟**

إجابتك: هتسجل سعر قديم، ومش هيتصلح غير بتغيير جديد أو الـ (Checkout). صح. يعني نفس ثغرة 8.3 بس بدل أجزاء من الثانية بقت 10 دقايق، عشان كده المسح وقت تغيير السعر إجباري.

**سؤال 31: ليه مدة انتهاء مادام بنمسح بإيدنا؟**

إجابتك: عشان يمسح المش مستخدم ويوفّر مساحة. ده شغل `allkeys-lru`. المدة **خطة احتياطية**: المسح فشل لأن (Redis) وقع، أو حد غيّر السعر بطريقة تانية ونسي يمسح، أو سباق نادر. المدة بتضمن إن الغلط ليه نهاية.

**سؤال 32: ليه محتاج L1 و L2؟ والطلب بيجيب من أنهي واحد؟**

بالترتيب الأقرب الأول (الجدول فوق). مع 3 نسخ: L1 لوحده = كل نسخة تضرب الداتابيز، و L2 لوحده = رحلة شبكة كل مرة. الاتنين مع بعض: الداتابيز مرة لكل النسخ، والمتكرر على نفس النسخة من غير شبكة.

**سؤال 33: نحط الكاش في (Resource Filter) عشان نوفّر شغل الـ (Controller)؟**

ينفع، بس مش هنا: التوفير (Microseconds) لأن المكلف هو الداتابيز، والمسح هيبقى أصعب (مفتاح من الـ URL)، والفلاتر للحاجات العامة (Cross-cutting). الأحسن كلاس `IProductQueries` يشيل الكاش (Mini Task). وللرد الكامل فيه (Output Caching) جاهز `[OutputCache]` لصفحات عامة.

**سؤال 34: المنتج المتكاش 200 والتاني 503. ده بيقول إيه؟**

إجابتك صح. والدرس: الكاش بيحسّن الاعتمادية **جزئيًا** بس. بيخفف الضربة، مش بديل لداتابيز شغالة.

**سؤال 35: ليه بنمسح بعد `SaveChanges`؟**

صح. لو قبله، طلب بين المسح والحفظ يقرا السعر القديم ويرجّعه للكاش.

**سؤال إنترفيو 36: اشرح (Cache-Aside) والبيانات القديمة والـ (Stampede).**

> "أدوّر في الكاش، ولو مش موجود أجيب من الداتابيز وأخزّن. البيانات القديمة بتتحل بالمسح بعد كل تعديل ناجح، ومدة انتهاء كخطة احتياطية. والـ (Stampede) بيتحل بإن طلب واحد بس يجيب الداتا والباقي يستنوه."

## رحلة السعر بعد الموديول 8

```
Price change path:
Admin PUT /price ──► Catalog: ChangePrice (Version+1) + Outbox + cache remove
                       ──► RabbitMQ: product-price-changed
                             ──► Basket handler: UPDATE cart items WHERE PriceVersion < Version

Checkout path:
Customer POST /checkout ──► Basket ──GET /prices (sync, no cache)──► Catalog
                              ├─ Catalog down     → 503, no order
                              ├─ price changed    → fix cart + 409 (what changed)
                              └─ same price       → Outbox → basket-checked-out → Ordering (202)
```

الصف اللي فوق بيخلي السلة غالبًا صح (حدث و Version)، واللي تحت بيخلي الطلب دايمًا صح: أي اختلاف بيرجّع 409، و (Catalog) واقع بيرجّع 503 من غير طلب.

## المشاكل اللي قابلتها وحلولها

أهم درس هنا: **اقرا اللوج الأول قبل ما تخمّن.** مشكلة 1 اتشخّصت الأول غلط (إنها بسبب RabbitMQ)، واللوج قال حاجة تانية خالص.

| # | العرض | السبب | الحل |
| --- | --- | --- | --- |
| 1 | `basket-api` في (Restart Loop)، و `Restarting (134)` في `docker compose ps` | `PendingModelChangesWarning`: الـ (Model) اتغيّر (`PriceVersion`) من غير (Migration)، والـ (Fail-fast) بتاع الموديول 4 قفل الخدمة (وده شغله الصح) | `dotnet ef migrations add AddPriceVersionToCartItems -p .../Basket.Infrastructure -s .../Basket.API -o Data/Migrations` |
| 2 | تحذير `No store type was specified for the decimal property 'Price'` | السعر من غير (Precision)، فـ (EF Core) هيقصّ الأرقام الكبيرة من غير ما يقول | `HasPrecision(18, 2)` على `ShoppingCartItem.Price` قبل الـ (Migration) |
| 3 | مش قادر أعمل تجربة 8.4 بإيقاف (RabbitMQ) | (Basket) محتاج (RabbitMQ) وقت التشغيل (`AutoProvision`) | بدل إيقافه: (Unbind) للـ (Queue) من لوحة (RabbitMQ)، فالحدث ميوصلش للسلة. والـ (Restart) بيرجّع الـ (Binding) |
| 4 | (وقائي) `OnException<DomainException>` اتنسخ لـ (Basket) | (Basket) معندوش `DomainException`، والـ (Reference) لـ (Ordering) بيربط الخدمتين | اتمسح السطر، وفضلت سياسة `OnException<Exception>` |
| 5 | (وقائي) `Currency` في عقد الحدث | اتحط قبل قراية الكود، و (Catalog) معندوش عملة | اتشال. إضافة حقل بعدين مش بتكسر حد |
| 6 | (وقائي) (Bug) صامت لو `PriceVersion` اتشال من الـ `GET` | (Catalog) بيرجّع الـ (Entity) مباشرة، والـ (JSON) الناقص بيدي 0 بهدوء | `ProductResponse` (DTO) صريح |
| 7 | (وقائي) المسح قبل `SaveChanges` | طلب بين المسح والحفظ يرجّع السعر القديم للكاش | `RemoveAsync` بعد الحفظ الناجح |

**دليل مشكلة 1:** (Ordering) كان `Up (healthy)` مع إن عنده (Listener) برضه، فـ (RabbitMQ) مكانش السبب. واللوج فيه `Database migration failed. Shutting down.` من `ServiceDefaults.cs`.

## الملخص السريع (Cheat Sheet)

| المفهوم | في جملة | في المشروع |
| --- | --- | --- |
| (Stale Data) | نسخة اتاخدت ومحدش قالها إنها بقت قديمة | السعر في السلة قبل الموديول |
| (Source of Truth) | الخدمة صاحبة الداتا | (Catalog) للسعر |
| (Event-Carried State Transfer) | حدث شايل الداتا نفسها | `ProductPriceChanged(NewPrice, Version)` |
| (Thin Event) | حدث فيه (Id) بس، والمستقبل يسأل | ماستخدمناهوش |
| (Version) | رقم بيزيد مع كل تغيير، الأحدث يكسب | `PriceVersion` |
| (Optimistic Concurrency) | احفظ بس لو محدش غيّر بعد ما قريت | `IsConcurrencyToken()` ← 409 |
| (Naturally Idempotent) | العملية نفسها مبتتأثرش بالتكرار | `UPDATE ... WHERE PriceVersion < @v` |
| `ExecuteUpdateAsync` | `UPDATE` واحد من غير تحميل | `ProductPriceChangedHandler` |
| (Batch Endpoint) | طلب واحد لكذا (Id) | `GET /api/products/prices?ids=...` |
| (Chatty Communication) | خدمتين بيتكلموا كتير على الفاضي | اتجنبناها بالـ (Batch) |
| (CQRS) مستوى 1 | طريق قراءة منفصل في نفس الداتابيز | `IOrderQueries` |
| (API Composition) | حد يسأل الخدمات ويجمّع | (BFF) في الموديول 9 |
| (Read Model) | جدول جاهز للشاشة بيتملي بالأحداث | السعر في `ShoppingCartItem` |
| (Graceful Degradation) | الداتا الاختيارية تغيب والصفحة تشتغل | "طلباتي" من غير صور |
| (Cache-Aside) | شوف الكاش، وإلا الداتابيز وخزّن | `cache.GetOrCreateAsync` |
| (Invalidation) | امسح الكاش بعد التعديل الناجح | `cache.RemoveAsync` في `ChangePrice` |
| L1 / L2 | ميموري النسخة / (Redis) المشترك | 30 ثانية / 10 دقايق |
| (Cache Stampede) | طلبات كتير على مفتاح فاضي في نفس اللحظة | (HybridCache) بيجمّعهم في عملية واحدة |

### أهم 10 قواعد

1. **الأحداث بتخلي النسخة غالبًا صح، والتحقق المباشر بيخلي القرار دايمًا صح.**
2. **القرار حسب تمن الغلط:** عرض قديم مقبول، طلب بسعر غلط لأ.
3. **الحدث يشيل الحالة كاملة + (Version)**، فالأحدث يكسب ومش محتاج (Gap Detection).
4. **الـ (Version) من غير (Concurrency Token) ملوش قيمة:** كل نسخة لازم يبقى ليها سعر واحد.
5. **أي حد بيكتب النسخة يحترم الـ (Version)** (الـ Handler و AddItem و Checkout).
6. **الشيك والتعديل في خطوة واحدة جوه الداتابيز** (`ExecuteUpdateAsync` بشرط).
7. **مترجّعش (Entity) من الـ (API)**، العقد يبقى (DTO) صريح.
8. **ابعت طلب (Batch) واحد** بدل طلب لكل عنصر.
9. **القراءة مش محتاجة الـ (Aggregate):** (Projection) لـ (DTO) مسطح.
10. **متكاشش الحقيقة**، وامسح الكاش **بعد** الحفظ، ومدة الانتهاء خطة احتياطية.

## اختبر نفسك

جاوب من دماغك الأول، وبعدين بص على الإجابة المختصرة.

1. **ليه السعر في السلة بيبقى قديم من غير ما حد يغلط؟**
   - لأنه صورة اتاخدت وقت الإضافة، وكل خدمة ليها داتابيز لوحدها.
2. **ليه المخزون مش بياخد نفس معاملة السعر؟**
   - بيتغير مع كل طلب ومحتاج حجز، مش نسخة (الموديول 11).
3. **ليه بنبعت `NewPrice` في الحدث بدل `ProductId` بس؟**
   - عشان (Basket) يحدّث من غير ما يسأل (Catalog)، ويشتغل حتى لو (Catalog) واقع.
4. **ليه `Version` مش `OccurredOn`؟**
   - ساعات السيرفرات بتختلف، والـ (Version) رقم بيزيد واحد واحد ومصدره واحد.
5. **ليه مش محتاجين (Gap Detection) في حدث السعر؟**
   - لأن كل حدث فيه السعر كامل، فنسخة 6 كفاية حتى لو 5 ضاعت.
6. **أدمنين غيّروا السعر في نفس اللحظة. إيه اللي بيحصل؟**
   - واحد بينجح ويبعت حدث، والتاني ياخد 409 ومفيش حاجة من تعديله بتتحفظ.
7. **(RabbitMQ) واقع والأدمن غيّر السعر. الرد والرسالة؟**
   - 200، والرسالة مستنية في `wolverine_outgoing_envelopes` لحد ما يرجع.
8. **نفس حدث السعر وصل مرتين. ليه مش محتاجين (Unique Index)؟**
   - الـ (Handler) بيعمل `UPDATE` بشرط `PriceVersion < @v`، فالمرة التانية تعدّل صفر صفوف.
9. **السعر في السلة 900 والحقيقي 1000. الـ (Checkout) يعمل إيه؟**
   - يصلّح السلة ويحفظها ويرجّع 409 بالتغيير، والمرة الجاية تعدي.
10. **(Catalog) واقع وقت الـ (Checkout)؟ ووقت صفحة "طلباتي"؟**
    - (Checkout): 503، لأن السعر ضروري. "طلباتي": 200 من غير صور، لأن الصورة اختيارية.
11. **ليه مستخدمناش `orderRepository` في `GET /api/orders`؟**
    - لأنه للكتابة: بيحمّل الـ (Aggregate) كامل، و `Total` مش عمود فمنقدرش نفلتر بيه في (SQL).
12. **إمتى أنقل من (API Composition) لـ (Read Model)؟**
    - لما الفلترة أو الـ (Limit) محتاج داتا الخدمتين مع بعض، أو الأداء والاعتمادية مهمين.
13. **ليه `GET /api/products/prices` مش متكاش؟**
    - لأنه التحقق النهائي، والكاش ممكن يبقى قديم.
14. **ليه السلة مش في (Redis)؟**
    - الـ (Checkout) بيعتمد على (Outbox) في نفس (Transaction) الـ (SQL)، و (Redis) مينفعش يدخل معاها.
15. **ليه L1 مدته 30 ثانية بس؟**
    - المسح مش بيوصل لميموري النسخ التانية، فالغلط ميطولش أكتر من 30 ثانية.

### أسئلة إنترفيو

- *"(Thin Event) مقابل (Event-Carried State Transfer)؟"* (الإجابة في 8.2)
- *"إزاي تحمي نسخة محلية من الرسايل المتكررة والمتلخبطة؟"* (8.3)
- *"إزاي تضمن إن الطلب ميتعملش بسعر قديم؟ وإيه المقايضة؟"* (8.4)
- *"(API Composition) مقابل (CQRS Read Model)؟"* (8.5)
- *"اشرح (Cache-Aside) والبيانات القديمة والـ (Stampede)"* (8.6)

## الملفات اللي اتغيرت في الريبو و ADR-005

| الملف | التغيير | الدرس |
| --- | --- | --- |
| `BuildingBlocks.Contracts/Catalog/ProductPriceChangedIntegrationEvent.cs` | ملف جديد | 8.2 |
| `Catalog.Domain/Entities/Product.cs` | `private set` للسعر، و `PriceVersion`، و `ChangePrice` | 8.2 |
| `Catalog.Domain/Exceptions/DomainException.cs` | ملف جديد | 8.2 |
| `Catalog.Infrastructure/Data/CatalogDbContext.cs` | `IsConcurrencyToken()` + (Migration) `AddPriceVersionToProducts` | 8.2 |
| `Catalog.API/Catalog.API.csproj` | (Reference) لـ `Messaging` و `Contracts`، وباكدجات (HybridCache) و (StackExchangeRedis) | 8.2 و 8.6 |
| `Catalog.API/Program.cs` | (Wolverine) + (Outbox)، وشيل `EnableRetryOnFailure`، و (HybridCache) + (Redis) | 8.2 و 8.6 |
| `Catalog.API/Controllers/ProductsController.cs` | `PUT {id}/price`، و `GET prices`، و `GET {id}` بالكاش و `ProductResponse` | 8.2 و 8.4 و 8.6 |
| فولدر الـ (DTOs) في (Catalog) | `ChangePriceRequest` و `ProductPriceResponse` و `ProductResponse` | 8.2 و 8.4 و 8.6 |
| `Basket.Domain/Entities/ShoppingCartItem.cs` | `PriceVersion` | 8.3 |
| `Basket.Domain/Contracts/ICatalogService.cs` | `PriceVersion` في `CatalogProductDto`، و `CatalogPriceDto` و `GetPricesAsync` | 8.3 و 8.4 |
| `Basket.Infrastructure/ExternalServices/Catalog/CatalogService.cs` | `GetPricesAsync` | 8.4 |
| `Basket.Infrastructure/Data/BasketDbContext.cs` | `HasPrecision(18, 2)` للسعر + (Migration) `AddPriceVersionToCartItems` | 8.3 |
| `Basket.API/IntegrationEvents/ProductPriceChangedHandler.cs` | ملف جديد | 8.3 |
| `Basket.API/Program.cs` | `DeclareExchange` + `ListenToRabbitQueue(...).UseDurableInbox()` + سياسة الأخطاء | 8.3 |
| `Basket.API/Controllers/BasketController.cs` | `PriceVersion` في `AddItemToBasket`، والتحقق في `Checkout` | 8.3 و 8.4 |
| `Basket.Application/DTOs/Checkout/Responses/CheckoutConflictResponseDTO.cs` | ملف جديد | 8.4 |
| `Ordering.Application/Orders/Queries/IOrderQueries.cs` | ملف جديد | 8.5 |
| `Ordering.Infrastructure/Queries/OrderQueries.cs` | ملف جديد | 8.5 |
| `Ordering.API/Controllers/OrdersController.cs` و `Program.cs` | `GET /api/orders` وتسجيل `IOrderQueries` | 8.5 |
| `docker-compose.yml` | خدمة `redis`، و `catalog-api`: متغيرات (RabbitMQ) و (Redis) و `depends_on` | 8.2 و 8.6 |
| `src/Services/Catalog/.env` و `.env.example` | `RabbitMq__UserName` و `RabbitMq__Password` | 8.2 |
| `docs/adr/ADR-005-data-across-services.md` | ملف جديد | القفل |

### ADR-005 (مختصر)

**الحالة:** مقبول · 2026-11-22. النسخة الكاملة في ملف `ADR-005-data-across-services.md` اللي اتبعت في الشات.

1. (Catalog) بيبعت `ProductPriceChanged(ProductId, NewPrice, Version)` بـ (Outbox).
2. `PriceVersion` بيزيد مع كل تغيير ومحمي بـ (Concurrency Token)، والتعارض ← 409.
3. (Basket) بيحدّث `ShoppingCartItem` بـ `ExecuteUpdateAsync` مشروط، مع (Durable Inbox).
4. التحقق وقت الـ (Checkout) (Sync) بـ (Batch Endpoint): اختلاف ← 409، و (Catalog) واقع ← 503.
5. (CQRS) مستوى 1 في (Ordering)، والـ (Composition) في (BFF) الموديول 9.
6. (HybridCache) + `redis:8-alpine` لتفاصيل المنتج بس، والمسح بعد الحفظ.

**بقرار:** مش هنكاش أسعار الـ (Checkout)، والسلة تفضل في (SQL) بسبب الـ (Outbox)، و (Redis) مش مشترك.

**اترفض:** (Thin Event)، والسؤال الدايم، والأحداث من غير تحقق، وفحص الـ (Inbox)، و `OccurredOn`، و (Load + foreach)، و (Redis) مشترك، والكاش في (Resource Filter).

## قائمة مهام القفل

- [x] الدروس 8.1 → 8.6 اتشرحت، والتجارب اتعملت (تغيير السعر، تحديث السلة، 409 و 503 في الـ Checkout، تجارب الكاش الأربعة).
- [x] ملف الملاحظات ده، و ADR-005.
- [ ] انسخ ADR-005 لـ `docs/adr/ADR-005-data-across-services.md`.
- [ ] انسخ الملف ده لـ `docs/notes/08-data-across-services.md`.
- [ ] حدّث `docs/PROJECT_CONTEXT.md` و `docs/LEARNING_PROGRESS.md`.
- [ ] (Commit) و (PR) للموديول 8 ودمجه في (main).
- [ ] (Mini Tasks اختيارية): `IProductQueries` في (Catalog)، و (Unique Index) على (السلة + المنتج)، و (Pagination) في `GET /api/orders`.

## الثغرات اللي لسه مفتوحة

| الثغرة | السيناريو | الحل |
| --- | --- | --- |
| العميل داس (Checkout) مرتين | حدثين بـ (Id) مختلف | (Idempotency Key) على مستوى الـ (API) |
| مفيش حدث `ProductRemoved` | المنتج الممسوح يفضل في السلة لحد الـ (Checkout) | حدث بنفس طريقة السعر لما يتعمل مسح المنتجات |
| `buyerId` جاي من الـ (URL) | أي حد يشوف طلبات أي حد | من الـ (JWT) في الموديول 10 |
| صفحة "طلباتي" بالصور | الـ (Composition) لسه ماتبناش | (BFF) في الموديول 9 |
| L1 في النسخ التانية | سعر قديم لحد 30 ثانية بعد التغيير | مقبول (الـ Checkout بيتحقق) |
| محدش بيبص على الـ (DLQ) | رسايل فاشلة ممكن تقعد أيام | (Alert) في الموديول 13 |

## المراجع

- [microservices.io: API Composition](https://microservices.io/patterns/data/api-composition.html) و [CQRS](https://microservices.io/patterns/data/cqrs.html)
- [Martin Fowler: What do you mean by "Event-Driven"?](https://martinfowler.com/articles/201701-event-driven.html)
- [Microsoft Learn: HybridCache](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid)
- [Microsoft Learn: EF Core Concurrency Conflicts](https://learn.microsoft.com/ef/core/saving/concurrency) و [ExecuteUpdate](https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete)
- [redis.io: Licenses](https://redis.io/legal/licenses/)
- Martin Kleppmann, *Designing Data-Intensive Applications* (الفصل 11: Stream Processing)
