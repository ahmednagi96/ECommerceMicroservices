# كورس الـ Microservices على مشروع ECommerceMicroservices: الخطة والتقدم

> آخر تحديث: 2026-11-28 · مبني على `docs/PROJECT_CONTEXT.md` وعلى الكود الفعلي في الريبو وملاحظات المذاكرة.
> الملف ده نسخته في الريبو هي `docs/microservices-course-roadmap.md` عشان Claude Code جوه Cursor يقرأه أول كل جلسة ويحدّثه في آخرها.

---

## 0. ابدأ من هنا (لأي محادثة جديدة)

1. اقرا الملف ده كله، وبعدين `docs/adr/ADR-007-identity-keycloak-jwt.md` (قرارات الموديول 10)، وملف ملاحظات آخر موديول: **"الموديول 10: الهوية (Keycloak و JWT)"** (Doc + نسخة في مستندات المشروع + Google Doc في فولدر `microservices` على Drive + `docs/notes/10-identity.md`).
2. الموديول 10 مقفول بالكامل. **ابدأ الموديول 11 (Saga) من الدرس 11.1.**
3. **مكتبة الرسائل المعتمدة: Wolverine** (ADR-003)، والـ Outbox/Inbox بـ Wolverine (ADR-004)، والبيانات عبر الخدمات والكاش (ADR-005)، والبوابة والـ BFF (ADR-006)، والهوية (ADR-007). أي كود جديد يتكتب بنفس الأسلوب.
4. التزم بـ **أسلوب الشرح** في القسم 2 بالحرف.
5. **اقرا الكود من الريبو العام** (`git clone https://github.com/ahmednagi96/ecommercemicroservices`) قبل أي درس. ⚠️ لو آخر Commits مش متعمّلها Push، اسأل المستخدم عن الكود الحالي.

---

## 1. إنت واقف فين دلوقتي (Snapshot)

**اللي خلصته فعلًا (✅):**
- تقسيم النظام لـ 3 خدمات: Catalog و Basket و Ordering، كل خدمة بطبقات Clean Architecture مبسطة (API / Application / Domain / Infrastructure).
- الـ (Database per Service): 3 قواعد SQL Server منفصلة، ومفيش Foreign Keys بين الخدمات.
- Docker: Dockerfile متعدد المراحل، و docker-compose، و YAML anchors (`x-sqlserver` و `x-api` و `x-api-env`)، و healthcheck، وحدود موارد، وشبكة bridge اسمها `ecommerce`، والأسرار عن طريق `env_file`.
- .NET 10، و (Central Package Management) في `Directory.Packages.props`، و `Directory.Build.props`.
- الاتصال المتزامن من Basket لـ Catalog بـ Typed HttpClient + `AddStandardResilienceHandler`.
- الموديول 4 ✅ (`BuildingBlocks/ServiceDefaults`) · الموديول 5 ✅ (Ordering بـ DDD) · الموديول 6 ✅ (RabbitMQ و Wolverine، ADR-003) · الموديول 7 ✅ (Outbox و Inbox، ADR-004) · الموديول 8 ✅ (ECST و Version و تحقق الـ Checkout و CQRS مستوى 1 و HybridCache + Redis، ADR-005) · الموديول 9 ✅ (YARP و Web.Bff، ADR-006) · **الموديول 10 ✅ (Keycloak و JWT، ADR-007)**.
- تفاصيل الموديولات 7 → 10 في ملفات الملاحظات (`docs/notes/07..10`) والـ ADRs. الـ Snapshot هنا للحالة الحالية بس.

