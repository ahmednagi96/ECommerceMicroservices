# الموديول 10: الهوية (Keycloak و JWT)

Nov 28, 2026 · @Ahmed Nagi

## نظرة عامة

الموديول 10 خلّى (Keycloak) هو المكان الوحيد اللي بيطبع التوكنات، وكل الخدمات بتاخد "إنت مين" من التوكن بس، مش من الرابط ولا الـ (Body).

مكانه في الكورس: **المرحلة الرابعة** (الحافة والأمان)، بعد الموديول 9 (البوابة والـ BFF)، وقبل الموديول 11 (Saga).

### خريطة الدروس

| الدرس | الموضوع | الناتج |
| --- | --- | --- |
| 10.1 | ليه الـ (Auth) القديم ميمشيش زي ما هو؟ | مزود هوية واحد، و RS256 بدل HS256، و (Discovery) و (JWKS) |
| 10.2 | (Keycloak) في Docker | `ecommerce` Realm، و `ecommerce-web`، و (Audience) `ecommerce-api`، و 3 مستخدمين، وملف الـ (Realm) في الريبو |
| 10.3 | الخدمات تتأكد من التوكن | `KC_HOSTNAME`، و `[Authorize]` في Ordering و Basket، و `buyerId` = `sub` |
| 10.4 | الـ (BFF) والبوابة | (Token Relay)، والعدّاد لكل مستخدم، و `AuthorizationPolicy` |
| 10.5 | الصلاحيات | الأدوار بـ (Mapper)، و `AppRoles`، وصاحب الطلب يلغي طلبه بس (IDOR) |
| 10.6 | القفلة | الاختبار، و ADR-007، والملف ده |

### الشكل النهائي للكود

| المكوّن | التغيير |
| --- | --- |
| `keycloak` في الـ compose | `quay.io/keycloak/keycloak:26.8.2` بـ `start-dev --import-realm`، على `127.0.0.1:8180`، و volume `keycloak-data`، و Health Check على بورت 9000 |
| `keycloak/ecommerce-realm.json` | الـ (Realm) كله + المستخدمين التلاتة (باسوردات تجربة) |
| `BuildingBlocks.Authentication` (مشروع جديد) | `AddJwtAuthentication` و `GetRequiredUserId` و `AppRoles` |
| Ordering | `GetMine` بالـ `sub`، و `Confirm` للأدمن، و `Cancel` لصاحب الطلب أو الأدمن |
| Basket | `[Authorize]` على الكونترولر، و `BuyerId` (اتغيّر اسمه من `UserName` بـ Migration) من التوكن |
| Catalog | `PUT /price` للأدمن بس، والـ GET مفتوحة |
| `Web.Bff` | `.RequireAuthorization()` و `ForwardAccessTokenHandler` لـ Ordering بس |
| `Gateway.API` | التأكد من التوكن، و `ClientKey` (مستخدم أو IP)، و `AuthorizationPolicy: default` على 4 Routes |
| اتمسح | `UsersController` وجداول `Users` في Basket و Catalog |

## الدرس 10.1: ليه الـ (Auth) القديم ميمشيش زي ما هو؟

في الـ (Microservices) لازم مكان واحد بس يطبع التوكنات، والخدمات تتأكد منها من غير ما تقدر تطبعها.

### المشكلة

1. **النظام كان مفتوح:** `GET /api/orders?buyerId=ahmed` بيرجّع طلبات أي حد لأي حد، و Basket بياخد `UserName` من الـ (Body).
2. **نسخ `AuthService` في كل خدمة** معناه 3 جداول مستخدمين. ونحطه في البوابة ممنوع (منطق بيزنس وقاعدة بيانات).
3. **الـ `Secret` المشترك (HS256)** بيوقّع وبيتأكد. لو كل خدمة معاها، اختراق خدمة واحدة = المخترق يطبع توكنات أدمن تتقبل في كل النظام.

### الفكرة: مزود هوية + مفتاحين

| مين | بيعمل إيه | عندنا |
| --- | --- | --- |
| مزود الهوية (IdP) | يعرف المستخدمين والباسوردات، وصفحة الدخول، ويطبع التوكنات | (Keycloak) |
| الواجهة (Client) | تودّي المستخدم يسجّل دخول، وتبعت التوكن مع كل طلب | الفرونت |
| الخدمات (Resource Servers) | تتأكد من التوكن وتاخد منه الهوية | Basket و Ordering و Catalog والـ (BFF) والبوابة |

|  | HS256 (الـ Monolith) | RS256 (الموديول 10) |
| --- | --- | --- |
| المفاتيح | `Secret` واحد بيوقّع وبيتأكد | مفتاح خاص يوقّع، ومفتاح عام يتأكد بس |
| الخدمة معاها إيه | الـ `Secret` كله | المفتاح العام بس |
| لو Catalog اتخترق | يطبع توكنات أدمن | ميقدرش يطبع ولا توكن |

جرّبناها بـ (Console App) فيه `RSA.Create(2048)`: المفتاح العام بيرجّع `True` للبيانات السليمة، و `False` للمتغيّرة، وبيرمي خطأ لو حاول يوقّع.

وكل مفتاح عام مربوط بمفتاح خاص واحد بس. عشان كده التوكن فيه `kid`: الخدمة تختار بيه المفتاح المطابق من الليستة.

### الخدمة بتجيب المفتاح العام منين؟

| الرابط | فيه إيه |
| --- | --- |
| الدليل (Discovery): `/.well-known/openid-configuration` | `issuer` (الاسم الرسمي) و `jwks_uri` (مكان المفاتيح) و `token_endpoint` |
| (JWKS): `/protocol/openid-connect/certs` | المفاتيح العامة، كل واحد بـ `kid` و `alg` و `use` |

| الخطوة | اللي بيحصل |
| --- | --- |
| 1 | أول طلب فيه توكن وصل للخدمة. |
| 2 | الخدمة فتحت الدليل، ولقت فيه `jwks_uri`. |
| 3 | جابت المفاتيح العامة وحفظتها في الذاكرة. |
| 4 | قرت `kid` من التوكن، واتأكدت بالمفتاح المطابق. |
| 5 | الطلبات اللي بعد كده من الذاكرة، من غير ما تكلم (Keycloak). |
| 6 | كل كام ساعة بتجدد، ولو جالها `kid` مش عندها بتروح تجيب المفاتيح تاني. |

