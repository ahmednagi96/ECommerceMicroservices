# كورس الـ Microservices على مشروع ECommerceMicroservices: الخطة والتقدم

> آخر تحديث: 2026-11-08 · مبني على `docs/PROJECT_CONTEXT.md` (فرع `build/upgrade-dotnet-10`) وعلى ملاحظات المذاكرة السابقة.
> انسخ الملف ده في الريبو كـ `docs/LEARNING_PROGRESS.md` عشان Claude Code جوه Cursor يقرأه أول كل جلسة ويحدّثه في آخرها.

---

## 1. إنت واقف فين دلوقتي (Snapshot)

**اللي خلصته فعلًا (✅):**
- تقسيم النظام لـ 3 خدمات: Catalog و Basket و Ordering، كل خدمة بطبقات Clean Architecture مبسطة (API / Infrastructure / Domain).
- الـ (Database per Service): 3 قواعد SQL Server منفصلة، ومفيش Foreign Keys بين الخدمات.
- Docker: Dockerfile متعدد المراحل (multi-stage)، و docker-compose فيه 6 كونتينرات، و YAML anchors، و healthcheck للقواعد، وحدود موارد، وشبكة bridge، والأسرار عن طريق `env_file`.
- ترقية لـ .NET 10، و (Central Package Management)، وإيقاف الـ build لو فيه ثغرات (NU1903/NU1904).
- الاتصال المتزامن (Sync HTTP) من Basket لـ Catalog بـ Typed HttpClient، و `AddStandardResilienceHandler` (Retry + Timeout + Circuit Breaker)، ورد 503/404 مظبوط، وحماية من التلاعب في السعر.
- اتعرفت على فكرة نسخ البيانات عن طريق الأحداث (Data Replication via Events) كفكرة بس، من غير تنفيذ.

**الديون التقنية الحالية (لازم تتقفل في الموديول 4):**
- Catalog و Ordering مفيهمش `LoadLocalDotEnv()`، فـ `dotnet run` محليًا مش شغال.
- `CATALOG_URL` مش موجود في `Basket/.env.example`.
- كيان `User` و `UsersController` متكررين في التلات خدمات (مكانهم خدمة Identity).
- ملفات `Class1.cs` فاضية، و `Basket.API/Properties/Entities/User.cs` في مكان غلط.
- فشل الـ Migration بيتسجل في الـ log والخدمة بتكمل عادي (المفروض تقف: fail fast).
- مفيش Health endpoints للـ APIs، ولا Authentication، ولا Gateway، ولا Broker، ولا Tests، ولا CI.

**إنت في:** نهاية **المرحلة الأولى (الأساسيات)** → **الخطوة الجاية: الموديول 4**.

---

## 2. منهجية الكورس (معايير عالمية)

- **ترتيب بالمشكلة مش بالأداة**: كل موديول بيبدأ بمشكلة حقيقية في المشروع، وبعدين النمط (Pattern) اللي بيحلها، وبعدين الأداة. ده نفس أسلوب كتاب Chris Richardson وموقع microservices.io (Pattern Language).
- **كل درس بنفس الشكل**: المشكلة (Problem) → الفكرة (Concept) + رسمة Mermaid → الكود في المشروع → المقايضات (Trade-offs) → إمتى ما تستخدمهاش → مهمة صغيرة → سؤالين مراجعة.
- **تعريف الإنجاز (Definition of Done)** لكل موديول: كود شغال بـ `docker compose up`، وملف ملاحظات في `docs/notes/NN-topic.md`، وقرار معماري واحد على الأقل مكتوب كـ ADR في `docs/adr/` (أسلوب Michael Nygard اللي الشركات شغالة بيه).
- **مستويات الفهم (Bloom)**: تفهم ← تطبق ← تحلل المقايضات ← تشرحها في إنترفيو System Design.

---

## 3. الخريطة الكاملة (17 موديول في 5 مراحل)

### المرحلة الأولى: الأساسيات ✅
| # | الموديول | الحالة |
|---|---|---|
| 0 | ليه Microservices وإمتى لأ، والـ (Bounded Contexts)، وتقسيم الخدمات | ✅ |
| 1 | Docker و Docker Compose | ✅ |
| 2 | الـ (Database per Service) و EF Core Migrations و CPM | ✅ |
| 3 | الاتصال المتزامن (Sync HTTP) والـ (Resilience) | ✅ (ناقص gRPC، مؤجل للموديول 12) |

