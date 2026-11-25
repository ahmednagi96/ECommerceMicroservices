# الموديول 9: البوابة (YARP Gateway) والـ BFF

Nov 25, 2026 · @Ahmed Nagi

## نظرة عامة

الموديول 9 حط **باب واحد** قدام النظام: بوابة بـ (YARP) على `localhost:5100`، و (BFF) للويب بيجمّع صفحة "طلباتي" من Ordering و Catalog في رد واحد.

مكانه في الكورس: **المرحلة الرابعة** (الحافة والأمان)، بعد الموديول 8 (البيانات عبر الخدمات والكاش)، وقبل الموديول 10 (Identity و JWT).

> رسمة شكل النظام موجودة في نسخة الـ Doc: المتصفح ← البوابة (CORS ثم Rate Limit ثم Route ثم Load Balance ثم Timeout) ← Catalog و Basket و Ordering (3 نسخ) و Web.Bff، والـ BFF بيكلم Ordering و Catalog جوه الشبكة.

المتصفح بيكلم البوابة بس. البوابة بتوصّل للخدمات أو للـ BFF، والـ BFF بيكلم Ordering و Catalog جوه الشبكة مباشرة.

### خريطة الدروس

| الدرس | الموضوع | الناتج |
| --- | --- | --- |
| 9.1 | ليه محتاجين بوابة؟ | المشكلة، والمسؤوليات، والممنوعات، والمقايضات |
| 9.2 | أول بوابة بـ YARP | مشروع `Gateway.API` شغال في Docker، و RoundRobin، و Health Checks على 3 نسخ |
| 9.3 | شغل البوابة المشترك | (Timeout)، و (Rate Limiting)، و (CORS) |
| 9.4 | الـ (BFF) وصفحة "طلباتي" | مشروع `Web.Bff`، و (API Composition)، و (Graceful Degradation) |
| 9.5 | القفلة | المراجعة، و ADR-006، والملف ده |

### الشكل النهائي للكود

| المكوّن | المكان | البورت على الجهاز | ملاحظات |
| --- | --- | --- | --- |
| البوابة | `src/ApiGateways/Gateway.API` | `127.0.0.1:5100` | `Yarp.ReverseProxy` 2.3.0 (MIT) |
| الـ BFF | `src/ApiGateways/Web.Bff` | مفيش (جوه الشبكة بس) | Minimal API + Typed HttpClients |
| Catalog | `src/Services/Catalog` | `5299` | Endpoint جديد: `GET /api/products?ids=` |

### المسارات (Routes) في البوابة

| المسار من برا | الـ Cluster | سياسات على الـ Route |
| --- | --- | --- |
| `/catalog/**` | `catalog-cluster` | `CorsPolicy: frontend` |
| `/basket/**` | `basket-cluster` | `CorsPolicy: frontend` |
| `POST /basket/api/basket/checkout` | `basket-cluster` | `CorsPolicy: frontend` + `RateLimiterPolicy: checkout` |
| `/ordering/**` | `ordering-cluster` | `CorsPolicy: frontend` |
| `/bff/**` | `bff-cluster` | `CorsPolicy: frontend` |

كل الـ Routes بتشيل البادئة بـ `PathRemovePrefix`، وكلها عليها الحد العام (`GlobalLimiter`) من غير ما يتكتب فيها.

### الـ Clusters

| الـ Cluster | المهلة (`ActivityTimeout`) | مسار الفحص | التوزيع |
| --- | --- | --- | --- |
| `catalog-cluster` | 10 ثواني | `/health/ready` | RoundRobin |
| `basket-cluster` | 35 ثانية | `/health/ready` | RoundRobin |
| `ordering-cluster` | 10 ثواني | `/health/ready` | RoundRobin |
| `bff-cluster` | 15 ثانية | `/health/live` | نسخة واحدة |

## الدرس 9.1: ليه محتاجين بوابة؟

البوابة (API Gateway) هي خدمة واحدة بتقف قدام كل الخدمات، بتاخد الطلب وتبعته للخدمة الصح، وبتعمل الحاجات المشتركة مرة واحدة.

### المشكلة: الواجهة من غير بوابة

الواجهة كانت هتكلم 3 عناوين: Catalog على 5299، و Basket على 5260، و Ordering على 5257. ده بيعمل 4 مشاكل:

1. **الواجهة عارفة مكان كل خدمة.** لو نقلنا أو قسمنا خدمة، الواجهة تتكسر.
2. **كل خدمة مكشوفة للعالم.** 3 أبواب بدل باب واحد.
3. **الحاجات المشتركة بتتكرر 3 مرات**، زي (JWT) و (Rate Limiting) و (CORS).
4. **الصفحات المركّبة** زي "طلباتي" بتعمل كذا طلب من الموبايل.

ولو شغّلنا أكتر من نسخة من خدمة، الواجهة مش هتعرف توزّع بالعدل، ولا تكتشف النسخة الواقعة، ولا تزوّد نسخة من غير ما تتعدّل.

**المقارنة بـ Laravel:** في Laravel كل المسارات كانت في `routes/api.php`، والـ (Middleware) زي `auth` بيتحط مرة واحدة. لما قسمنا لخدمات خسرنا نقطة الدخول الواحدة، والبوابة بترجّعهالنا.

### الفكرة

أبسط شكل للبوابة هو وكيل عكسي (Reverse Proxy):

1. بتستقبل الطلب من الواجهة.
2. بتبص على المسار.
3. بتبعته للخدمة الصح جوه شبكة Docker باسمها.
4. بترجّع الرد زي ما هو.

### البوابة بتعمل إيه

| المسؤولية | يعني إيه | اتعملت في |
| --- | --- | --- |
| التوجيه (Routing) | `/catalog/...` يروح لـ Catalog | 9.2 |
| توزيع الحمل (Load Balancing) | يوزّع على النسخ | 9.2 |
| فحص النسخ (Health Checks) | يبطّل يبعت للنسخة الواقعة | 9.2 |
| المهلة (Timeout) | ميستناش خدمة بطيئة للأبد | 9.3 |
| تحديد عدد الطلبات (Rate Limiting) | حد لكل عميل | 9.3 |
| الـ (CORS) | يسمح لواجهتنا تكلمنا من المتصفح | 9.3 |
| التحقق من التوكن (JWT) | يرفض الطلب من غير تسجيل دخول | الموديول 10 |

### الممنوع في البوابة

- **منطق بيزنس**، زي حساب سعر أو قرار إلغاء طلب.
- **قاعدة بيانات.** البوابة مالهاش قاعدة.
- **كود خاص بخدمة واحدة.**

لو البوابة بقى فيها منطق، أي تعديل في أي خدمة هيحتاج تعديل فيها، ونرجع لـ (Monolith) متوزع.

### مين بيعدّي على البوابة؟