في الكود بنقول للخدمة "الدليل هنا" بس، بدل ما نديها `Secret` بإيدنا.

### اللي من ملف الـ (Authentication) القديم راح فين

| في الـ (Monolith) | في الموديول 10 |
| --- | --- |
| `signup` و `login` والهاش و `forgot-password` و `reset-password` و `change-password` | (Keycloak) جاهزين |
| (Refresh Token Rotation) واكتشاف التوكن القديم | مفتاح **Revoke Refresh Token** في (Keycloak) |
| `CreateAccessToken` بـ `Secret` | (Keycloak) بيوقّع بالمفتاح الخاص |
| `[Authorize]` و `AddJwtBearer` | زي ما هو، في كل خدمة |
| `currentUser.GetRequiredUserId()` | `User.GetRequiredUserId()` من `sub` |
| 401 = مش عارفينك، 403 = عارفينك وممنوع | زي ما هو |

### ليه (Keycloak)؟

| الاختيار | الرخصة | القرار |
| --- | --- | --- |
| (Keycloak) | Apache 2.0، مشروع في (CNCF) | ✅ مجاني، جاهز بلوحة تحكم وصفحات دخول، ومنتشر في الشغل |
| (Duende IdentityServer) | تجارية في الإنتاج | ❌ نفس سبب رفض MassTransit |
| (OpenIddict) | Apache 2.0 | ❌ مكتبة، وإنت اللي بتكتب صفحات الدخول والتسجيل |

المقايضة: خدمة Java تقيلة زيادة، والتوكن ميتلغيش فورًا (بيفضل شغال لحد ما يخلص). ومتستخدموش في (Monolith) صغير، أو لو الشركة عندها مزود هوية جاهز.

## الدرس 10.2: (Keycloak) في Docker

(Keycloak) بقى خدمة جاهزة جنب RabbitMQ و Redis، وإعداداته كلها ملف في الريبو.

### الجزء 1: التشغيل

```yaml
  keycloak:
    image: quay.io/keycloak/keycloak:26.8.2
    command: ["start-dev", "--import-realm"]
    restart: unless-stopped
    networks:
      - ecommerce
    env_file:
      - ./keycloak.env
    environment:
      KC_HEALTH_ENABLED: "true"
      KC_HOSTNAME: http://localhost:8180
      KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"
    ports:
      - "127.0.0.1:8180:8080"
    volumes:
      - keycloak-data:/opt/keycloak/data
      - ./keycloak/ecommerce-realm.json:/opt/keycloak/data/import/ecommerce-realm.json:ro
    healthcheck:
      test: ["CMD", "bash", "-c", "{ printf 'HEAD /health/ready HTTP/1.0\\r\\n\\r\\n' >&0; grep -q 'HTTP/1.0 200'; } 0<>/dev/tcp/localhost/9000"]
      interval: 10s
      timeout: 5s
      retries: 30
      start_period: 30s
    deploy:
      resources:
        limits:
          cpus: "1.0"
          memory: 1g
```

| السطر | ليه |
| --- | --- |
| `start-dev` | وضع التطوير: HTTP، وقاعدة (H2) صغيرة جوه `/opt/keycloak/data` |
| `keycloak.env` | `KC_BOOTSTRAP_ADMIN_USERNAME` و `PASSWORD` لأدمن مؤقت، والملف في `.gitignore` |
| `KC_HEALTH_ENABLED` | يفتح `/health/ready` على بورت الإدارة 9000 |
| `healthcheck` بـ `/dev/tcp` | صورة (Keycloak) مفيهاش `curl` |
| `keycloak-data` | الداتا نفسها (المستخدمين والمفاتيح) على جهازك، مش نسخة احتياطية |

في `docker compose ps` هتلاقي `8443/tcp` و `9000/tcp` من غير سهم: دول `EXPOSE` في الصورة بس، والمفتوح على جهازك `127.0.0.1:8180` بس.

### الجزء 2: الـ (Realm) والـ (Client)

| المصطلح | معناه | عندنا |
| --- | --- | --- |
| (Realm) | مساحة منفصلة بمستخدمينها ومفاتيحها | `ecommerce` (مش `master`) |
| (User) | الشخص | `ahmed` و `sara` (`customer`)، و `shop-admin` (`customer` و `admin`) |
| (Client) | **تطبيق** متسجّل مسموحله يطلب توكنات، مش العميل اللي بيشتري | `ecommerce-web` (الواجهة) |
| (Audience) | التوكن معمول لمين | `ecommerce-api` واحد لكل الـ API |

الـ (Client) زي تسجيل تطبيق Laravel عند جوجل في (Socialite)، بس بالعكس: (Keycloak) مكان جوجل، والواجهة مكان تطبيق Laravel.

رحلة الدخول (Standard flow) والإعدادات:

| الخطوة | اللي بيحصل | الإعداد |
| --- | --- | --- |
| 1 | الواجهة نقلت المتصفح لصفحة (Keycloak) وقالت "أنا `ecommerce-web`". | Client ID |
| 2 | (Keycloak) اتأكد إن عنوان الرجوع مسموح. | Valid redirect URIs: `http://localhost:3000/*` |
| 3 | المستخدم كتب الباسورد في صفحة (Keycloak)، مش في الواجهة. | — |
| 4 | رجع بكود مؤقت، والواجهة بدّلته بتوكن بـ `fetch`. | Web origins: `http://localhost:3000` |

| الإعداد | القيمة | ليه |
| --- | --- | --- |
| Client authentication | OFF (Public) | كود JavaScript في المتصفح، وأي سر جواه مكشوف. الحماية بداله Redirect URIs و (PKCE) |
| Standard flow | ON | الطريقة الصح للواجهة |
| Direct access grants | ON (للتطوير بس) | نطلّع توكن بـ `curl`. في الإنتاج مقفول |
| Audience mapper | `ecommerce-api` (Custom)، Add to access token | من غيره `aud` = `account` بس |
| Password policy | `^(?=.*[A-Za-z])(?=.*\d).{8,}$` | نفس قاعدة الـ (Monolith) |
| Default roles | `customer` | أي مستخدم جديد ياخده لوحده |