### المرحلة الثانية: خدمة جاهزة للإنتاج ومجال غني
**4. ⏭️ تقوية الأساس: (Microservice Chassis) و (Production Baseline)**
- المشكلة: كل خدمة بتعيد كتابة نفس الإعدادات، والـ compose بيعتبر الـ API جاهز وهو ممكن يكون واقع، والأخطاء بترجع بشكل غير موحد.
- المفاهيم: Health Check API (`/health/live` و `/health/ready`)، والـ (Options Pattern) مع `ValidateOnStart`، والإعدادات على طريقة (12-Factor App)، والـ (ProblemDetails) مع `IExceptionHandler`، والـ (Structured Logging) بـ Serilog، والـ (Correlation ID)، والـ (Fail Fast).
- التطبيق: مشروع `BuildingBlocks` أو `ServiceDefaults` مشترك، و healthcheck للـ APIs في compose، وقفل كل الديون التقنية اللي فوق.
- المراجع: microservices.io (Microservice chassis, Health Check API)، و Microsoft eBook ".NET Microservices Architecture" (فصل Health monitoring)، و 12factor.net.

**5. الـ Ordering بـ DDD (Tactical Design)**
- المشكلة: خدمة Ordering فاضية، والطلب له قواعد عمل (Invariants) لازم تتحمي.
- المفاهيم: Aggregate و Entity و Value Object (Money, Address) و Domain Events و Strongly-typed IDs، والفرق بين Rich و Anemic Model، والـ Repository.
- التطبيق: `Order` و `OrderItem` و `OrderStatus` مع اختبارات وحدة (Unit Tests) للدومين.
- المراجع: Vlad Khononov "Learning Domain-Driven Design"، و Ordering.Domain في ريبو `dotnet/eShop`، وفصل DDD في Microsoft eBook.

### المرحلة الثالثة: الاتصال غير المتزامن والبيانات الموزعة
**6. أساسيات الرسائل: RabbitMQ و Events**
- المشكلة: لما العميل يعمل Checkout، إزاي Ordering تعرف من غير ما Basket تستنى؟
- المفاهيم: Exchange و Queue و Binding و Routing Key و Ack و Prefetch و DLQ، والفرق بين Event و Command، و Pub/Sub مقابل Point-to-Point.
- التطبيق: demo صغير بـ `RabbitMQ.Client` عشان تفهم اللي تحت، وبعدين مكتبة: **MassTransit v8** (Apache 2.0، مثبتة على 8.x) أو **Wolverine** (MIT). وبعدها حدث `BasketCheckoutEvent` من Basket لـ Ordering، ومشروع `Contracts` مشترك.
- المراجع: RabbitMQ Tutorials الرسمية، وكتاب Hohpe & Woolf "Enterprise Integration Patterns"، و microservices.io (Messaging, Domain event).

**7. الرسائل الموثوقة (Reliable Messaging)**
- المشكلة: لو حفظت الطلب والـ broker وقع قبل ما الرسالة تتبعت (Dual-write problem)؟ ولو الرسالة وصلت مرتين؟
- المفاهيم: Transactional Outbox، و Inbox / Idempotent Consumer، وضمان At-least-once، و Retry و Redelivery، و DLQ، وترتيب الرسائل.
- المراجع: microservices.io (Transactional outbox, Idempotent consumer)، و Martin Kleppmann "Designing Data-Intensive Applications".

**8. البيانات عبر الخدمات والكاش**
- المشكلة: Basket بتسأل Catalog كل مرة. ولو Catalog وقعت؟
- المفاهيم: Data Replication via Events (`ProductPriceChanged` → نسخة محلية في Basket)، و CQRS Read Models، و API Composition، و Eventual Consistency وتأثيرها على تجربة المستخدم (UX)، و Redis كـ (Cache-aside) أو كمخزن للسلة (Polyglot Persistence).
- المراجع: microservices.io (CQRS, API composition)، و Sam Newman "Building Microservices" 2nd ed.

### المرحلة الرابعة: الحافة والأمان وسير العمل
**9. الـ API Gateway بـ YARP**: Routing و Transforms و Rate Limiting و CORS، والـ Gateway مقابل الـ BFF، والـ Aggregation. (microservices.io: API gateway / BFF)