| النوع | مثال | بيعدّي على البوابة؟ |
| --- | --- | --- |
| من برا لجوه (North-South) | المتصفح بيطلب المنتجات | ✅ أيوه |
| من خدمة لخدمة (East-West) | Basket بيسأل Catalog عن السعر | ❌ لأ |

الطلب الداخلي مش محتاج (Rate Limiting) ولا (CORS)، ولو عدّى على البوابة هيزوّد خطوة وضغط من غير فايدة.

### المقايضات

| الميزة | التمن |
| --- | --- |
| باب واحد بدل 3 | لو البوابة وقعت، النظام كله وقع (Single Point of Failure) |
| الحاجات المشتركة مرة واحدة | خطوة زيادة في كل طلب (كام ملّي ثانية) |
| الواجهة مش عارفة التقسيم الداخلي | كل مسار جديد محتاج تعديل في إعدادات البوابة |

حل الـ (Single Point of Failure): أكتر من نسخة من البوابة ورا (Load Balancer)، وده سهل لأنها مش بتحفظ حالة (Stateless).

### إمتى ما تستخدمهاش

- عندك خدمة واحدة بس.
- للطلبات الداخلية بين الخدمات.
- لو هتحط فيها منطق بيزنس.

### ليه YARP؟

من Microsoft، مفتوح المصدر برخصة (MIT)، ومكتبة جوه مشروع ASP.NET عادي، فنفس `Program.cs` والـ (Middleware) اللي إنت عارفهم. البدايل زي (Nginx) و (Kong) و (Envoy) أدوات منفصلة برا .NET.

## الدرس 9.2: أول بوابة بـ YARP

البوابة مشروع ASP.NET صغير بيقرا قواعد التوجيه من الإعدادات، وبتوزّع بالدور على النسخ السليمة بس.

### المشروع

```bash
dotnet new web -n Gateway.API -o src/ApiGateways/Gateway.API
dotnet sln add src/ApiGateways/Gateway.API/Gateway.API.csproj
```

- قالب `web` مش `webapi`، لأن البوابة مفيهاش (Controllers) ولا Swagger.
- الـ csproj فيه `PackageReference Include="Yarp.ReverseProxy"` بس، والإصدار `2.3.0` في `Directory.Packages.props`. الـ `TargetFramework` جاي من `Directory.Build.props`.

### الكود الأساسي

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health/live");
app.MapReverseProxy();