المستخدمين لازم يبقى ليهم Email و First name و Last name و `Temporary: OFF`، وإلا الدخول يفشل بـ `Account is not fully set up`.

### الجزء 3: أول توكن

```bash
TOKEN=$(curl -s -X POST http://localhost:8180/realms/ecommerce/protocol/openid-connect/token \
  -d grant_type=password -d client_id=ecommerce-web \
  -d username=ahmed -d password=Ahmed1234 | jq -r .access_token)

jwt_part() {
  local p
  p=$(echo "$1" | cut -d. -f"$2" | tr '_-' '/+')
  while [ $(( ${#p} % 4 )) -ne 0 ]; do p="$p="; done
  echo "$p" | base64 -d
}

jwt_part "$TOKEN" 1 | jq   # header: alg, kid
jwt_part "$TOKEN" 2 | jq   # payload: the claims
```

| الـ (Claim) | معناه |
| --- | --- |
| `sub` | رقم المستخدم (GUID)، عمره ما بيتغير |
| `iss` و `aud` و `exp` | مين طبعه، ومعمول لمين، وينتهي إمتى (5 دقايق) |
| `azp` | أنهي (Client) طلبه |
| `realm_access.roles` | الأدوار، متداخلة (اتحلت في 10.5) |

التجديد بـ `grant_type=refresh_token` بيرجّع زوج جديد. والتوكن القديم بيفضل شغال افتراضيًا، لحد ما نفعّل **Realm settings** ثم **Tokens** ثم **Revoke Refresh Token**.

### الجزء 4: الـ (Realm) كملف في الريبو

1. **Realm settings** ثم **Action** ثم **Partial export** (بالأدوار والـ Clients). المستخدمين مش بيتصدّروا، والأسرار بتبقى `*`.
2. الملف باسم `keycloak/ecommerce-realm.json` بالظبط (`<realm>-realm.json`).
3. نزوّد مصفوفة `users` بإيدنا (`credentials` و `realmRoles`).
4. `--import-realm` بيستورد **لو الـ (Realm) مش موجود بس**. أي تعديل في الملف محتاج مسح الـ volume.

```bash
# Never "docker compose down -v": it deletes the SQL Server volumes too
docker compose rm -sf keycloak
docker volume rm ecommerce_keycloak-data
docker compose up -d keycloak
```

بعد المسح الـ `kid` بيتغيّر، لأن المفاتيح في الـ volume مش في الملف. وكل مفتاح عام بيتأكد من توكنات مفتاحه الخاص بس، فالتوكنات القديمة تترفض.

## الدرس 10.3: الخدمات تتأكد من التوكن

Ordering و Basket بقوا ياخدوا المستخدم من `sub` في التوكن، والـ `?buyerId=` و `UserName` في الـ (Body) اتشالوا.

### الجزء 1: مشكلة الـ `iss`

(Keycloak) بيكتب اسمه من العنوان اللي الطلب جاله عليه:

| الخطوة | اللي بيحصل |
| --- | --- |
| 1 | التوكن اتطلب من جهازك على `localhost:8180`، فالـ `iss` = `http://localhost:8180/realms/ecommerce`. |
| 2 | Ordering فتح الدليل من `keycloak:8080`، فالـ `issuer` = `http://keycloak:8080/realms/ecommerce`. |
| 3 | التوقيع سليم والـ `aud` سليم، بس الاسمين مختلفين. |
| 4 | 401، وفي اللوج `IDX10205`. |

الحل في الـ compose:

```yaml
      KC_HOSTNAME: http://localhost:8180
      KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"
```

| الإعداد | `issuer` اللي Ordering بيشوفه | `jwks_uri` اللي Ordering بيشوفه | النتيجة |
| --- | --- | --- | --- |
| ولا سطر | `keycloak:8080` | `keycloak:8080` | ❌ الاسم مختلف عن التوكن |
| `KC_HOSTNAME` بس | `localhost:8180` | `localhost:8180` | ❌ `localhost` جوه الكونتينر = Ordering نفسه |
| السطرين | `localhost:8180` | `keycloak:8080` | ✅ |

الـ `issuer` دايمًا `8180` لأي حد. الـ `jwks_uri` بيتغيّر حسب السائل (Frontchannel مقابل Backchannel). في الإنتاج `KC_HOSTNAME: https://auth.shop.com` وغالبًا مش محتاجين السطر التاني.

### الجزء 2: Ordering

الباكدج: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.15 (MIT) في CPM.

```csharp
// GET api/orders  (the buyer comes from the token, not from the URL)
[Authorize]
[HttpGet]
public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
{
    var buyerId = User.GetRequiredUserId();
    var orders = await OrderQueries.GetByBuyerAsync(buyerId, cancellationToken);
    return Ok(orders);
}
```

| النقطة | ليه |
| --- | --- |
| `MapInboundClaims = false` | من غيره `sub` بيتحول لاسم Microsoft الطويل، و `FindFirstValue("sub")` يرجّع `null` |
| `UseAuthentication` قبل `UseAuthorization` | الأول "إنت مين؟" والتاني "مسموحلك؟". لو اتعكسوا كله 401 |
| `sub` مش `preferred_username` | اسم المستخدم ممكن يتغيّر، و `sub` لأ |
| `ValidIssuer` مش مكتوب | بيتاخد من الدليل لوحده |

| التجربة | النتيجة |
| --- | --- |
| من غير توكن | 401 + `WWW-Authenticate: Bearer` |
| توكن `ahmed` + `?buyerId=user-2` | طلبات `ahmed` بس، والـ `?buyerId` اتجاهل |
| توكن `sara` | 0 طلبات |
| (Header) و (Signature) بتوع `ahmed` + (Payload) بتاع `sara` | 401 `The signature is invalid` |
| بعد 5 دقايق | 401 `The token expired` |

### الجزء 3: Basket و `BuildingBlocks.Authentication`

