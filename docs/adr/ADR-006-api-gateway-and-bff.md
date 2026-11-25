# ADR-006: البوابة (API Gateway) بـ YARP والـ (BFF)

## الحالة
مقبول · 2026-11-25

## السياق
الواجهة كانت هتحتاج تكلم 3 عناوين مختلفة (Catalog على 5299، و Basket على 5260، و Ordering على 5257). ده معناه:
- الواجهة عارفة تقسيمنا الداخلي، وأي تغيير فيه بيكسرها.
- كل خدمة مكشوفة لوحدها، والحاجات المشتركة ((Rate Limiting) و (CORS) و (JWT) بعدين) هتتكرر 3 مرات.
- لو شغّلنا أكتر من نسخة من خدمة، الواجهة مش هتعرف توزّع عليهم ولا تكتشف الواقعة.
- صفحة "طلباتي" محتاجة داتا من Ordering و Catalog مع بعض، فالواجهة هتعمل طلبين ورا بعض وتدمجهم بنفسها.

## القرار
1. **بوابة واحدة بـ (YARP)** (`Yarp.ReverseProxy` 2.3.0، رخصة MIT) في `src/ApiGateways/Gateway.API`، وهي الباب الوحيد للواجهة (`127.0.0.1:5100` على الجهاز، و 8080 جوه الشبكة).
2. **التوجيه بالبادئة:** `/catalog` و `/basket` و `/ordering` و `/bff`، مع `PathRemovePrefix`، فالخدمات مش بتتغير.
3. **قواعد السلوك في `appsettings.json`، والعناوين في `appsettings.Docker.json`** (`ASPNETCORE_ENVIRONMENT: Docker`). مفيش عناوين في متغيرات البيئة، لأنها بتغطي على ملف Docker.
4. **توزيع الحمل `RoundRobin`** و **فحص صحة (Active Health Check)** لكل (Cluster): كل 10 ثواني، ومهلة 5 ثواني، و `ConsecutiveFailures`. الخدمات بتتفحص على `/health/ready`، والـ BFF على `/health/live` (مالوش قاعدة بيانات). كل نسخة (Destination) منفصل باسم الكونتينر الكامل.
5. **صحة البوابة `live` بس**، ومش معتمدة على صحة الخدمات، عشان وقوع خدمة ميقفلش الباب على الباقي.
6. **مهلة (Timeout) لكل (Cluster)** بـ `ActivityTimeout`، أكبر من أبطأ عملية طبيعية: Catalog و Ordering = 10 ثواني، و Basket = 35 ثانية (الـ Checkout فيه Resilience لحد 30 ثانية)، و BFF = 15 ثانية (Ordering 10 + Catalog 3).
7. **تحديد عدد الطلبات** بالمكتبة اللي جوه ASP.NET (من غير باكدج): `GlobalLimiter` لكل IP (Fixed Window)، و `/health` مستثنى، وسياسة أشد `checkout` على `POST /basket/api/basket/checkout`. الرفض بـ **429** مع `Retry-After`.
8. **الـ (CORS) في البوابة بس**، بسياسة `frontend`، والـ (Origins) من الإعدادات (`Cors:AllowedOrigins`). الترتيب: `UseCors` ثم `UseRateLimiter` ثم `MapReverseProxy`.
9. **(BFF) منفصل للويب** (`src/ApiGateways/Web.Bff`)، للشاشات المركّبة بس. الطلبات البسيطة بتروح من البوابة للخدمة على طول. الـ BFF جوه الشبكة بس (من غير `ports`)، والبوابة بتوصله بـ `/bff`.
10. **صفحة "طلباتي"** (`GET /bff/my-orders`) بـ (API Composition): Ordering الأول، وبعدين Catalog بطلب (Batch) واحد (`GET /api/products?ids=`، مقسّم بـ `Chunk(100)`). Ordering فشل ← 503. Catalog فشل ← نفس الطلبات من غير تفاصيل المنتجات و `productDetailsAvailable: false` (Graceful Degradation). الاسم والسعر المدفوع من Ordering دايمًا.
11. **(Endpoint) عرض منفصل في Catalog** (`GET /api/products?ids=`) بدل ما نكبّر `/prices` اللي بيستخدمه الـ Checkout.

