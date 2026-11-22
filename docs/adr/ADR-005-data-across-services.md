# ADR-005: البيانات عبر الخدمات والكاش

## الحالة
مقبول · 2026-11-22

## السياق
(Basket) كان بياخد السعر من (Catalog) وقت الإضافة للسلة بس، وبيحفظ نسخة منه. لو السعر اتغير بعدها، الطلب كان بيتعمل بالسعر القديم. وكمان (Ordering) مكانش فيه أي (Endpoint) للقراءة، و (Catalog) بيقرا من الداتابيز في كل طلب.

## القرار
1. **(Catalog) بيبعت `ProductPriceChangedIntegrationEvent(ProductId, NewPrice, Version)`** (Event-Carried State Transfer)، على (Exchange) اسمه `product-price-changed`، من خلال (Wolverine Outbox) زي (Basket).
2. **`Product.PriceVersion` بيزيد مع كل تغيير سعر**، ومحمي بـ `IsConcurrencyToken()`. التعارض ← 409. والتغيير بيحصل من `Product.ChangePrice` بس.
3. **(Basket) بيحتفظ بـ `Price` و `PriceVersion` في `ShoppingCartItem`**، والـ `ProductPriceChangedHandler` بيعمل `ExecuteUpdateAsync` بشرط `PriceVersion < event.Version` (Naturally Idempotent)، مع `UseDurableInbox()`. وأي كود بيكتب السعر (`AddItemToBasket`) بيحترم الـ (Version).
4. **التحقق النهائي وقت الـ (Checkout) متزامن (Sync)** بـ `GET /api/products/prices?ids=...` (Batch، حد 100). اختلاف ← تصليح السلة + 409 بالتفاصيل. (Catalog) واقع ← 503 ومفيش طلب.
5. **(CQRS) مستوى 1 في (Ordering):** `IOrderQueries` + `GET /api/orders?buyerId=`، بـ (Projection) لـ (DTO) من غير الـ (Aggregate).
6. **الـ (API Composition)** لصفحة "طلباتي" بالصور مكانها (BFF) في الموديول 9، مع (Graceful Degradation) للداتا الاختيارية.
7. **الكاش:** (HybridCache) (L1 = 30 ثانية، L2 = Redis 10 دقايق) لـ `GET /api/products/{id}` بس، و `RemoveAsync` بعد حفظ تغيير السعر. `redis:8-alpine` من غير (Persistence)، و `maxmemory 256mb` و `allkeys-lru`، ومفاتيح بـ `catalog:`، ولـ (Catalog) بس.

## اللي مش هنعمله (بقرار)
- **مش هنكاش** `GET /api/products/prices`: ده التحقق من الحقيقة.
- **السلة مش هتتنقل لـ (Redis)** دلوقتي: الـ (Checkout) محتاج (Outbox) في نفس (Transaction) الـ (SQL).
- **(Redis) مش مشترك بين الخدمات.**

## البدائل اللي اترفضت
| البديل | ليه اترفض |
|---|---|
| (Thin Event) و (Basket) يسأل (Catalog) | يرجّع الربط بين الخدمتين ويضغط على (Catalog) |
| (Basket) يسأل (Catalog) في كل عرض للسلة | السلة تقع لو (Catalog) واقع، وحمل كبير |
| الاعتماد على الأحداث بس من غير تحقق | الحدث ممكن يكون لسه في الـ (Outbox)، وثغرة وقت الإضافة |
| التحقق إن الـ (Inbox) فاضي وقت الـ (Checkout) | الـ (Inbox) بيشوف اللي وصله بس |
| `OccurredOn` بدل `Version` | ساعات السيرفرات بتختلف |
| (Load + foreach + SaveChanges) في الـ (Handler) | بطيء، وفيه فخ "شيّك وبعدين احفظ" |
| (Redis) مشترك بين الخدمات | داتابيز مشتركة متنكرة |
| الكاش في (Resource Filter) | توفير صغير، والمسح أصعب |
| (Valkey) | مقبول تمامًا (BSD)، اخترنا (Redis 8 / AGPLv3) لأنه الأشهر، والتحويل = اسم الصورة |

## العواقب
- ✅ مفيش طلب بسعر قديم، والسلة بتحدّث نفسها.
- ⚠️ الـ (Checkout) بقى يعتمد على (Catalog) (Availability أقل).
- ⚠️ (Catalog) بقى محتاج (RabbitMQ) و (Redis) وقت التشغيل.
- ⚠️ ثغرة أجزاء من الثانية وقت الإضافة، والتحقق الأخير بيلقطها.
- ⚠️ النسخ التانية من (Catalog) ممكن تفضل بالسعر القديم في L1 لحد 30 ثانية.

## المراجع
- microservices.io: API Composition و CQRS
- Martin Fowler: What do you mean by "Event-Driven"?
- Microsoft Learn: HybridCache و EF Core Concurrency و ExecuteUpdate
- redis.io/legal/licenses