```csharp
public static class ClaimsPrincipalExtensions
{
    // The user id from the token ("sub"). Only call it behind [Authorize].
    public static string GetRequiredUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub")
        ?? throw new InvalidOperationException("No 'sub' claim. Is [Authorize] missing on this endpoint?");
}
```

- `AddJwtAuthentication()` و `GetRequiredUserId()` في مكان واحد، وكل خدمة سطر واحد.
- الـ (Exception) معناها إننا نسينا `[Authorize]` (غلط في الكود = 500)، مش غلط من العميل.
- `UserName` اتشال من `AddItemRequestDTO` و `CheckoutRequestDTO`، و `[Authorize]` على `BasketController`.
- عمود `ShoppingCart.UserName` اتغيّر اسمه لـ `BuyerId` بـ (Migration).
- `UsersController` في Basket و Catalog اتمسح (كان بيرجّع كل المستخدمين بباسورداتهم لأي حد)، وجداول `Users` اتشالت.

**الرسالة في RabbitMQ مفيهاش توكن:**

| المصدر | بنتأكد إزاي |
| --- | --- |
| طلب HTTP من برا (North-South) | توكن في كل طلب |
| رسالة من جوه (East-West) | نثق في اللي نشرها (Basket اتأكد)، ونحمي الـ (Broker) نفسه |

(Checkout) كامل بتوكن `ahmed` عمل طلب في Ordering بالـ `sub` من نفسه، و `sara` خدت 400 "السلة فاضية" على سلتها هي.

## الدرس 10.4: الـ (BFF) والبوابة مع التوكن

الـ (BFF) بيعدّي توكن المستخدم لـ Ordering بس، والبوابة بتتأكد من التوكن وتعدّ الطلبات لكل مستخدم وتقفل الـ Routes المحمية.

### الجزء 1: الـ (BFF) والـ (Token Relay)

**المشكلة:** الـ (BFF) كان بيكلّم Ordering من غير توكن، فـ Ordering رد 401، والـ (BFF) فهمها "واقع" ورجّع 503.

| الطريقة | Ordering يعرف المستخدم؟ | إمتى |
| --- | --- | --- |
| (Token Relay): نفس توكن المستخدم | ✅ | الشغل باسم المستخدم (اخترناه) |
| (Client Credentials): توكن للـ BFF نفسه | ❌ | مفيش مستخدم (مهمة في الخلفية، داتا عامة) |
| (Token Exchange) | ✅ | لو كل خدمة ليها (Audience) مختلف |

**مشروع `BuildingBlocks.Authentication`:** الـ (BFF) والبوابة ملهمش قاعدة بيانات، و `BuildingBlocks` فيه EF Core. فالـ (Auth) بقى مشروع صغير (`FrameworkReference` + JwtBearer)، و `BuildingBlocks` بيعمله (Reference) فالخدمات ماتغيّرتش.

**`RequireHttpsMetadata` بقى إعداد:** الـ (BFF) والبوابة بيشتغلوا بـ `ASPNETCORE_ENVIRONMENT: Docker`، فـ `!IsDevelopment()` كان هيطلب HTTPS.

```csharp
// Secure by default; only local environments turn it off
options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", defaultValue: true);
```

```yaml
x-api-env: &api-env
  ASPNETCORE_ENVIRONMENT: Development
  ASPNETCORE_URLS: http://+:8080
  DB_PORT: "1433"
  Auth__MetadataAddress: http://keycloak:8080/realms/ecommerce/.well-known/openid-configuration
  Auth__RequireHttpsMetadata: "false"
```

**الـ (DelegatingHandler)** زي (Middleware) بس للطلبات الطالعة:

```csharp
// Copies the user's "Authorization: Bearer ..." header to the outgoing request
public sealed class ForwardAccessTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrEmpty(authorization))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);

        return base.SendAsync(request, ct);
    }
}
```

| الخطوة | اللي بيحصل |
| --- | --- |
| 1 | المستخدم بعت الطلب للبوابة بالتوكن. |
| 2 | البوابة عدّت الهيدر زي ما هو للـ (BFF). |
| 3 | الـ (BFF) اتأكد من التوكن (`.RequireAuthorization()`). |
| 4 | الـ (Handler) حط نفس الهيدر في الطلب الطالع لـ Ordering بس. |
| 5 | Catalog اتطلب من غير توكن (داتا عامة، Least Privilege). |

- `=` مش `Add` في الهيدر، عشان الـ (Retry) ميكررهوش.
- `catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)` قبل الـ `catch` العام: توكن خلص في الطريق = 401 مش 503.
- الـ Dockerfile بتاع الـ (BFF) بينسخ مشروع `BuildingBlocks.Authentication` كمان.

### الجزء 2: البوابة

**الفخ:** لو البوابة فكّت التوكن وخدت `sub` من غير ما تتأكد من التوقيع، المهاجم يبعت `sub` عشوائي في كل طلب، فياخد عدّاد جديد كل مرة، والحد ميخلصش.

```csharp
// Logged-in user: one counter per user. Guest: one counter per IP.
static string ClientKey(HttpContext context) =>
    context.User.Identity?.IsAuthenticated == true
        ? $"user:{context.User.GetRequiredUserId()}"
        : $"ip:{ClientIp(context)}";
```

```csharp
app.MapHealthChecks("/health/live");

app.UseCors();
app.UseAuthentication();   // fills context.User before the limiter needs it
app.UseRateLimiter();
app.UseAuthorization();    // needed for "AuthorizationPolicy" on routes

app.MapReverseProxy();
```

`AuthorizationPolicy` على الـ Route = `middleware('auth')` في Laravel:

| الـ Route | `AuthorizationPolicy` |
| --- | --- |
| `basket-route` و `checkout-route` و `ordering-route` و `bff-route` | `default` (لازم مستخدم) |
| `catalog-route` | مفيش (التصفح مفتوح) |

**(Defense in Depth):** البوابة ترفض بدري وتعدّ بالمستخدم، والخدمة متثقش في إن كل الطلبات عدّت من البوابة. والتأكد مرتين رخيص لأنه في الذاكرة بالمفتاح اللي في الكاش.