app.Run();
```

الإعدادات في JSON مش في الكود، عشان نغيّر العناوين حسب البيئة من غير ما نلمس الكود. والبوابة مش بتستخدم `AddServiceDefaults` لأنها محتاجة `DbContext`.

### مصطلحات YARP

| المصطلح | معناه | المقابل في Laravel |
| --- | --- | --- |
| Route | "لو المسار شكله كده..." | سطر في `routes/api.php` |
| Cluster | "...ابعته للمجموعة دي" | — |
| Destination | عنوان نسخة واحدة جوه المجموعة | — |

```json
"ordering-route": {
  "ClusterId": "ordering-cluster",
  "Match": { "Path": "/ordering/{**catch-all}" },
  "Transforms": [ { "PathRemovePrefix": "/ordering" } ]
}
```

- `{**catch-all}` = أي حاجة بعد البادئة، حتى لو فيها `/`.
- `PathRemovePrefix` بيشيل `/ordering` قبل الإرسال، فالخدمة مش بتتغير. والـ (Query String) بيعدّي لوحده.

### الطلب بيمشي إزاي جوه البوابة

| الخطوة | اللي بيحصل |
| --- | --- |
| 1 | طلب `GET /ordering/api/orders?buyerId=ahmed` وصل للبوابة. |
| 2 | البوابة لقت `ordering-route` مطابق. |
| 3 | الـ (Transform) شال `/ordering`، فبقى `/api/orders?buyerId=ahmed`. |
| 4 | الـ Cluster اختار نسخة سليمة بالدور. |
| 5 | الرد رجع للواجهة زي ما هو. |

### البوابة جوه Docker

جوه الكونتينر، `localhost` معناها الكونتينر نفسه. عشان كده فيه ملفين إعدادات:

| الملف | فيه إيه | بيتقري إمتى |
| --- | --- | --- |
| `launchSettings.json` | بورت البوابة (5000) | `dotnet run` بس |
| `appsettings.json` | الـ Routes، وقواعد السلوك، وعناوين `localhost` | دايمًا، الأول |
| `appsettings.Docker.json` | عناوين Docker بالأسماء | لما `ASPNETCORE_ENVIRONMENT=Docker`، وبيغطي على اللي فوقه |
| `Dockerfile` (`EXPOSE 8080`) | ملحوظة توضيحية | مش بيفتح أي بورت |
| `docker-compose.yml` | `ASPNETCORE_URLS` و `5100:8080` | Docker بس |

**القاعدة:** جوه Docker كل حاجة 8080 وبالأسماء. البورتات اللي في `ports:` أبواب على جهازك إنت بس. والقيمة اللي بتيجي آخر بتكسب: `appsettings.json` ثم `appsettings.Docker.json` ثم متغيرات البيئة.

خدمة `gateway-api` في الـ compose:

- من غير `<<: *api`، لأن الـ anchor ده بيستنى قاعدة بيانات.
- من غير `depends_on`، لأن YARP بيكلم الخدمات وقت الطلب بس.
- healthcheck بـ `curl` على `/health/live`. صحة البوابة هي صحتها هي بس، عشان وقوع خدمة ميقفلش الباب على الباقي.
- حدود موارد 0.5 CPU و 256MB، والـ Dockerfile بينسخ فولدر البوابة بس.

### توزيع الحمل وفحص النسخ

```json
"ordering-cluster": {
  "LoadBalancingPolicy": "RoundRobin",
  "HealthCheck": {
    "Active": {
      "Enabled": true,
      "Interval": "00:00:10",
      "Timeout": "00:00:05",
      "Policy": "ConsecutiveFailures",
      "Path": "/health/ready"
    }
  }
}
```

وفي `appsettings.Docker.json` كل نسخة Destination لوحدها:

```json
"ordering-cluster": {
  "Destinations": {
    "ordering-1": { "Address": "http://ecommerce-ordering-api-1:8080/" },
    "ordering-2": { "Address": "http://ecommerce-ordering-api-2:8080/" },
    "ordering-3": { "Address": "http://ecommerce-ordering-api-3:8080/" }
  }
}
```

**ليه مش عنوان واحد `ordering-api`؟** اسم الخدمة بيشاور على النسخ كلها، بس:

1. البوابة بتعيد استخدام الاتصال (Connection Pooling)، فبتلزق في نسخة.
2. شايفة عنوان واحد، فمش هتعرف تفحص كل نسخة ولا تشيل الواقعة.

اسم الكونتينر = اسم المشروع (`ecommerce`) + اسم الخدمة + رقم النسخة، لأن Ordering ملوش `container_name`. تتأكد منه بـ `docker compose ps ordering-api`.

### نسخة بتقع

| الثانية | اللي بيحصل |
| --- | --- |
| 0 | التلات نسخ سليمة، والتوزيع بالدور. |
| 2 | النسخة 2 وقعت. |
| 2 لحد 10 | طلبات رايحة للنسخة 2 ممكن ترجع 502، لأن البوابة لسه متعرفش. |
| 10 | أول فحص فشل (مرة). |
| 20 | تاني فحص فشل (مرتين)، فالنسخة اتشالت. |
| بعد كده | التوزيع على 1 و 3 بس، وكل الطلبات 200. |

| نوع الفحص | بيشتغل إزاي | الميزة | العيب |
| --- | --- | --- | --- |
| Active | البوابة بتسأل كل نسخة كل فترة | بيكتشف حتى لو مفيش طلبات | بطيء (لحد 20 ثانية) |
| Passive | بتراقب الطلبات الحقيقية | سريع وقت الزحمة | عملاء حقيقيين بياخدوا Error الأول |

في الإنتاج بنستخدم الاتنين. إحنا اكتفينا بالـ Active.

### أكواد الحالة من البوابة

| الكود | معناه | مين السبب |
| --- | --- | --- |
| 404 من غير لوج في الخدمة | مفيش Route مطابق | البوابة |
| 404 مع لوج في الخدمة | المسار مش موجود جوه الخدمة | الخدمة |
| 502 | البوابة مش قادرة توصل للخدمة | الخدمة واقفة |
| 504 | الخدمة اتأخرت في الرد | الخدمة بطيئة |

### التجارب اللي نجحت

- التوجيه من `localhost:5100` للخدمات التلاتة.
- `/payment/anything` رجّع 404 من البوابة.
- مسح `PathRemovePrefix` خلّى Catalog يستلم `/catalog/api/...` ويرجّع 404.
- `docker compose stop catalog-api` رجّع 502 لـ Catalog، و Ordering اشتغل عادي.
- 3 نسخ Ordering بـ `--scale ordering-api=3`: التوزيع 2/2/2.
- `docker stop ecommerce-ordering-api-2`: بعد حوالي 25 ثانية الطلبات كلها 200 على 1 و 3.

## الدرس 9.3: شغل البوابة المشترك

تلات حمايات اتعملت مرة واحدة على الباب: مهلة لكل خدمة، وحد لعدد الطلبات، وسياسة (CORS) للواجهة.

### الجزء 1: المهلة (Timeout)

الافتراضي في YARP هو 100 ثانية. خدمة معلّقة معناها عميل مستني، وموارد محجوزة في البوابة ممكن تخنقها.

```json
"basket-cluster": {
  "LoadBalancingPolicy": "RoundRobin",
  "HttpRequest": { "ActivityTimeout": "00:00:35" }
}
```

**القاعدة:** المهلة أكبر من أبطأ عملية طبيعية، وقيمة لكل Cluster، والرقم بيتاخد من القياس (p99) في المراقبة. Basket أخد 35 ثانية لأن الـ Checkout فيه `AddStandardResilienceHandler` ممكن ياخد لحد 30 ثانية.

لو مهلة Basket كانت 10 ثواني:

| الثانية | اللي بيحصل |
| --- | --- |
| 0 | العميل داس (Checkout)، والطلب عدّى من البوابة لـ Basket. |
| 0 | Basket بيسأل Catalog عن الأسعار، بس Catalog بطيء. |
| من 0 لـ 14 | الـ (Resilience Handler) بيعيد المحاولة. |
| 10 | البوابة زهقت تستنى، ورجّعت 504. |
| 14 | Catalog رد، و Basket حفظ ونشر الحدث بالـ (Outbox). |

**النتيجة:** العميل فاكر إن الطلب فشل، والطلب اتعمل فعلًا. الـ 504 معناها "مش عارف"، مش "فشل"، والحل الكامل (Idempotency Key).

**الـ Timeout مقابل الـ Health Check:**

|  | طلب الفحص | طلب العميل |
| --- | --- | --- |
| مين بيبعته | البوابة، كل 10 ثواني | العميل |
| مهلته | 5 ثواني (`HealthCheck.Timeout`) | `ActivityTimeout` |
| بيحمي | الطلبات الجاية | الطلب الحالي |

الـ Health Check لوحده مش كفاية: بطيء في الاكتشاف، ومش بيلغي الطلبات اللي اتبعتت، وممكن الفحص ينجح و Endpoint معيّن يعلّق.

**التجربة:** `docker pause` لنسخة Ordering مع مهلة 5 ثواني. طلب رجع 504 بعد 5 ثواني، وبعد حوالي 30 ثانية الـ Health Check شال النسخة.

### الجزء 2: تحديد عدد الطلبات (Rate Limiting)

جوه ASP.NET نفسه من .NET 7، من غير باكدج. نفس فكرة `throttle:60,1` في Laravel، بس مرة واحدة على البوابة.

| الطريقة | بتشتغل إزاي | العيب |
| --- | --- | --- |
| Fixed Window | عدّاد بيتصفّر كل فترة ثابتة | ممكن يعدّي ضعف الحد عند حدود الفترتين |
| Sliding Window | الفترة بتتحرك مع الوقت | أدق وأعقد |
| Token Bucket | تذاكر بتتملي بمعدل ثابت | الأنسب للزحمة المفاجئة |

الشكل النهائي: حد عام لكل عميل + حد أشد على الـ Checkout.

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global limit: every request, one counter per client IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/health"))
            return RateLimitPartition.GetNoLimiter("health");

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            });
    });

    // Strict policy, used only by the checkout route
    options.AddPolicy("checkout", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromSeconds(30),
                QueueLimit = 0
            }));

    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        return ValueTask.CompletedTask;
    };
});

// at the end of Program.cs
static string ClientIp(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
```

```json
"checkout-route": {
  "ClusterId": "basket-cluster",
  "RateLimiterPolicy": "checkout",
  "CorsPolicy": "frontend",
  "Match": { "Path": "/basket/api/basket/checkout", "Methods": [ "POST" ] },
  "Transforms": [ { "PathRemovePrefix": "/basket" } ]
}
```

- الافتراضي في ASP.NET هو 503، وده غلط في المعنى. 429 معناها "إنت بتبعت كتير"، ومش بتعمل (Alerts) كاذبة.
- `GetNoLimiter("health")` عشان فحص Docker ميترفضش.
- `checkout-route` بيكسب على `basket-route` لأن مساره أدق من `{**catch-all}`.
- العدّاد جوه البوابة مش جوه نسخ الخدمة، وهو عدّاد واحد لكل IP على البوابة كلها.
- في Docker كل طلبات جهازك بتبان بـ IP واحد. ورا (Load Balancer) محتاج `X-Forwarded-For` بحذر، وفي الموديول 10 العدّاد هيبقى لكل مستخدم.
- 3 نسخ من البوابة = 3 عدّادات، والحل عدّادات في Redis (مش معمول).