**10. الهوية والأمان (Identity & Security)**: OAuth2 و OIDC، و Keycloak في compose، والتحقق من الـ JWT في الـ Gateway والصلاحيات (Authorization) جوه كل خدمة (Defense in Depth)، و Client Credentials بين الخدمات، وإدارة الأسرار. وهنا نشيل `User` المتكرر نهائيًا. (OWASP API Security Top 10)

**11. الـ Saga**: Checkout → Payment → Inventory، و Choreography مقابل Orchestration، والتعويض (Compensation)، و State Machine. ومعاها نضيف خدمتي Payment و Inventory. (Chris Richardson "Microservices Patterns"، فصل Sagas)

**12. الـ gRPC**: خدمة Discount، و `.proto` كعقد، و HTTP/2 جوه الكونتينرات، و Deadlines، وإمتى gRPC وإمتى REST.

### المرحلة الخامسة: التشغيل والجودة
**13. المراقبة (Observability) و .NET Aspire**: OpenTelemetry (Traces و Metrics و Logs)، وانتقال الـ Trace عبر HTTP و RabbitMQ، و Jaeger و Grafana و Seq، والـ (Four Golden Signals) و SLO، و Aspire AppHost كبيئة تطوير. (Google SRE Book)

**14. الاختبارات**: هرم الاختبارات (Test Pyramid) في الـ Microservices، و `WebApplicationFactory` مع Testcontainers، و Contract Testing بـ Pact. (Martin Fowler / Toby Clemson: "Testing Strategies in a Microservice Architecture")

**15. الـ CI/CD والنشر**: GitHub Actions لكل خدمة بـ path filters، والصور على GHCR، والنشر على VPS بـ compose، وبعدين أساسيات Kubernetes (Deployments و Services و Probes و ConfigMaps و Secrets). (مع skill الـ cicd-mentor)

**16. موضوعات متقدمة ومشروع التخرج (Capstone)**: API و Event Versioning، و Strangler Fig، والأنماط الغلط (Distributed Monolith)، والـ Scaling. وفي الآخر Mock Interview لـ System Design على Checkout المتجر، ومستند معماري كامل للمشروع.

---

## 4. المراجع العالمية المعتمدة
| النوع | المرجع | بنستخدمه في |
|---|---|---|
| كتاب | Sam Newman — Building Microservices (2nd ed) و Monolith to Microservices | الحدود، والتواصل، والنشر |
| كتاب + موقع | Chris Richardson — Microservices Patterns و microservices.io | الأنماط كلها (Saga, Outbox, CQRS, Gateway) |
| رسمي Microsoft | .NET Microservices Architecture eBook، وريبو `dotnet/eShop`، وتوثيق .NET Aspire | التطبيق على .NET |
| كتاب | Vlad Khononov — Learning DDD | الموديول 5 |
| كتاب | Martin Kleppmann — DDIA | البيانات والاتساق |
| كتاب | Hohpe & Woolf — Enterprise Integration Patterns | الرسائل |
| كتاب | Google — Site Reliability Engineering | المراقبة |
| مقالات | martinfowler.com (Microservices, Testing Strategies) | المفاهيم والاختبارات |
| عملي (اختياري) | كورس Mehmet Ozkaya على Udemy، ومحتوى Milan Jovanović | أمثلة .NET إضافية |

---

## 5. متتبع التقدم (Progress Tracker)
| # | الموديول | الحالة | ملف الملاحظات | الـ ADR |
|---|---|---|---|---|
| 0–3 | الأساسيات | ✅ | — | — |
| 4 | Chassis و Production Baseline | ⏭️ التالي | `docs/notes/04-service-baseline.md` | ADR-001: مكان الإعدادات المشتركة |
| 5 | Ordering DDD | ⬜ | | |
| 6 | RabbitMQ و Events | ⬜ | | ADR: MassTransit v8 أو Wolverine |
| 7 | Outbox / Inbox | ⬜ | | |
| 8 | Data Replication و Redis | ⬜ | | |
| 9 | YARP Gateway | ⬜ | | |
| 10 | Identity | ⬜ | | |
| 11 | Saga | ⬜ | | |
| 12 | gRPC | ⬜ | | |
| 13 | Observability و Aspire | ⬜ | | |
| 14 | Testing | ⬜ | | |
| 15 | CI/CD و K8s | ⬜ | | |
| 16 | Advanced و Capstone | ⬜ | | |

**قاعدة التحديث:** في آخر كل جلسة نغيّر الحالة، ونكتب سطر "إيه اللي اتعمل" وسطر "إيه اللي جاي".