| التجربة | النتيجة |
| --- | --- |
| `/ordering/api/orders` من غير توكن | 401 من البوابة، ومفيش سطر في لوج Ordering |
| Catalog من غير توكن | 200 |
| 25 طلب بتوكن `ahmed` ثم طلب بتوكن `sara` (نفس الـ IP) | `ahmed`: 20 × 200 ثم 429، و `sara`: 200 |
| 25 طلب بتوكنات مزوّرة مختلفة | 20 × 200 ثم 429 (اتعدّوا كضيف واحد بالـ IP) |
| "طلباتي" بتوكن / من غير توكن / Catalog واقف | الصفحة كاملة / 401 من الـ BFF / `productDetailsAvailable: false` |

## الدرس 10.5: الصلاحيات (Authorization)

الأدمن بس يغيّر الأسعار ويأكّد الطلبات، وكل عميل يلغي طلبه هو بس.

|  | (Authentication) | (Authorization) |
| --- | --- | --- |
| السؤال | إنت مين؟ | مسموحلك؟ |
| بيتأكد من إيه | التوقيع و `exp` و `iss` و `aud` | الأدوار وصاحب الحاجة |
| لو فشل | **401**: مش عارفينك | **403**: عارفينك وممنوع |

### الجزء 1: الأدوار

`[Authorize(Roles = AppRoles.Admin)]` على `PUT /api/products/{id}/price` بس، مش على الكلاس: Basket بيكلّم `GET /prices` من جوه من غير توكن.

**المفاجأة:** الأدمن نفسه خد 403، لأن الأدوار متداخلة جوه `realm_access.roles`، و .NET بيدوّر على (Claim) مستقل.

| الحل | الميزة | العيب |
| --- | --- | --- |
| (Mapper) في (Keycloak) ✅ | من غير كود، وأي لغة تفهمه | لازم نصدّر الـ (Realm) تاني |
| (Claims Transformation) في .NET | من غير ما نلمس (Keycloak) | كود في كل خدمة |

1. `ecommerce-web` ثم Client scopes ثم `ecommerce-web-dedicated` ثم Mapper نوعه **User Realm Role**.
2. Name: `realm-roles-flat`، و Token Claim Name: `roles`، و Multivalued: ON، و Add to access token: ON.
3. في `AddJwtAuthentication`: `options.TokenValidationParameters.RoleClaimType = "roles";`
4. توكن **جديد** (القديم صورة من قبل الـ Mapper)، وتصدير الـ (Realm) تاني مع نسخ مصفوفة `users`.

```csharp
// Must match the realm role names in Keycloak exactly (case-sensitive)
public static class AppRoles
{
    public const string Admin = "admin";
    public const string Customer = "customer";
}
```

| التجربة | النتيجة |
| --- | --- |
| `PUT /price` بتوكن الأدمن الجديد | 200 |
| `PUT /price` بتوكن `ahmed` | 403 |
| `PUT /price` من غير توكن | 401 (من Catalog، الـ Route مفتوح في البوابة) |
| `GET` منتج من غير توكن | 200 |
| (Checkout) بتوكن `ahmed` | 202 (Basket لسه بيكلّم `/prices`) |

### الجزء 2: صاحب الطلب (IDOR)

`[Authorize]` بيسأل "مسجّل دخول؟" بس. `Cancel` بياخد **رقم الطلب** من الرابط، فـ `sara` لو عرفت الرقم تلغي طلب `ahmed`. دي (IDOR) أو (Broken Object Level Authorization)، رقم 1 في OWASP API.

| العملية | مين مسموحله | لو مش مسموحله |
| --- | --- | --- |
| `Confirm` | الأدمن (مؤقتًا لحد الـ Saga) | 403 |
| `Cancel` | صاحب الطلب أو الأدمن | 404 (نفس رد الطلب اللي مش موجود) |

```csharp
var order = await orderRepository.GetByIdAsync(new OrderId(id), cancellationToken);

// someone else's order looks exactly like a missing one
if (order is null || (ownerOrAdmin && !CanAccess(order)))
    return OrderNotFound(id);

private bool CanAccess(Order order) =>
    User.IsInRole(AppRoles.Admin) || order.BuyerId == User.GetRequiredUserId();
```

- `ownerOrAdmin`: `Cancel` بيبعته `true`، و `Confirm` مش محتاجه لأن الـ (Attribute) ضمن إنه أدمن.
- 404 مش 403 لـ `sara`: عشان متعرفش إن الطلب موجود. والرد لازم يبقى متطابق (`OrderNotFound` في مكان واحد).
- طبقتين: الصلاحية في الكونترولر، وقاعدة البيزنس (مينفعش تأكيد طلب ملغي = 409) في `Order`.
- ده (Resource-based Authorization). لما القواعد تكتر: `IAuthorizationService` مع (Authorization Handler).

| التجربة | النتيجة |
| --- | --- |
| `sara` تلغي طلب `ahmed` | 404 |
| `sara` تلغي رقم مش موجود | نفس الـ 404 بالظبط |
| `ahmed` يأكّد طلبه | 403 |
| الأدمن يأكّد طلب `ahmed` | 204 و `Confirmed` |
| `ahmed` يلغي طلبه | 204 و `Cancelled` |
| الأدمن يأكّد طلب ملغي | 409 (الصلاحية عدّت، والـ Domain رفض) |

## أسئلتك وإجاباتها

كل الأسئلة اللي سألتها أثناء الموديول، متقسمة حسب الموضوع.

### المفاتيح والتوقيع

| السؤال | الإجابة |
| --- | --- |
| فاهم الـ (Monolith)، بس النظام الجديد مش واضح | نفس فكرتك + تغييرين: الـ `login` اتنقل لـ (Keycloak)، والـ `Secret` اتقسم لمفتاح خاص يوقّع وعام يتأكد. الـ (JWKS) مجرد طريقة توصيل للمفتاح العام بدل `appsettings.json`. (وتصحيح: الـ Refresh Token نص عشوائي مش موقّع، والسيرفر هو اللي بيعمل التوكن الجديد) |
| الخدمة بتجيب المفتاح العام منين؟ | من الدليل (Discovery) تعرف `jwks_uri`، ومنه تجيب المفاتيح وتختار بالـ `kid`. مع أول طلب فيه توكن، مش وقت الـ Startup |
| رابط `MetadataAddress` ده بتاع المفاتيح؟ | لأ، ده الدليل. منه بياخد `issuer` (يقارنه بالـ `iss`) و `jwks_uri` (يجيب منه المفاتيح) |
| مفتاح عام قديم يتأكد من توكن جديد؟ | لأ، ولا العكس. كل مفتاح عام مربوط بمفتاح خاص واحد، وعشان كده فيه `kid` |
| ليه التوقيع الحقيقي بتاع `ahmed` اترفض على بيانات `sara`؟ | التوقيع محسوب على البيانات بالظبط، فمينفعش يتنقل لبيانات تانية |