| التجربة | النتيجة |
| --- | --- |
| 25 طلب GET | أول 20 = 200، والباقي 429 |
| 4 طلبات Checkout ببيانات فاضية | 400 تلات مرات، وبعدين 429 من غير ما يوصل لـ Basket |
| لوج Ordering بعد 15 طلب بحد 10 | 10 سطور بس |

### الجزء 3: الـ (CORS)

الـ (Origin) = البروتوكول + الدومين + البورت. `localhost:3000` و `localhost:5100` اتنين مختلفين.

المتصفح عنده قاعدة (Same-Origin Policy): كود JavaScript في صفحة من Origin مش مسموحله يقرا رد من Origin تاني، إلا لو السيرفر قال إنه مسموح بهيدر `Access-Control-Allow-Origin`.

القاعدة دي بتحمي المستخدم:

| الخطوة | اللي بيحصل من غير القاعدة |
| --- | --- |
| 1 | إنت مسجّل دخول في البنك، والمتصفح شايل الـ (Cookie). |
| 2 | فتحت موقع مشبوه في تاب تانية. |
| 3 | الموقع بيبعت طلب للبنك، والمتصفح بيبعت الـ Cookie لوحده. |
| 4 | البنك بيرد ببياناتك، والموقع المشبوه بيقراها. |

المتصفح بيستأذن بطلب `OPTIONS` (Preflight) قبل `POST` بـ JSON، و `PUT`، و `DELETE`، وأي طلب فيه `Authorization`. لأن في الكتابة الطلب نفسه هو الضرر، فإخفاء الرد بعدين مش كفاية. الـ GET والـ (Form) العادي (Simple Requests) مش بيستأذنوا، وعشان كده Laravel محتاج `@csrf`.

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .WithMethods("GET", "POST", "PUT", "DELETE")
        .AllowAnyHeader());
});

// pipeline order
app.MapHealthChecks("/health/live");
app.UseCors();
app.UseRateLimiter();
app.MapReverseProxy();
```

```json
"Cors": {
  "AllowedOrigins": [ "http://localhost:3000" ]
}
```

- `CorsPolicy: frontend` على كل Route. لو اتنسى Route، الواجهة مش هتعرف تكلمه.
- `UseCors` قبل `UseRateLimiter`، عشان الـ Preflight ميتحسبش من رصيد العميل.
- في الإنتاج: `Cors__AllowedOrigins__0: https://shop.com` كمتغير بيئة، ومن غير `localhost:3000`.
- الـ (CORS) في البوابة بس: المتصفح بيكلم البوابة بس، ولو اتحط في الخدمة كمان الهيدر هيتكرر والمتصفح يرفض.

| الخطأ الشائع | النتيجة |
| --- | --- |
| `http://localhost:3000/` بشرطة في الآخر | مش هيطابق |
| `AllowAnyOrigin()` | أي موقع يكلمك، وبيلغي فايدة الـ CORS |
| CORS في البوابة والخدمة | هيدر متكرر والمتصفح يرفض |

**التجربة:** صفحة `cors-test.html` على `python3 -m http.server 3000`. قبل الحل: `Blocked` مع إن الطلب وصل لـ Ordering ورجع 200. بعد الحل: `OK`. و `curl -X OPTIONS` بـ Origin مسموح رجّع 204 بالهيدر، وبـ `http://evil.com` من غير الهيدر.

## الدرس 9.4: الـ (BFF) وصفحة "طلباتي"

الـ (Backend For Frontend) سيرفر صغير مخصوص لواجهة واحدة، بيجمّع الداتا من كذا خدمة بالشكل اللي الشاشة محتاجاه. صفحة "طلباتي" بقت طلب واحد: `GET /bff/my-orders`.

### المشكلة

الصفحة محتاجة الطلبات من Ordering، وصورة كل منتج وسعره دلوقتي وهل لسه متاح من Catalog. من غير BFF، الواجهة بتعمل طلبين ورا بعض، وبتعرف تقسيمنا الداخلي، ومنطق الدمج بيتكرر في كل واجهة.

من 8.5: اخترنا (API Composition) مش (Read Model)، لأن نسخ الصور لـ Ordering تقيل على صفحة عرض.

### البوابة مقابل الـ BFF

|  | البوابة | الـ BFF |
| --- | --- | --- |
| بتعرف الشاشات؟ | ❌ لأ | ✅ أيوه |
| بتعمل إيه | توجيه وحماية وحدود | تجميع وتشكيل داتا |
| كام واحدة | واحدة للكل | واحد لكل نوع واجهة |
| فيها منطق بيزنس؟ | ❌ لأ | ❌ لأ، تجميع للعرض بس |

**في الشغل:** النمط اتعرف من SoundCloud ومنتشر في الشركات اللي عندها واجهات كتير، بس كتير من الشركات الصغيرة بتكتفي بالبوابة أو بـ (GraphQL). الـ BFF بيظهر لما يبقى فيه شاشات مركّبة كتير، أو واجهات باحتياجات مختلفة، أو لأسباب أمان (يمسك التوكن على السيرفر). أهم سبب لـ BFF منفصل للموبايل: التطبيقات القديمة اللي متحدّثتش. متعملش BFF للموبايل من الأول.

**في مشروعنا:** `Web.Bff` واحد، للشاشات المركّبة بس. الطلبات البسيطة بتروح من البوابة للخدمة على طول.

### الطلب بيمشي إزاي

| الخطوة | اللي بيحصل |
| --- | --- |
| 1 | الواجهة بتبعت `GET /bff/my-orders?buyerId=ahmed` للبوابة. |
| 2 | البوابة بتوجّهه لـ `Web.Bff`. |
| 3 | الـ BFF بيطلب الطلبات من Ordering. |
| 4 | الـ BFF بيجمع أرقام المنتجات من غير تكرار. |
| 5 | الـ BFF بيطلب بياناتهم من Catalog في طلب (Batch) واحد. |
| 6 | الـ BFF بيدمج ويرجّع رد واحد. |

مفيش `Task.WhenAll` لأن الخطوة 5 محتاجة نتيجة الخطوة 3. والخطوة 5 طلب واحد مش طلب لكل منتج، عشان نتجنب (N+1).

### شكل الرد

```json
{
  "productDetailsAvailable": true,
  "orders": [
    {
      "orderId": "6f1c...",
      "status": "Submitted",
      "createdAt": "2026-11-22T10:15:00Z",
      "currency": "EGP",
      "total": 1500.00,
      "items": [
        {
          "productId": "d9df...",
          "name": "Laptop",
          "quantity": 1,
          "paidPrice": 1500.00,
          "imageUrl": "https://cdn.example.com/laptop.jpg",
          "currentPrice": 1400.00,
          "isAvailable": true
        }
      ]
    }
  ]
}
```