**حالة الكود بعد الموديول 10:**
- **Keycloak** في compose: `quay.io/keycloak/keycloak:26.8.2` بـ `start-dev --import-realm`، على `127.0.0.1:8180`، و volume `keycloak-data`، و Health Check بـ `/dev/tcp` على بورت 9000، و `keycloak.env` (برا Git) + `keycloak.env.example`. `KC_HOSTNAME: http://localhost:8180` و `KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"`.
- **`keycloak/ecommerce-realm.json`** (Partial export + `users`): Realm `ecommerce`، وأدوار `customer` (افتراضي) و `admin`، و `ecommerce-web` (Public Client، Standard flow + Direct access grants للتطوير، Redirect `http://localhost:3000/*`)، و Mappers على `ecommerce-web-dedicated`: `ecommerce-api-audience` (Custom audience `ecommerce-api`) و `realm-roles-flat` (User Realm Role ← `roles`، Multivalued). Password policy Regex (حرف + رقم، 8+). المستخدمين: `ahmed` / `Ahmed1234` و `sara` / `Sara12345` (customer)، و `shop-admin` / `Admin1234` (customer + admin). Revoke Refresh Token مفعّل.
- **`src/BuildingBlocks/BuildingBlocks.Authentication`** (مشروع جديد، FrameworkReference + `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.15 في CPM)، و `BuildingBlocks` بيعمله ProjectReference: `AddJwtAuthentication()` (MetadataAddress و Audience من `Auth:*`، و `MapInboundClaims = false`، و `NameClaimType = preferred_username`، و `RoleClaimType = roles`، و `RequireHttpsMetadata` من `Auth:RequireHttpsMetadata` افتراضي true)، و `ClaimsPrincipalExtensions.GetRequiredUserId()` (= `sub`)، و `AppRoles` (`admin` و `customer`).
- **الإعدادات:** `x-api-env` فيه `Auth__MetadataAddress: http://keycloak:8080/realms/ecommerce/.well-known/openid-configuration` و `Auth__RequireHttpsMetadata: "false"`. كل مشروع فيه `Auth` في `appsettings.json` (localhost:8180 و `Audience: ecommerce-api`) و `RequireHttpsMetadata: false` في `appsettings.Development.json`. الـ BFF والبوابة: `Auth` في `appsettings.Docker.json`.
- **Ordering:** `UseAuthentication` قبل `UseAuthorization`. `GET /api/orders` = `GetMine` بـ `[Authorize]` والـ `sub` (من غير `buyerId`). `Confirm` بـ `[Authorize(Roles = AppRoles.Admin)]`. `Cancel` بـ `[Authorize]` + `ExecuteAsync(..., ownerOrAdmin: true)` و `CanAccess` (أدمن أو `order.BuyerId == sub`)، وغير كده `OrderNotFound` (404 متطابق).
- **Basket:** `[Authorize]` على `BasketController`، و `UserName` اتشال من `AddItemRequestDTO` و `CheckoutRequestDTO`، و `ShoppingCart.UserName` اتغيّر لـ `BuyerId` بـ Migration، والحدث `BuyerId: cart.BuyerId` (= `sub`).
- **Catalog:** `PUT /api/products/{id}/price` بـ `[Authorize(Roles = AppRoles.Admin)]` (على الـ Endpoint بس، الـ GET كلها مفتوحة وأهمها `/prices` اللي Basket بيكلّمه من غير توكن).
- **اتمسح:** `UsersController` وجداول `Users` في Basket و Catalog.
- **Web.Bff:** ProjectReference لـ `BuildingBlocks.Authentication`، و `AddJwtAuthentication` + `UseAuthentication/UseAuthorization`، و `/my-orders` بـ `.RequireAuthorization()` و `ClaimsPrincipal` (من غير `buyerId`)، و `ForwardAccessTokenHandler` (DelegatingHandler + `IHttpContextAccessor`) على `OrderingClient` بس، و `OrderingClient.GetOrdersAsync(ct)` على `api/orders`، و catch لـ 401 قبل الـ 503. الـ Dockerfile بينسخ المشروع الجديد.
- **Gateway.API:** ProjectReference + `AddJwtAuthentication`. الترتيب: `MapHealthChecks` ← `UseCors` ← `UseAuthentication` ← `UseRateLimiter` ← `UseAuthorization` ← `MapReverseProxy`. `ClientKey` = `user:<sub>` لو `IsAuthenticated` وإلا `ip:<ip>` في `GlobalLimiter` وسياسة `checkout`. `AuthorizationPolicy: default` على `basket-route` و `checkout-route` و `ordering-route` و `bff-route`، و `catalog-route` من غيرها.

**ثغرات معروفة لسه مفتوحة:**
- ⚠️ مفيش Idempotency Key على مستوى الـ API لو العميل داس Checkout مرتين.
- ⚠️ مفيش حدث `ProductRemoved`: المنتج الممسوح بيفضل في السلة لحد الـ Checkout.
- ⚠️ `GET /api/orders` من غير Pagination.
- ⚠️ مفيش Unique Index على (ShoppingCartId, ProductId) في Basket (Mini Task).
- ⚠️ مفيش Alert على الـ DLQ (الموديول 13).
- ⚠️ البوابة والـ BFF من غير Serilog و ProblemDetails من ServiceDefaults، ومفيش Passive Health Check، والـ Rate Limiting في الذاكرة.
- ⚠️ `Confirm` بالأدمن مؤقت لحد الـ Saga (الموديول 11).
- ⚠️ Keycloak نسخة واحدة `start-dev` و H2 (الإنتاج في الموديول 15)، ومفيش واجهة حقيقية بـ Authorization Code + PKCE.

**إنت في:** **المرحلة الرابعة** → خلصت **الموديول 10**، والجاي **الموديول 11 (Saga: `ReserveStock` كـ Command، بـ Wolverine، والتأكيد يبقى أوتوماتيك بدل الأدمن)**.

---

## 2. منهجية الكورس وأسلوب الشرح (مهم جدًا)

**أسلوب الشرح اللي المستخدم طالبه:**
- المستخدم **لسه بيتعلم**، وعنده خبرة سابقة في **Laravel** (Events و Listeners و Queues و `dispatch` و `middleware('auth')` و Socialite). قارن بيها لما تفيد.
- عربي مصري بسيط، والمصطلحات الإنجليزي بين قوسين: «الـ (Exchange)».
- **التنسيق مهم جدًا:** جمل قصيرة، ومتحطش كود أو مصطلحات إنجليزي كتير ورا بعض جوه جملة عربي واحدة. الأحسن خطوات مرقمة، والكود في بلوك لوحده. المستخدم بيفضّل الشرح البرمجي المباشر عن التشبيهات البعيدة. **الأسئلة اللي فيها أسماء إنجليزي كتير اكتبها على سطور قصيرة منفصلة.**
- **قسّم الدرس الكبير لأجزاء صغيرة**، جزء في كل رسالة، واستنى المستخدم يقول "يلا" قبل الجزء اللي بعده.
- **اشرح الكود حتة حتة "بالبلدي"**، وابدأ بأقل كود ممكن، وأجّل التفاصيل.
- قبل كود جديد، اربطه بحاجة المستخدم عملها بإيده أو اتعلمها قبل كده (في الموديول 10: ملف الـ Authentication بتاع الـ Monolith في مستندات المشروع كان مرجع ممتاز).
- لكل خطوة: **ليه بنعملها، وبتحل إيه**.
- الأسئلة السريعة أثناء المذاكرة: **رد قصير 2–4 سطور** على السؤال بس.
- صحّح إجابات المستخدم بلطف: ✅ الصح، و ⚠️ أو ❌ الغلط مع السبب.
- لما المستخدم يقول "مفهمتش" أو "تايه": اشرحها بجدول زمني (Timeline) خطوة بخطوة، أو جدول "مين بيعمل إيه وإمتى"، أو ابدأ من حاجة هو عارفها وغيّر حاجة واحدة كل خطوة (اشتغلت جدًا في 10.1). **ممنوع كلام عربي جوه Code Block وممنوع سلاسل أسهم ←، أي Timeline يبقى جدول Markdown.**
- لما المستخدم يحتار بين اختيارين، **قوله يختار إيه بوضوح** في سطر واحد.
- **اقرا الكود الحقيقي في الريبو** قبل ما تكتب الدرس، ومتفترضش حاجة عن المشروع. وأي حاجة مش متأكد إنها اتعملت، اسأل.
- لما خدمة تقع أو نتيجة غريبة: **اطلب اللوج الأول** (`docker compose ps` و `docker compose logs <svc> --tail 50`). في البوابة `grep "Proxying to"`، وفي 401 غريب `grep -i "bearer\|IDX"`.
- تحقق من إصدارات وتراخيص المكتبات بالبحث قبل ما تذكرها.
- المستخدم بيفضّل **أدوات مجانية/مفتوحة المصدر** تنفع في الشغل (عشان كده مفيش MassTransit ولا Duende).
- المستخدم شغال على Linux (Ubuntu، Bash).

**شكل كل درس:** المشكلة → الفكرة + رسمة/تشبيه → الكود في المشروع → المقايضات → إمتى ما تستخدمهاش → تجربة عملية → سؤال مراجعة + سؤال إنترفيو.

**تعريف الإنجاز لكل موديول:** كود شغال بـ `docker compose up`، وملف ملاحظات (الدروس + كل أسئلة المستخدم وإجاباتها + المشاكل اللي قابلها وحلولها + ملخص + اختبر نفسك)، و ADR. ملف الملاحظات بيتعمل Doc ونسخة Google Doc في فولدر `microservices` على Drive ونسخة في مستندات المشروع وملف md للريبو.

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
**6.** أساسيات الرسائل: RabbitMQ و Wolverine ✅ · **7.** الرسائل الموثوقة (Outbox / Inbox) ✅ · **8.** البيانات عبر الخدمات والكاش ✅

### المرحلة الرابعة: الحافة والأمان وسير العمل
**9. ✅ YARP Gateway و BFF** (9.1 → 9.5).

**10. ✅ الهوية: Keycloak و JWT**
- 10.1 ✅ ليه الـ Auth القديم ميمشيش: الـ Secret المشترك (HS256)، ومزود هوية واحد، و RS256 (مفتاح خاص ومفتاح عام)، و Discovery و JWKS و `kid`، وليه Keycloak (Apache 2.0) مش Duende ولا OpenIddict.
- 10.2 ✅ Keycloak في Docker (start-dev، Health Check على 9000)، و Realm و Client (Public) و Audience Mapper و Roles و Users، وأول توكن بـ `curl` وفكّه، و Refresh و Revoke Refresh Token، والـ Realm كملف (`--import-realm`).
- 10.3 ✅ مشكلة الـ `iss` و `KC_HOSTNAME` + Backchannel، و Ordering بـ `[Authorize]` و `sub`، و `BuildingBlocks.Authentication`، و Basket من التوكن، و `UserName` ← `BuyerId`، ومسح `UsersController`، والرسائل الداخلية من غير توكن.
- 10.4 ✅ الـ BFF و Token Relay (DelegatingHandler، Catalog من غير توكن)، و `RequireHttpsMetadata` من الإعدادات، والبوابة تتأكد وتعدّ لكل مستخدم (`ClientKey`)، و `AuthorizationPolicy: default`، و Defense in Depth.
- 10.5 ✅ الأدوار (`realm_access.roles` ← Mapper `roles` + `RoleClaimType`)، و `AppRoles`، و `PUT /price` و `Confirm` للأدمن، و `Cancel` (Resource-based، IDOR، 404).
- 10.6 ✅ مراجعة واختبار 7 أسئلة (3 كاملين و 2 نص)، و ADR-007، وملف الملاحظات.

**11.** Saga (`ReserveStock` كـ Command، وبـ Wolverine) · **12.** gRPC.

### المرحلة الخامسة: التشغيل والجودة
**13.** Observability و Aspire (ومنها Alert على الـ DLQ) · **14.** Testing · **15.** CI/CD و K8s (ومنها `TypeLoadMode.Static` لـ Wolverine، و Keycloak للإنتاج: `start` + Postgres + HTTPS + أكتر من نسخة) · **16.** Advanced و Capstone (ومنها Database Read Replicas).

---

## 4. المراجع العالمية المعتمدة
| النوع | المرجع | بنستخدمه في |
|---|---|---|
| كتاب | Sam Newman — Building Microservices (2nd ed) | الحدود، والتواصل، والنشر، والبوابة و BFF، والأمان |
| كتاب + موقع | Chris Richardson — Microservices Patterns و microservices.io | الأنماط كلها |
| رسمي Microsoft | .NET Microservices Architecture eBook، وريبو `dotnet/eShop` | التطبيق على .NET |
| رسمي Microsoft | توثيق YARP، و JWT Bearer و Authorization في ASP.NET Core | الموديول 9 و 10 |
| رسمي | توثيق Keycloak (keycloak.org/server) | الموديول 10 |
| معيار | OWASP API Security Top 10 | الموديول 10 (IDOR) |
| كتاب | Vlad Khononov — Learning DDD | الموديول 5 |
| كتاب | Martin Kleppmann — DDIA | البيانات والاتساق (الموديول 7 و 8) |
| مقال | Martin Fowler — What do you mean by "Event-Driven"? | ECST (الموديول 8) |
| كتاب | Hohpe & Woolf — Enterprise Integration Patterns | الرسائل |
| رسمي | RabbitMQ docs، وتوثيق Wolverine | الموديول 6 و 7 و 8 |
| كتاب | Google — Site Reliability Engineering | المراقبة |

---

## 5. متتبع التقدم (Progress Tracker)
| # | الموديول | الحالة | ملف الملاحظات | الـ ADR |
|---|---|---|---|---|
| 0–3 | الأساسيات | ✅ | — | — |
| 4 | Chassis و Production Baseline | ✅ | `docs/notes/04-service-baseline.md` | ADR-001 |
| 5 | Ordering DDD | ✅ | `docs/notes/05-ordering-ddd.md` | ADR-002 |
| 6 | RabbitMQ و Wolverine | ✅ | `docs/notes/06-messaging-rabbitmq.md` | ADR-003 |
| 7 | Outbox / Inbox | ✅ | `docs/notes/07-reliable-messaging.md` | ADR-004 |
| 8 | البيانات عبر الخدمات والكاش | ✅ | `docs/notes/08-data-across-services.md` | ADR-005 |
| 9 | YARP Gateway و BFF | ✅ | `docs/notes/09-api-gateway-bff.md` | ADR-006 |
| 10 | الهوية: Keycloak و JWT | ✅ | `docs/notes/10-identity.md` + Doc + Google Doc | ADR-007: Keycloak + RS256 + sub + Token Relay + Roles Mapper + IDOR |
| 11 | Saga | ⏭️ الجاي | | |
| 12 | gRPC | ⬜ | | |
| 13 | Observability و Aspire | ⬜ | | |
| 14 | Testing | ⬜ | | |
| 15 | CI/CD و K8s | ⬜ | | |
| 16 | Advanced و Capstone | ⬜ | | |

**سجل الجلسات:**
- 2026-11-12 → 2026-11-25 · الموديولات 4 → 9 (التفاصيل في ملفات الملاحظات).
- 2026-11-25 · 10.1: الـ Secret المشترك، و RS256، و Discovery و JWKS (اتشرح من الـ Monolith بتغيير حاجة واحدة كل خطوة).
- 2026-11-26 · 10.2 كله (Keycloak في Docker، و Realm و Client و Mappers والمستخدمين، وأول توكن، والـ Realm كملف). مشاكل: الملف اتركّب فولدر (اسم الملف غلط) وبعدين جوه الـ volume، و Restart loop بسبب باسوردات `123456789` ضد الـ Password policy. 10.3 الجزء 1 (`KC_HOSTNAME` + Backchannel) و 2 (Ordering).
- 2026-11-27 · 10.3 الجزء 3 (Basket و `BuildingBlocks.Authentication`، و `UserName` ← `BuyerId` بـ Migration، ومسح `UsersController` وجداول `Users`). 10.4 كله (BFF Token Relay، والبوابة تتأكد وتعدّ لكل مستخدم). 10.5 الجزء 1 (Roles Mapper).
- 2026-11-28 · 10.5 الجزء 2 (Confirm/Cancel و IDOR)، و 10.6: اختبار 7 أسئلة (أخطاء: 401 لما `UseAuthorization` قبل `UseAuthentication`، و `buyerId` من الـ Query في Endpoint جديد)، و ADR-007، وملف الملاحظات (Doc + Google Doc في `microservices` + نسخة في المشروع + `docs/notes/10-identity.md`). الموديول 10 اتقفل.
- **اللي جاي:** الموديول 11.1 (Saga).

**قاعدة التحديث:** في آخر كل جلسة نغيّر الحالة، ونكتب سطر "إيه اللي اتعمل" وسطر "إيه اللي جاي".