### (Keycloak) و Docker

| السؤال | الإجابة |
| --- | --- |
| `8443/tcp` و `9000/tcp` ظاهرين ليه؟ | `EXPOSE` في الصورة بس. اللي فيه سهم `->` هو المفتوح على جهازك |
| الداتا فين فعلًا؟ | قاعدة (H2) ملفات في `/opt/keycloak/data/h2`. الـ volume متركّب مكان الفولدر، فالداتا نفسها على جهازك، مش نسخة احتياطية |
| الـ volume في التطوير بس؟ | أيوه. في الإنتاج قاعدة حقيقية برا (Postgres) عليها (Backup) |
| لو مسحنا الـ volume؟ | مفاتيح جديدة (`kid` جديد)، والـ (Realm) والمستخدمين اتمسحوا. التوكن القديم يعدّي بس طول ما الخدمة حافظة المفتاح القديم |
| مين الـ (Client)؟ | تطبيق متسجّل في (Keycloak) (الواجهة)، مش العميل اللي بيشتري. Ordering و Basket مش Clients، هم بيتأكدوا بس |
| الـ Audience mapper: أختار `ecommerce-web` في Included Client Audience؟ | لأ، شيله. التوكن معمول للـ API (`ecommerce-api` في Custom Audience) |
| قاعدة الباسورد: أشيلها ولا أحطها؟ | نفس Regex الـ (Monolith)، وبتشتغل على الباسوردات الجديدة وعلى الاستيراد كمان |
| تجربة التجديد (Refresh)؟ | `grant_type=refresh_token` بيرجّع زوج جديد. القديم شغال افتراضيًا، و **Revoke Refresh Token** بيخليه مرة واحدة (زي الـ Monolith) |
| ضفت مستخدم في الملف وعملت `restart`؟ | مش هيظهر. الاستيراد بيتخطّى لو الـ (Realm) موجود. امسح الـ volume (من غير `--build`) |

### الـ `iss` والعناوين

| السؤال | الإجابة |
| --- | --- |
| Ordering بيجيب التوكن؟ | لأ، التوكن بيجيله من المستخدم. Ordering بيجيب الدليل، والدليل فيه `issuer` |
| الـ `issuer` و `jwks_uri` هيبقوا 8080 ولا 8180؟ | `issuer` دايمًا 8180. `jwks_uri` حسب السائل: جهازك 8180، و Ordering 8080 |
| `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` بيحل إيه؟ | `KC_HOSTNAME` خلّى كل الروابط 8180، ومنها `jwks_uri` اللي Ordering ميقدرش يوصله. التاني بيرجّع روابط السيرفرات بالعنوان اللي الطلب جه عليه |
| Ordering يكلّم (Keycloak) على `localhost:8180`؟ | مينفعش. `localhost` جوه كونتينر Ordering = Ordering نفسه، و 8180 باب على جهازك بس |
| `Auth__MetadataAddress` أحطه فين؟ | في `environment` بتاع الخدمة، وبعدين اتنقل لـ `x-api-env` |

### الصلاحيات والـ IDOR

| السؤال | الإجابة |
| --- | --- |
| لو `logout-all` أخد `userId` من الـ Body؟ | حتى مع `[Authorize]`، أي مستخدم يقفل جلسات غيره. `[Authorize]` = "مسجّل دخول؟"، مش "نفس الشخص؟" |
| `sara` تلغي طلب `ahmed`: 401 ولا 403؟ | مش 401، هي معاها توكن سليم. بـ `[Authorize]` بس = 204 (اتلغى!)، ومع فحص المالك = 403 أو 404 |
| هي مش شايفة `ahmed` أصلًا؟ | `Cancel` بياخد رقم الطلب من الرابط، مش `buyerId`. لو عرفت الرقم (لينك، لوج، سكرين شوت) تلغيه |
| `bool ownerOrAdmin = false` لزمته إيه؟ | `ExecuteAsync` مشتركة. `Cancel` محتاج فحص المالك، و `Confirm` محمي بالـ Attribute. و `= false` عشان `Confirm` ميتغيّرش |
| الأدمن بتوكن قديم بعد الـ Mapper؟ | 403 لحد ما التوكن يخلص. أول تجديد بالـ Refresh Token يجيب توكن فيه `roles`، من غير تسجيل دخول |

### الـ (BFF) والبوابة والرسائل

| السؤال | الإجابة |
| --- | --- |
| `accessor.HttpContext` ده طلب مين؟ | الطلب اللي وصل للـ (BFF). البوابة ساعي بريد عدّى الهيدر زي ما هو |
| نبعت التوكن لـ Catalog "احتياطي"؟ | مش هيقفل التصفح، بس بيزوّد أماكن تسريب التوكن. توكن متسرّب من Catalog يتقبل في Ordering (نفس الـ aud). (Least Privilege) |
| الـ BFF ياخد توكن خاص بيه إمتى؟ | (Client Credentials) لما الشغل مش باسم مستخدم. والـ (DelegatingHandler) للطلبات الطالعة مش (Middleware) |
| الخطوة 4 (`AuthorizationPolicy`) مش واضحة | هي `Route::middleware('auth')` في Laravel: الطلب من غير توكن يترفض من الباب بدل ما يوصل للخدمة |
| نشيل `[Authorize]` من Ordering لأن البوابة بتقفل؟ | لأ: منثقش في البوابة 100%، والخدمات الداخلية بتكلّمه مباشرة، و `GetRequiredUserId` هترمي 500 |
| السلال القديمة اللي `UserName` بتاعها `"ahmed"`؟ | يتيمة. في نظام حقيقي بنعمل (Data Migration) يربط الـ ID القديم بالـ `sub` |
| Ordering يتأكد من توكن في الـ Event؟ | لأ: الرسالة من جوه، والتوكن هيخلص قبل الـ (Retry)، وهيتخزن في الـ Outbox والـ Inbox و RabbitMQ |