| الحقل | جاي منين | ليه |
| --- | --- | --- |
| `name` | Ordering | الاسم وقت الشراء، الفاتورة متتغيرش |
| `paidPrice` | Ordering | السعر اللي اتدفع فعلًا |
| `imageUrl` | Catalog | Ordering مش بيخزّن صور |
| `currentPrice` | Catalog | "سعره دلوقتي" جنب "اشتري تاني" |
| `isAvailable` | Catalog | المنتج لسه موجود ومتاح في المخزن |
| `productDetailsAvailable` | الـ BFF | هل قدر يجيب بيانات Catalog |

### لو خدمة وقعت (Graceful Degradation)

| الحالة | الـ BFF يرجّع إيه | الواجهة تعرض إيه |
| --- | --- | --- |
| الاتنين شغالين | الرد كامل | الصفحة كاملة |
| Catalog واقع | حقول Catalog بـ `null`، و `productDetailsAvailable: false` | صورة افتراضية، وزرار "اشتري تاني" مخفي |
| المنتج اتمسح أو خلص | `isAvailable: false` | علامة "غير متاح" |
| Ordering واقع | 503 | رسالة "حاول تاني" |

`null` معناها "مش عارفين"، و `false` معناها "عارفين إنه مش متاح".

### الكود: Catalog

Endpoint عرض منفصل عن `/prices`، لأن `/prices` عقد الـ Checkout ومش هنكبّره بحاجات عرض. كل مستهلك ياخد Endpoint على قد احتياجه.

```csharp
public record ProductSummaryResponse(Guid Id, string Name, string ImageUrl, decimal Price, bool InStock);

// GET api/products?ids=...&ids=...
[HttpGet]
public async Task<IActionResult> GetSummaries([FromQuery] Guid[] ids, CancellationToken cancellationToken)
{
    if (ids.Length is 0 or > 100)
        return Problem(statusCode: StatusCodes.Status400BadRequest,
                       title: "ids must contain between 1 and 100 items.");

    var products = await dbContext.Products
        .AsNoTracking()
        .Where(p => ids.Contains(p.Id))
        .Select(p => new ProductSummaryResponse(p.Id, p.Name, p.ImageUrl, p.Price, p.Stock > 0))
        .ToListAsync(cancellationToken);

    return Ok(products);
}
```

### الكود: `Web.Bff`

| الملف | فيه إيه |
| --- | --- |
| `Clients/ServiceDtos.cs` | نسخ خاصة من JSON الخدمات، من غير Project References |
| `Clients/OrderingClient.cs` | Typed HttpClient لـ Ordering |
| `Clients/CatalogClient.cs` | Typed HttpClient لـ Catalog، بيقسّم بـ `Chunk(100)` |
| `MyOrders/MyOrdersResponse.cs` | شكل الرد، وحقول Catalog فيه nullable |
| `MyOrders/MyOrdersEndpoint.cs` | منطق الدمج |
| `Program.cs` | التسجيل والـ Resilience |

قلب الدرس:

```csharp
// 1) Orders are the core of the page: no orders = no page
IReadOnlyList<OrderDto> orders;
try
{
    orders = await ordering.GetOrdersAsync(buyerId, ct);
}
catch (Exception ex) when (!ct.IsCancellationRequested)
{
    logger.LogWarning(ex, "Ordering unavailable for buyer {BuyerId}", buyerId);
    return TypedResults.Problem(statusCode: 503,
        title: "Orders are unavailable right now. Please try again.");
}

// 2) Product details are an enhancement: if Catalog fails, keep going
var productIds = orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToArray();

Dictionary<Guid, ProductSummaryDto>? products = null;
try
{
    var list = await catalog.GetProductsAsync(productIds, ct);
    products = list.ToDictionary(p => p.Id);
}
catch (Exception ex) when (!ct.IsCancellationRequested)
{
    logger.LogWarning(ex, "Catalog unavailable, returning orders without product details");
}

// 3) Merge
return TypedResults.Ok(new MyOrdersResponse(products is not null, views));
```

مهلات مختلفة لكل خدمة حسب أهميتها للصفحة:

```csharp
// Ordering: the core of the page, can wait a bit
builder.Services.AddHttpClient<OrderingClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Ordering"]!))
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
    });

// Catalog: only an enhancement, give up fast
builder.Services.AddHttpClient<CatalogClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]!))
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3);
    });
```

`TotalRequestTimeout` بيقفل على كل المحاولات مع بعض، فلو Catalog وقع الصفحة بترجع بعد 3 ثواني، مش 30 (الافتراضي). والعناوين لازم تخلص بـ `/`.

### Docker والبوابة

- `web-bff` في الـ compose **من غير `ports:`**. المتصفح لازم يعدّي من البوابة عشان الـ Rate Limiting والـ CORS.
- `appsettings.Docker.json` بتاع الـ BFF: `ordering-api:8080` و `catalog-api:8080` (اسم الخدمة، فمش بيوزّع بالتساوي على النسخ).
- في البوابة: `bff-route` (`/bff/{**catch-all}` + `PathRemovePrefix` + CORS)، و `bff-cluster` بمهلة 15 ثانية (أكبر من 10 + 3)، وفحص على `/health/live` لأن الـ BFF مالوش قاعدة.

| التجربة | النتيجة |
| --- | --- |
| `curl localhost:5100/bff/my-orders?buyerId=ahmed` | الرد كامل |
| `curl localhost:5400/...` | Connection refused (الـ BFF مقفول من برا) |
| `docker compose stop catalog-api` | 200 في حوالي 3 ثواني، و nulls و `productDetailsAvailable: false` |
| `docker compose stop ordering-api` | 503 |

### المقايضات وإمتى ما تستخدموش

| الميزة | التمن |
| --- | --- |
| الواجهة بتعمل طلب واحد | خدمة زيادة تتبني وتتنشر وتتراقب |
| الواجهة مش عارفة التقسيم الداخلي | خطوة زيادة في الطريق |
| الصفحة تفضل شغالة لو جزء وقع | وقت الرد = مجموع الطلبات اللي ورا بعض |

متستخدموش لو الشاشة بتكلم خدمة واحدة، أو عندك واجهة بسيطة. وأشهر غلط: منطق بيزنس في الـ BFF، فيتحول لـ (Monolith) صغير. لو نفس المنطق محتاجه في BFF الويب و BFF الموبايل، يبقى بيزنس ومكانه الخدمة.

## أسئلتك وإجاباتها

كل الأسئلة اللي سألتها أثناء الموديول، متقسمة حسب الموضوع.