## اللي مش هنعمله (بقرار)
- **مفيش منطق بيزنس في البوابة ولا في الـ BFF.** الـ BFF بيجمّع ويشكّل بس.
- **الطلبات الداخلية بين الخدمات (East-West) مش بتعدّي على البوابة** (مثلًا Basket ← Catalog).
- **مفيش BFF للموبايل** دلوقتي. هنفصله لما الاحتياجات تختلف فعلًا.
- **مفيش (Rate Limiting) موزع** (عدّادات في Redis). كل نسخة بوابة بعدّاد لوحدها.
- **Swagger مش من البوابة.** بيفضل على بورت كل خدمة وقت التطوير.

## البدائل اللي اترفضت
| البديل | ليه اترفض |
|---|---|
| الواجهة تكلم الخدمات على طول | ربط بالتقسيم الداخلي، وتكرار الحاجات المشتركة، ومفيش توزيع حمل |
| Nginx أو Kong أو Envoy | قويين، بس أدوات منفصلة برا .NET بلغة إعدادات جديدة |
| Destination واحد باسم الخدمة (`ordering-api`) | الاتصال بيلزق في نسخة (شفنا 4/2/0)، ومفيش فحص لكل نسخة |
| فحص `ready` للبوابة معتمد على الخدمات | خدمة واحدة تقع تقفل الباب على الكل |
| الدمج في الواجهة | طلبين ورا بعض على نت ضعيف، وتكرار المنطق في كل واجهة |
| الدمج جوه البوابة نفسها | البوابة تكبر وتبقى مربوطة بالشاشات |
| (Read Model) في Ordering فيه الصور | تقيل على صفحة عرض، وفيه نسخ داتا ومزامنة |
| إضافة الصور لـ `/prices` | يربط الـ Checkout باحتياجات العرض |
| `AllowAnyOrigin` | بيلغي فايدة الـ (CORS) |
| رفض الـ Rate Limiting بـ 503 (الافتراضي) | معناها غلط، وبيعمل (Alerts) كاذبة |

## العواقب
- ✅ الواجهة بتعرف عنوان واحد، والخدمات ممكن تتقسم أو تزيد نسخها من غير ما الواجهة تتغير.
- ✅ النسخة الواقعة بتتشال لوحدها، وصفحة "طلباتي" بتفضل شغالة لو Catalog وقع.
- ⚠️ البوابة بقت (Single Point of Failure). الحل في الإنتاج: أكتر من نسخة ورا (Load Balancer)، لأنها (Stateless).
- ⚠️ خطوة زيادة في كل طلب، وخطوتين في طلبات الـ BFF.
- ⚠️ الـ 504 معناها "مش عارف"، والعملية ممكن تكون نجحت (مرتبطة بثغرة الـ Idempotency Key المفتوحة).
- ⚠️ في Docker كل طلبات الجهاز بتبان بـ IP واحد. لو حطينا (Load Balancer) قدام البوابة، محتاجين `X-Forwarded-For` بحذر. وفي الموديول 10 العدّاد هيبقى لكل مستخدم من الـ (JWT).
- ⚠️ الـ BFF بيكلم `ordering-api` باسم الخدمة، فمش بيوزّع بالتساوي على النسخ (مؤجل للموديول 13).
- ⚠️ البوابة والـ BFF لسه من غير Serilog و ProblemDetails من `ServiceDefaults` (محتاجة نسخة من غير `DbContext`).

## المراجع
- microservices.io: API Gateway و Backends for Frontends و API Composition
- Sam Newman: Building Microservices (2nd ed)، و "Backends For Frontends"
- Microsoft Learn: YARP (Routes و Clusters و Health Checks و Load Balancing و Timeouts)
- Microsoft Learn: Rate limiting middleware in ASP.NET Core، و Enable CORS in ASP.NET Core
- MDN: Cross-Origin Resource Sharing (CORS)