## المشاكل اللي قابلتها وحلولها

أغلب المشاكل كانت في تركيب الملفات في Docker، واللوج كان بيقول السبب بالظبط.

| المشكلة | العَرَض | السبب | الحل |
| --- | --- | --- | --- |
| الملف اتركّب فولدر | `Failed to run import: .../ecommerce-realm.json (Is a directory)` | الملف كان لسه اسمه `realm-export.json`، و Docker لما ملقاش المسار عمل فولدر فاضي بالاسم ده | `ls -la keycloak/` (أول السطر `d` = فولدر)، ثم `sudo rm -r` للفولدر، ثم `mv` الملف بالاسم الصح. قبل أي مسح اتأكد إن ملفك مش جوه الفولدر |
| الفولدر اتحفظ جوه الـ volume | `OCI runtime create failed ... not a directory: Are you trying to mount a directory onto a file` | نفس الفولدر اتعمل كمان جوه `data/import` في volume `keycloak-data` | `docker compose rm -sf keycloak` و `docker volume rm ecommerce_keycloak-data` و `up` |
| (Restart loop) وقت الاستيراد | `health: starting` ما بيخلصش، واللوج فيه `invalidPasswordRegexPatternMessage` | باسوردات الملف `123456789` أرقام بس، وقاعدة الباسورد (حرف + رقم) بتتطبق على الاستيراد كمان | `docker compose stop keycloak`، و `grep` على `passwordPolicy` و `"value"`، وتغييرهم لـ `Ahmed1234` و `Sara12345` و `Admin1234` بـ `sed` |
| توكن سليم اترفض (متوقع) | 401 و `IDX10205` | `iss` بـ `localhost:8180` والدليل من جوه Docker بـ `keycloak:8080` | `KC_HOSTNAME` + `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` |
| الأدمن خد 403 (متوقع) | `[Authorize(Roles = "admin")]` رفض `shop-admin` | الأدوار متداخلة جوه `realm_access` | (Mapper) `roles` + `RoleClaimType = "roles"` + توكن جديد |
| "طلباتي" بترجّع 503 (متوقع) | الـ BFF بيرد 503 | Ordering بقى عليه `[Authorize]` والـ BFF بيكلّمه من غير توكن | (Token Relay) في 10.4 |

**تحذير مهم:** متعملش `docker compose down -v` عشان تمسح volume الـ (Keycloak). دي بتمسح volumes قواعد SQL Server كمان.

**الدرس الأهم:** لما خدمة متقومش، `docker compose logs <svc> | grep -i error`، ولو فيه 401 غريب `grep -i "bearer\|IDX"`.

## الملخص و ADR-007

النظام بقى عنده مزود هوية واحد، وكل طلب من برا بيتأكد منه في البوابة وفي الخدمة، والملكية بتتفحص في الكود.

### رحلة طلب "طلباتي"

| الخطوة | مين | اللي بيحصل |
| --- | --- | --- |
| 1 | (Keycloak) | `ahmed` سجّل دخول في صفحة (Keycloak) وخد توكن موقّع بالمفتاح الخاص. |
| 2 | البوابة | CORS ثم التأكد من التوكن ثم العدّاد بالـ `sub` ثم `AuthorizationPolicy`. |
| 3 | الـ (BFF) | اتأكد من التوكن، وعدّاه لـ Ordering بس. |
| 4 | Ordering | اتأكد، وخد `sub`، ورجّع طلبات `ahmed` بس. |
| 5 | Catalog | اتطلب من غير توكن (داتا عامة). |
| 6 | الـ (BFF) | دمج الاتنين ورجّع رد واحد. |

ولا خدمة كلّمت (Keycloak) في الرحلة دي، كلهم اتأكدوا بالمفاتيح اللي في الكاش.

### نقط لازم تفتكرها

1. مكان واحد يطبع التوكن (مفتاح خاص)، والباقي يتأكد بالمفتاح العام.
2. "إنت مين" من التوكن بس. أي (Parameter) فيه هوية جاي من العميل = ثغرة، حتى مع `[Authorize]`.
3. مش عارفينك = 401، وعارفينك وممنوع = 403، وحاجة غيرك = 404.
4. البوابة والخدمة الاتنين بيتأكدوا (Defense in Depth)، والتوكن يروح بس للي محتاجه.
5. التوكن صورة من لحظة ما اتعمل: أي تغيير في الأدوار يوصل مع التوكن الجاي، وعشان كده عمره قصير.

### قرارات ADR-007

| # | القرار |
| --- | --- |
| 1 | (Keycloak) 26.8 (Apache 2.0) مزود الهوية الوحيد، والتوكنات RS256 |
| 2 | `ecommerce` Realm، و `ecommerce-web` (Public Client)، و Direct access grants للتطوير بس |
| 3 | (Audience) واحد `ecommerce-api` لكل الـ API |
| 4 | الـ (Realm) كملف `keycloak/ecommerce-realm.json` + `--import-realm`، ومستخدمين التجربة للتطوير بس |
| 5 | `KC_HOSTNAME` + `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` لـ `issuer` واحد |
| 6 | `BuildingBlocks.Authentication`: `MetadataAddress` و `MapInboundClaims = false` و `RoleClaimType = "roles"`، و `RequireHttpsMetadata` من الإعدادات (الافتراضي `true`) |
| 7 | هوية المستخدم = `sub`، و `BuyerId` في Basket و Ordering |
| 8 | البوابة بتتأكد، و `AuthorizationPolicy: default` على الـ Routes المحمية، والعدّاد بـ `user:sub` أو `ip:` |
| 9 | الـ (BFF) بيعمل (Token Relay) لـ Ordering بس |
| 10 | الأدوار بـ (Mapper) في (Keycloak)، و `AppRoles`، و `PUT /price` و `Confirm` للأدمن |
| 11 | `Cancel` لصاحب الطلب أو الأدمن، و 404 لأي حد تاني |
| 12 | الرسائل الداخلية مفيهاش توكنات، والثقة في الناشر |
| 13 | التوكن 5 دقايق: سحب صلاحية بياخد لحد 5 دقايق (مقايضة مقبولة بدل Introspection) |