### البورتات والأسماء والإعدادات

| السؤال | الإجابة |
| --- | --- |
| البورتات في `appsettings.json` والـ Dockerfile والـ compose، مين المصدر؟ | فيه سؤالين: البوابة بتسمع فين (`launchSettings` مع `dotnet run`، و `ASPNETCORE_URLS` = 8080 في Docker)، وبتبعت لفين (`appsettings.json` لـ localhost، و `appsettings.Docker.json` بالأسماء). `EXPOSE` ملحوظة بس. |
| عناوين localhost في `appsettings.json` مش مستخدمة في Docker؟ | بتتقري، بس `appsettings.Docker.json` بيكتب فوقها بنفس المفتاح. القيمة اللي بتيجي آخر بتكسب. |
| لازمة `ports:` في خدمات الـ compose إيه؟ | ليك إنت بس: Swagger، و SSMS، و `dotnet run`، ولوحة RabbitMQ. الكونتينرات بتكلم بعض بالأسماء. في الإنتاج بنسيب بورت البوابة بس. |
| أتأكد من أسماء الكونتينرات إزاي؟ | `docker compose ps ordering-api`، عمود `NAME`. |
| ليه `ecommerce-` في أول Ordering بس؟ | Catalog و Basket نسخة واحدة فاسم الخدمة كفاية. Ordering 3 نسخ وملوش `container_name`، فـ Docker بيؤلّف الاسم: المشروع + الخدمة + الرقم. |
| لو كتبت `ordering-api-1`؟ | مش هيشتغل: مش موجود في DNS بتاع Docker، فـ 502 و Unhealthy. تتأكد بـ `docker compose exec gateway-api getent hosts <name>`. |
| الفرق بين اسم الخدمة واسم الكونتينر؟ | اسم الخدمة (`ordering-api`) لكل النسخ، من `services:`. اسم الكونتينر (`ecommerce-ordering-api-1`) لنسخة واحدة، من `docker compose ps`. استخدم اسم الخدمة، إلا لو محتاج نسخة بعينها. |
| أوقف نسخة واحدة إزاي؟ | `docker stop ecommerce-ordering-api-2`. أما `docker compose stop ordering-api` فبيوقف التلاتة. |
| الخدمة بتكلم قاعدتها بالاسم ولا البورت؟ | الاتنين: `catalog-db,1433`. الاسم بيقول أنهي كونتينر، والبورت أنهي باب جواه. |
| `Server=localhost,1434` في `appsettings.json` مستخدم؟ | لأ. في Docker الـ `entrypoint` بيعمل `ConnectionStrings__DefaultConnection`، ومع `dotnet run` الـ `LocalEnvLoader` بيعمله من `.env`. و `${DB_NAME}` مش بيتبدّل في .NET. اتمسح. |
| ملف `.env` بتاع مين؟ | الاتنين. الأسرار للطريقتين، و `DB_HOST=localhost` و `DB_PORT` لجهازك. في Docker الـ compose بيغطي على العنوان بـ `catalog-db` و 1433. |
| Basket بيكلم قاعدته على 1433 ولا 1435؟ | جوه Docker `basket-db,1433`، ومع `dotnet run` `localhost,1435`. نفس القاعدة. |
| لو شلت `ports` من `ordering-db` أو RabbitMQ؟ | الخدمات جوه Docker تشتغل عادي، بس SSMS ولوحة RabbitMQ و `dotnet run` يبطّلوا. وقت التطوير سيبها بـ `127.0.0.1`. |
| بورتات RabbitMQ من غير `127.0.0.1`؟ | مفتوحة لأي جهاز على نفس الشبكة. الأحسن `127.0.0.1:5672:5672` و `127.0.0.1:15672:15672`. |

### البوابة والتوزيع والفحص

| السؤال | الإجابة |
| --- | --- |
| من غير بوابة، الواجهة هتكلم أنهي نسخة؟ | هتقسم بإيدها ومش بالعدل، ولو نسخة وقعت هتفضل تبعتلها، وأي نسخة جديدة محتاجة تعديل في الواجهة. |
| Basket يكلم Catalog عن طريق البوابة؟ | لأ. طلب داخلي، مش محتاج Rate Limiting ولا CORS، وهيزوّد ضغط من غير فايدة. |
| البوابة بقت Single Point of Failure؟ | أكتر من نسخة ورا Load Balancer، وده سهل لأنها Stateless. |
| لو مسحت `PathRemovePrefix`؟ | Catalog بيستلم `/catalog/api/products/...` ويرجّع 404 من غير Body، عشان كده `curl` بيبان فاضي. استخدم `curl -i`، وابص على لوج الخدمة عشان تعرف الـ 404 من مين. |
| Swagger من البوابة؟ | مش هيشتغل، لأنه بيطلب `/swagger/v1/swagger.json` من غير البادئة. بيفضل على بورت كل خدمة. |
| Docker بيوزّع `ordering-api`، ليه 3 Destinations؟ | الاتصال بيلزق في نسخة، والبوابة مش هتعرف تفحص كل نسخة وتشيل الواقعة. |
| Active مقابل Passive Health Check؟ | Active: البوابة بتفحص كل فترة، بيكتشف من غير طلبات بس بطيء. Passive: بتراقب الطلبات، سريع بس عملاء بياخدوا Error الأول. في الإنتاج الاتنين. |
| `/health/live` في البوابة و `"Path": "/health/ready"` في إعداداتها؟ | الأول Docker بيفحص بيه البوابة، والتاني البوابة بتفحص بيه الخدمات. |
| أول طلب أخد 454ms؟ | (Cold Start): .NET بيجهّز الكود و EF Core بيفتح أول اتصال. طبيعي. |

### المهلة وتحديد الطلبات

| السؤال | الإجابة |
| --- | --- |
| الـ Health Check مهلته 5 ثواني، ليه Timeout كمان؟ | الـ 5 ثواني لطلب الفحص بس. طلب العميل مهلته 100 ثانية افتراضيًا، والفحص مش بيلغي الطلبات اللي اتبعتت. |
| Checkout رجعله 504، اتعمل ولا لأ؟ | مش معروف. تسأل `GET /api/orders` (وممكن يتأخر بسبب RabbitMQ)، والحل الصح Idempotency Key. ولو Catalog واقع، Basket نفسه بيرجّع 503. |
| تختار قيمة الـ Timeout إزاي؟ | أكبر من أبطأ عملية طبيعية، لكل Route، ومن القياس (p99). |
| 3 نسخ Ordering والحد 10، ليه مش 30؟ | العدّاد في البوابة قبل اختيار النسخة. الـ "3 أضعاف" كانت عن 3 نسخ من البوابة. |
| 10 على Ordering وطلب على Catalog؟ | 429، عدّاد واحد لكل IP على البوابة كلها. لعدّاد لكل خدمة: سياسة لكل Route أو اسم الـ Route في الـ `partitionKey`. |
| سياسة واحدة للكل ولا لكل خدمة؟ | حد عام لكل عميل + حد أشد على الـ Endpoints الغالية (Checkout وتسجيل الدخول). الحد حسب تمن الـ Endpoint. |
| `ClientIp` فايدتها؟ | بتجيب IP العميل كـ `partitionKey`، في مكان واحد بدل التكرار. `?? "unknown"` لو الـ IP بـ null. |
| الحد العام محتاج يتكتب في الـ Routes؟ | لأ، `GlobalLimiter` بيشتغل على كل طلب. `RateLimiterPolicy` للحدود الخاصة بس. |

