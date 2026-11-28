# ADR-007: الهوية بـ (Keycloak) و (JWT)

## الحالة
مقبول · 2026-11-28

## السياق
بعد الموديول 9، النظام كان مفيهوش أي هوية:
- `GET /api/orders?buyerId=...` في Ordering بيرجّع طلبات أي حد لأي حد.
- Basket بياخد `UserName` من جسم الطلب في الإضافة والـ (Checkout)، فأي حد يلمس سلة غيره.
- `PUT /api/products/{id}/price` في Catalog و `Confirm` و `Cancel` في Ordering مفتوحين لأي حد.
- `UsersController` في Basket و Catalog بيرجّع جداول `Users` (فيها عمود `Password`) من غير أي حماية.
- الـ (Rate Limiting) في البوابة لكل IP بس، فالناس اللي ورا نفس الراوتر بيتقاسموا عدّاد واحد.

ونقل `AuthService` بتاع الـ (Monolith) زي ما هو مش حل: نسخه في كل خدمة = 3 جداول مستخدمين، وحطه في البوابة = منطق وقاعدة بيانات فيها، وتوقيع (HS256) بـ `Secret` مشترك = أي خدمة تتخترق تقدر تطبع توكنات أدمن.

## القرار
1. **(Keycloak) هو مزود الهوية الوحيد** (`quay.io/keycloak/keycloak:26.8.2`، رخصة Apache 2.0، مشروع في CNCF). بيعمل الدخول والتسجيل والباسوردات والتجديد، وبيوقّع التوكنات بـ **RS256**. الخدمات معاها المفتاح العام بس، وبتجيبه من (JWKS) عن طريق الدليل (Discovery) وتحفظه في الذاكرة.
2. **التشغيل في التطوير:** `start-dev --import-realm` على `127.0.0.1:8180`، و volume `keycloak-data` للداتا (H2)، و Health Check على بورت الإدارة 9000 بـ `/dev/tcp` (الصورة مفيهاش `curl`)، وبيانات الأدمن المؤقت في `keycloak.env` (برا Git).
3. **`ecommerce` Realm** (مش `master`)، و **`ecommerce-web` (Public Client)** من غير سر، بـ Standard flow و Valid redirect URIs و Web origins لـ `http://localhost:3000`. **Direct access grants للتطوير بس** (التجارب بـ `curl`).
4. **(Audience) واحد `ecommerce-api`** لكل الـ API، بـ (Audience Mapper) على `ecommerce-web-dedicated`.
5. **الـ (Realm) كملف في الريبو:** `keycloak/ecommerce-realm.json` (Partial export + مصفوفة `users` بإيدنا) ومتركّب في `/opt/keycloak/data/import`. مستخدمين التجربة (`ahmed` و `sara` و `shop-admin`) بباسوردات تجربة **للتطوير بس**. الاستيراد بيحصل لو الـ (Realm) مش موجود بس.
6. **`issuer` واحد:** `KC_HOSTNAME: http://localhost:8180` و `KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"`. الـ `issuer` دايمًا `localhost:8180`، و `jwks_uri` للخدمات بـ `keycloak:8080`.
7. **مشروع `BuildingBlocks.Authentication`** (FrameworkReference + `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.15، MIT)، و `BuildingBlocks` بيعمله Reference:
   - `AddJwtAuthentication()`: `MetadataAddress` و `Audience` من الإعدادات، و `MapInboundClaims = false`، و `NameClaimType = "preferred_username"`، و `RoleClaimType = "roles"`، و `RequireHttpsMetadata` من `Auth:RequireHttpsMetadata` (الافتراضي `true`).
   - `GetRequiredUserId()` بيرجّع `sub`، وبيرمي (Exception) لو ناقص (يعني نسينا `[Authorize]`).
   - `AppRoles` (`admin` و `customer`) بدل النصوص المتكررة.
8. **هوية المستخدم = `sub` من التوكن بس.** `GET /api/orders` من غير `buyerId`. `UserName` اتشال من DTOs بتاعة Basket، والعمود اتغيّر اسمه لـ `BuyerId` بـ (Migration). `UsersController` وجداول `Users` اتمسحوا.
9. **البوابة بتتأكد من التوكن:** الترتيب `UseCors` ثم `UseAuthentication` ثم `UseRateLimiter` ثم `UseAuthorization` ثم `MapReverseProxy`. `AuthorizationPolicy: default` على `basket-route` و `checkout-route` و `ordering-route` و `bff-route`، و `catalog-route` مفتوح. العدّاد بـ `user:<sub>` للمستخدم اللي توكنه اتأكد منه، و `ip:<ip>` للضيف.
10. **الخدمات بتتأكد كمان** (Defense in Depth)، حتى لو البوابة بتقفل الـ Route.
11. **الـ (BFF) بيعمل (Token Relay):** `.RequireAuthorization()` على `/my-orders`، و `ForwardAccessTokenHandler` (DelegatingHandler) على `OrderingClient` بس. Catalog بيتطلب من غير توكن. 401 من Ordering بيرجع 401 مش 503.
12. **الأدوار:** (Mapper) نوعه User Realm Role بيكتب `roles` في أول مستوى في التوكن. `PUT /price` في Catalog و `Confirm` في Ordering عليهم `[Authorize(Roles = AppRoles.Admin)]` (على الـ Endpoint مش الكلاس، عشان `GET /prices` الداخلي).
13. **(Resource-based Authorization) في `Cancel`:** صاحب الطلب (`order.BuyerId == sub`) أو الأدمن. غير كده **404** بنفس رد الطلب اللي مش موجود (`OrderNotFound`).
14. **الرسائل الداخلية (RabbitMQ) مفيهاش توكنات.** `BuyerId` في `BasketCheckedOutIntegrationEvent` داتا عادية، والثقة في الخدمة اللي نشرتها (اتأكدت من التوكن)، والحماية على الـ (Broker) نفسه.
15. **عمر توكن الدخول 5 دقايق** (افتراضي Keycloak) بدل الـ (Introspection) في كل طلب.

## اللي مش هنعمله (بقرار)
- **مفيش (Audience) لكل خدمة ولا (Token Exchange)** دلوقتي.
- **مفيش (Introspection)** ولا قايمة توكنات ملغية: سحب الصلاحية بيوصل مع التوكن الجاي.
- **مفيش واجهة حقيقية (Authorization Code + PKCE)** دلوقتي. التجارب بـ Direct access grants.
- **مفيش (Client Credentials)** للـ BFF: مفيش شغل من غير مستخدم لحد دلوقتي.
- **(Keycloak) مش ورا البوابة.** ليه صفحاته وعنوانه هو (في الإنتاج دومين زي `auth.shop.com`).

## البدائل اللي اترفضت
| البديل | ليه اترفض |
|---|---|
| نسخ `AuthService` في كل خدمة | 3 جداول مستخدمين وتكرار كل منطق الأمان |
| الـ Auth في البوابة | منطق بيزنس وقاعدة بيانات في البوابة |
| HS256 بـ `Secret` مشترك | أي خدمة تتخترق تطبع توكنات أدمن |
| Duende IdentityServer | محتاج رخصة مدفوعة في الإنتاج (نفس سبب رفض MassTransit) |
| OpenIddict | مجاني، بس مكتبة: صفحات الدخول والتسجيل وكل حاجة بإيدنا |
| `buyerId` أو `UserName` من الطلب | أي حد يدّعي إنه أي حد، حتى مع `[Authorize]` |
| `preferred_username` كهوية | اسم المستخدم ممكن يتغيّر، و `sub` لأ |
| `sub` من التوكن من غير التأكد منه في البوابة | المهاجم يغيّر `sub` في كل طلب وياخد عدّاد جديد |
| `[Authorize]` على `ProductsController` كله | بيكسر `GET /prices` اللي Basket بيكلّمه من جوه من غير توكن |
| (Claims Transformation) للأدوار في .NET | كود في كل خدمة، والـ (Mapper) بيخلي التوكن واضح لأي لغة |
| 403 لـ `sara` في `Cancel` | بيكشف إن الطلب موجود |
| توكن المستخدم جوه الـ Event | بيخلص قبل الـ Retry والـ Replay، وبيتخزن في الـ Outbox والـ Inbox و RabbitMQ |

## العواقب
- ✅ كل الثغرات اللي في السياق اتقفلت، و"طلباتي" بقت بتعرف المستخدم من التوكن.
- ✅ مفيش أي سر في إعدادات الخدمات، واختراق خدمة مش بيدّي المخترق قدرة يطبع توكنات.
- ✅ الخدمات مش بتكلّم (Keycloak) في كل طلب، وتغيير المفاتيح (Key Rotation) من غير تعديل أي خدمة.
- ✅ العدّاد في البوابة عادل لكل مستخدم، والطلب من غير توكن بيترفض من الباب.
- ⚠️ (Keycloak) بقى اعتمادية مهمة: لو وقع، الدخول والتجديد بيقفوا، والناس بتطلع خلال 5 دقايق، وأي خدمة تعمل Restart مش هتلاقي المفاتيح. الحل في الإنتاج: أكتر من نسخة + Postgres (الموديول 15).
- ⚠️ سحب دور (زي `admin`) بياخد لحد 5 دقايق عشان يتطبق.
- ⚠️ أي تعديل في لوحة التحكم لازم يتصدّر تاني للملف (مع نسخ `users` بإيدنا)، وإلا هيضيع لو الـ volume اتمسح.
- ⚠️ التأكد من التوكن بيحصل مرتين (البوابة والخدمة)، بقصد.
- ⚠️ مستخدمين وباسوردات تجربة في Git: مقبول للتطوير بس.

## المراجع
- Keycloak docs: Running in a container، و Configuring the hostname (v2)، و Importing and exporting realms، و Health checks
- Microsoft Learn: JWT Bearer authentication، و Role-based و Resource-based authorization، و YARP Authentication and Authorization
- OWASP API Security Top 10: API1 Broken Object Level Authorization
- Sam Newman: Building Microservices (2nd ed)، فصل Security