الملف الكامل في `docs/adr/ADR-007-identity-keycloak-jwt.md`، وفيه البدائل اللي اترفضت والعواقب.

### ثغرات لسه مفتوحة

| الثغرة | هتتقفل في |
| --- | --- |
| (Keycloak) نسخة واحدة بـ `start-dev` و H2 | الموديول 15 (`start` + Postgres + HTTPS + أكتر من نسخة) |
| الواجهة الحقيقية (Authorization Code + PKCE) مش معمولة، والتجارب بـ Direct access grants | لما نبني واجهة |
| مفيش Idempotency Key للـ Checkout | لسه متحددش |
| التأكيد بالأدمن مؤقت | الموديول 11 (Saga) |
| عدّادات الـ Rate Limiting في الذاكرة (مش موزعة) | لما نحتاجها |

## اختبر نفسك

جاوب في دماغك الأول، وبعدين قارن بالإجابة. الأسئلة السبعة الأولى هي اختبار 10.6 (نتيجتك: 3 كاملين و 2 نص).

### أسئلة مراجعة

| السؤال | الإجابة |
| --- | --- |
| 1. (Keycloak) وقع 10 دقايق: إيه شغال وإيه واقف؟ | التوكنات الموجودة شغالة (المفاتيح في الكاش). الدخول والتسجيل و**التجديد** واقفين، فبعد 5 دقايق الناس تطلع تدريجيًا. وأي خدمة تعمل Restart ترفض كل التوكنات |
| 2. `UseAuthorization` قبل `UseAuthentication`؟ | 401 لكل الطلبات المحمية حتى بتوكن سليم: `User` فاضي = مش عارفينك |
| 3. `RemoveItem` بياخد `[FromQuery] string buyerId`؟ | (IDOR): `sara` تمسح من سلة `ahmed`. الحل `User.GetRequiredUserId()` وشيل الـ Parameter |
| 4. شلنا `KC_HOSTNAME` والـ Backchannel؟ | 401 و `IDX10205`: `iss` في التوكن 8180 والدليل من جوه Docker 8080 |
| 5. `payment-route` من غير `AuthorizationPolicy`؟ | آمن (Payment بيتأكد)، بس خسرنا الرفض البدري والاتساق. والـ `GlobalLimiter` لسه شغال عليه |
| 6. شلنا `admin` و `shop-admin` غيّر سعر بعد دقيقتين؟ | مش Bug: توكنه القديم صالح لحد ما يخلص. مقايضة الـ JWT، والحل عمر قصير (أو Introspection بتمن عالي) |
| 7. نحط التوكن في `BasketCheckedOutIntegrationEvent`؟ | لأ: هيخلص قبل الـ `ScheduleRetry` والـ Replay، وهيتخزن في الـ Outbox والـ Inbox و RabbitMQ (تسريب) |
| 8. ليه `ecommerce-web` مالوش سر؟ | كود في المتصفح، والحماية بـ Redirect URIs و (PKCE) |
| 9. ليه `ClientKey` بيستنى `IsAuthenticated`؟ | عشان `sub` من توكن مزوّر ميعملش عدّاد جديد في كل طلب |
| 10. ليه 404 مش 403 لـ `sara` في `Cancel`؟ | عشان متعرفش إن الطلب موجود، والرد متطابق مع الطلب اللي مش موجود |

### أسئلة إنترفيو

| السؤال | إجابة نموذجية |
| --- | --- |
| ليه RS256 مش HS256 في الـ Microservices؟ | في HS256 نفس الـ Secret بيوقّع وبيتأكد، فكل خدمة تقدر تطبع توكنات. في RS256 المفتاح الخاص في مزود الهوية بس، والعام منشور على JWKS، فالـ Key Rotation من غير تعديل أي خدمة |
| الفرق بين Authentication و Authorization؟ | الأول التأكد من الهوية (لو فشل 401)، والتاني قرار الصلاحية حسب الدور أو الملكية (لو فشل 403) |
| الخدمة اللي بتستقبل Event محتاجة توكن؟ | لأ. رقم المستخدم داتا عادية من ناشر اتأكد، والتوكن عمره أقصر من الـ Retry. بنحمي الـ Broker نفسه |
| إيه هو الـ Token Relay؟ وإمتى Client Credentials؟ | تعدية توكن المستخدم للخدمة التالية عشان تشتغل باسمه. و Client Credentials لما مفيش مستخدم (مهام خلفية، داتا عامة) |
| Rate Limiting لكل مستخدم في البوابة؟ | اتأكد من التوكن، وعدّ بالـ sub للمسجّل وبالـ IP للضيف. الغلطة: sub من توكن مش متأكد من توقيعه. ومع أكتر من بوابة العدّادات في Redis |
| إيه هي الـ IDOR؟ وإزاي تمنعها في `GET /orders/{id}`؟ | الـ API يوصل لحاجة بـ ID من العميل من غير ما يتأكد إنها بتاعته. الحل فحص الملكية بالـ sub أو الفلترة بيه في الاستعلام، و 404. والـ GUID مش حماية |
| إزاي تخلّي إعدادات Keycloak قابلة للتكرار؟ | ملف الـ Realm في Git، والقيم المختلفة من متغيرات البيئة، ومستخدمين التجربة في التطوير بس، وفي البيئات الشغالة أداة تطبّق التعديلات تدريجيًا من CI/CD |

### مهام صغيرة للتدريب

- [ ] اعمل `GET /api/orders/{id}` في Ordering، وامنع الـ IDOR بفلترة الاستعلام بالـ `sub`.
- [ ] اعمل `DELETE /api/basket/items/{productId}` في Basket بالهوية من التوكن (سؤال 3 بشكل صح).
- [ ] انقل قاعدة `CanAccess` لـ (Authorization Handler) بـ `IAuthorizationService`.
- [ ] حد أقل للضيوف من المستخدمين المسجّلين في `GlobalLimiter`.