### الـ CORS

| السؤال | الإجابة |
| --- | --- |
| اللينك فتح من شريط العنوان عادي، إزاي؟ | ده تنقّل مش طلب من كود صفحة تانية. الـ CORS بس لـ `fetch` و `axios` من صفحة على Origin تاني. |
| الـ Preflight هو الـ CSRF؟ | لأ. الـ Preflight المتصفح بيعمله لوحده. الـ CSRF Token بيحمي من الـ Simple Requests (زي Form عادي) اللي مش بتستأذن. مع JWT في هيدر `Authorization` مش محتاجينه. |
| CORS لموقعي بس بيقفل الـ API؟ | لأ. curl و Postman مش بيتأثروا. هو بيحمي المستخدم، مش الـ API. |
| الواجهة على `https://shop.com`؟ | متغير بيئة `Cors__AllowedOrigins__0`، من غير تعديل `appsettings.json` اللي جوه الـ Image، ومن غير `localhost:3000`. |
| رقم خطأ الـ CORS؟ | مفيش. المتصفح بيخبّي الرد، والكود بياخد `TypeError: Failed to fetch`. |
| ليه الـ Preflight في الكتابة بس؟ | في GET إخفاء الرد كفاية. في POST و PUT و DELETE الطلب نفسه هو الضرر، فالمتصفح بيستأذن قبل. |

### الـ BFF والكود

| السؤال | الإجابة |
| --- | --- |
| الشركات بتستخدم الـ BFF فعلًا؟ | أيوه، بس مش كلها. لما يبقى فيه شاشات مركّبة كتير، أو واجهات مختلفة، أو لأسباب أمان. |
| داتا من خدمة واحدة أعدّي على الـ BFF؟ | طريقتين موجودين. اخترنا: البسيط من البوابة للخدمة، والمركّب بس من الـ BFF. متضيفش خطوة مش بتعمل شغل. |
| داتا الويب بتفرق عن الموبايل؟ | غالبًا متقاربة. الفصل بيحصل بسبب تحديث التطبيقات، والشبكة، والمميزات، والفرق. |
| `Chunk(100)`؟ | Catalog بيرفض أكتر من 100 رقم. 250 منتج = 3 طلبات (100 و 100 و 50) بدل 400 وصفحة من غير صور. |
| `loggerFactory.CreateLogger("MyOrders")`؟ | Logger باسم بيظهر في اللوج. `ILogger<T>` مش نافع لأن الكلاس `static`. |
| ليه `TypedResults.Problem`؟ | نفس `Problem()` بتاع الـ Controllers، بس في Minimal API مفيش `ControllerBase`. |
| `ToDictionary` بيطلّع إيه؟ | كل منتج متخزّن برقمه (Key = Id)، فالوصول ليه مباشر. لو Catalog وقع، بيفضل `null`. |
| `AddProblemDetails` و `AddHealthChecks` منين؟ | جوه ASP.NET نفسه، وهما نفس اللي في `ServiceDefaults`. |
| `IsAvailable = null` مقابل `false`؟ | `null` مش عارفين (صورة افتراضية)، و `false` غير متاح (علامة وزرار مقفول). |
| ليه 3 ثواني مش 30؟ | `TotalRequestTimeout` بتاع Catalog 3 ثواني، وبيقفل على كل المحاولات مع بعض. |

## المشاكل اللي قابلتها وحلولها

أغلب المشاكل كانت في الإعدادات مش في الكود، واللوج كان بيوضح السبب في كل مرة.

| المشكلة | العَرَض | السبب | الحل |
| --- | --- | --- | --- |
| البورت مشغول | `failed to bind host port 127.0.0.1:6000/tcp: address already in use` | البوابة بـ `dotnet run` من الجزء 1 كانت لسه شغالة | `Ctrl + C`، وفحص بـ `lsof -i :6000`. و 6000 المتصفحات بتمنعه (`ERR_UNSAFE_PORT`)، فاخترنا 5100 |
| توزيع 4/2/0 | النسخة 1 ماخدتش ولا طلب، والنسخة 2 أخدت 4 | سطور `ReverseProxy__...` القديمة في `environment` كانت بتغطي على `appsettings.Docker.json`، فـ `ordering-1` كان `ordering-api` (اسم الخدمة) | مسح السطور. التشخيص بـ `docker compose logs gateway-api \| grep "Proxying to"` و `printenv \| grep ReverseProxy`. بعدها 2/2/2 |
| البوابة مش راضية تقوم | `No matching IActiveHealthCheckPolicy found for the active health check policy name ' '` | `"Policy": " "` اتمسح بالغلط في `ordering-cluster` | `"Policy": "ConsecutiveFailures"`. YARP بيتحقق من الإعدادات وقت الـ Startup (Fail Fast) |
| رجوع لنسخة واحدة | بعد إعادة تشغيل الجهاز، Ordering نسخة واحدة | `docker compose up -d` العادي مش بيفتكر `--scale` | `docker compose up -d --scale ordering-api=3` |
| أول طلب بطيء | 454ms مقابل 2 لـ 13ms | (Cold Start) | طبيعي، مفيش حل مطلوب |
| Swagger من البوابة | مش شغال | بيطلب `/swagger/v1/swagger.json` من غير البادئة | بيفضل على بورت كل خدمة |
| بقايا إعدادات | `localhost,1434` في `appsettings.json` بتاع الخدمات | قيمة قديمة مش مستخدمة | اتمسح `ConnectionStrings` من الخدمات التلاتة |

**الدرس الأهم:** لما النتيجة غريبة، ابدأ باللوج قبل أي تخمين. في البوابة `Proxying to` بيقول كل طلب راح فين، و `Active health state` بيقول مين Healthy.

## الملخص و ADR-006

الواجهة بقت بتعرف عنوان واحد، والبوابة بتحمي وتوزّع، والـ BFF بيجمّع الصفحات المركّبة ويفضل شغال لو جزء وقع.

### نقط لازم تفتكرها

1. البوابة للطلبات الجاية من برا بس، ومفيهاش منطق بيزنس. الطلبات الداخلية مش بتعدّي عليها.
2. جوه Docker كل حاجة 8080 وبالأسماء، والقيمة اللي بتيجي آخر بتكسب في الإعدادات.
3. كل نسخة Destination لوحدها، والـ Timeout بيحمي الطلب الحالي والـ Health Check بيحمي الطلبات الجاية.
4. 429 مش 503، و 504 معناها "مش عارف"، والـ CORS بيحمي المستخدم مش الـ API.
5. الـ BFF بيجمّع ويشكّل بس، وبيفرّق بين الأساس (503) والتحسين (Graceful Degradation).

### قرارات ADR-006

| # | القرار |
| --- | --- |
| 1 | بوابة واحدة بـ YARP 2.3.0 (MIT)، الباب الوحيد للواجهة |
| 2 | التوجيه بالبادئة مع `PathRemovePrefix` |
| 3 | قواعد السلوك في `appsettings.json`، والعناوين في `appsettings.Docker.json` |
| 4 | RoundRobin + Active Health Checks، وكل نسخة باسم الكونتينر |
| 5 | صحة البوابة `live` بس |
| 6 | مهلة لكل Cluster أكبر من أبطأ عملية (10 / 35 / 10 / 15) |
| 7 | Rate Limiting: `GlobalLimiter` لكل IP + سياسة `checkout`، والرفض 429 مع `Retry-After` |
| 8 | CORS في البوابة بس، سياسة `frontend` من الإعدادات |
| 9 | `Web.Bff` منفصل للشاشات المركّبة بس، جوه الشبكة |
| 10 | "طلباتي" بـ API Composition: Ordering ثم Catalog Batch، و Graceful Degradation |
| 11 | Endpoint عرض منفصل في Catalog بدل تكبير `/prices` |

الملف الكامل في `docs/adr/ADR-006-api-gateway-and-bff.md`، وفيه البدائل اللي اترفضت والعواقب.

### ثغرات لسه مفتوحة

| الثغرة | هتتقفل في |
| --- | --- |
| مفيش Idempotency Key للـ Checkout (و 504 بتخليها أهم) | لسه متحددش |
| `buyerId` جاي من الـ URL، والعدّاد لكل IP | الموديول 10 (JWT) |
| الـ BFF مش بيوزّع بالتساوي على نسخ Ordering | الموديول 13 |
| البوابة والـ BFF من غير Serilog و ProblemDetails من `ServiceDefaults` | الموديول 13 |
| Rate Limiting مش موزع، ومفيش Passive Health Check | لما نحتاجهم |

## اختبر نفسك

جاوب في دماغك الأول، وبعدين قارن بالإجابة.

### أسئلة مراجعة

| السؤال | الإجابة |
| --- | --- |
| 1. Basket بيسأل Catalog وقت الـ Checkout. الطلب يعدّي إزاي؟ | مباشرة على `catalog-api:8080`. طلب East-West مش بيعدّي على البوابة. |
| 2. Destination واحد `ordering-api:8080` و 3 نسخ، إيه اللي هيحصل؟ | الطلبات هتلزق في نسخة، والبوابة مش هتفحص كل نسخة (زي 4/2/0). |
| 3. ليه صحة البوابة `live` بس؟ | عشان وقوع خدمة ميقفلش الباب على الخدمات السليمة. |
| 4. Checkout رجع 504، نقول إيه؟ | مش معروف، ممكن يكون اتعمل. لازم نتأكد، والحل Idempotency Key. |
| 5. 20 طلب على Ordering وبعدين طلب على Catalog في نفس الـ 10 ثواني؟ | 429، الحد العام عدّاد واحد لكل IP. |
| 6. الـ CORS بيحمي من إيه؟ | بيمنع JavaScript في موقع تاني يقرا ردود الـ API من متصفح الضحية. مش بيمنع curl. |
| 7. Catalog وقع في صفحة "طلباتي"؟ | الطلبات من غير تفاصيل المنتجات، و `productDetailsAvailable: false`، في حوالي 3 ثواني. |
| 8. ليه مهلة الـ `bff-cluster` 15 ثانية؟ | أبطأ سيناريو للـ BFF = 10 (Ordering) + 3 (Catalog) = 13، فيلحق يرجّع 503 بتاعه بدل 504 من البوابة. |
| 9. ليه اسم المنتج في "طلباتي" من Ordering؟ | الاسم وقت الشراء، حتى لو المنتج اتغيّر أو اتمسح من Catalog. |
| 10. ليه `UseCors` قبل `UseRateLimiter`؟ | عشان طلب الاستئذان ميتحسبش من رصيد العميل. |

### أسئلة إنترفيو

| السؤال | إجابة نموذجية |
| --- | --- |
| الـ API Gateway بقى Single Point of Failure، هتعمل إيه؟ | أكتر من نسخة ورا Load Balancer من الكلاود أو Kubernetes. سهل لأن البوابة Stateless. |
| الفرق بين Active و Passive Health Check؟ | Active بيسأل كل فترة وبيكتشف من غير طلبات بس ببطء. Passive بيراقب الطلبات وسريع بس بعد أخطاء حقيقية. الاتنين مع بعض: Passive يشيل بسرعة، و Active يتأكد من الرجوع. |
| إزاي تختار قيمة الـ Timeout؟ | أكبر من أبطأ عملية طبيعية، لكل Route، ومن القياس (p99)، ومربوطة بالـ Resilience الداخلي. |
| ليه الـ Rate Limiting بيرجّع 429 مش 503؟ | 429 معناها العميل بيبعت كتير ويستنى `Retry-After`. 503 مشكلة عندنا، وبتعمل Alerts كاذبة. |
| ليه الـ CORS في الـ Gateway بس؟ | المتصفح بيكلم البوابة بس، والخدمات بتتكلم من سيرفر لسيرفر، ومكان واحد للإدارة، والهيدر المتكرر بيترفض. |
| الفرق بين الـ API Gateway والـ BFF؟ وإمتى أكتر من BFF؟ | البوابة عامة للتوجيه والحماية، والـ BFF مخصوص لواجهة وبيجمّع لشاشاتها. أكتر من BFF لما الواجهات احتياجاتها تختلف (خصوصًا تحديث تطبيقات الموبايل) أو فرق مختلفة. |
| أشهر غلط مع الـ BFF؟ | منطق بيزنس جواه. الـ BFF بيجمّع ويشكّل بس، وأي قرار مكانه الخدمة صاحبة البيانات. |

### مهام صغيرة للتدريب

- [ ] اعمل Rate Limiting منفصل لكل Route بإضافة اسم الـ Route للـ `partitionKey`.
- [ ] زوّد Passive Health Check لـ `ordering-cluster` وقارن سرعة الاكتشاف.
- [ ] اعمل overload لـ `AddServiceDefaults` من غير `DbContext` واستخدمه في البوابة والـ BFF.
