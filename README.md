# eShop Microservices

.NET 8 ile geliştirilmiş, gerçek dünya senaryolarını kapsayan tam teşekküllü bir e-ticaret mikroservis mimarisi. Proje; CQRS, DDD, Clean Architecture, Vertical Slice Architecture ve Event-Driven Architecture gibi modern yazılım mimarisi kalıplarını bir arada kullanmaktadır.

---

## Mimari Genel Bakış

```
┌─────────────────────────────────────────────────────────────┐
│                     Shopping.Web (Razor Pages)               │
│                        :6005 / :6065                         │
└───────────────────────────┬─────────────────────────────────┘
                            │ HTTP (Refit)
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                   YARP API Gateway                           │
│                     :6004 / :6064                            │
│           Rate Limiting  •  Reverse Proxy                    │
└──────┬────────────────────┬───────────────────┬─────────────┘
       │ /catalog-service   │ /basket-service   │ /ordering-service
       ▼                    ▼                   ▼
┌────────────┐    ┌──────────────────┐   ┌──────────────────┐
│ Catalog    │    │   Basket.API     │   │  Ordering.API    │
│   .API     │    │  :6001 / :6061   │   │  :6003 / :6063   │
│ :6000/6060 │    └────────┬─────────┘   └────────┬─────────┘
└─────┬──────┘             │ gRPC                  │ MassTransit
      │                    ▼                       │ (Consumer)
      │           ┌─────────────────┐              │
      │           │ Discount.Grpc   │              │
      │           │  :6002 / :6062  │              │
      │           └─────────────────┘              │
      │                                            │
      ▼              ┌──────────────┐              ▼
┌──────────┐         │   RabbitMQ   │◄────────────►│
│PostgreSQL│         │  :5672/15672 │         (Pub/Sub)
│ CatalogDb│         └──────────────┘
│  :6161   │
└──────────┘  ┌──────────┐  ┌──────────┐  ┌──────────┐
              │PostgreSQL│  │  Redis   │  │SQL Server│
              │ BasketDb │  │  :6379   │  │ OrderDb  │
              │  :6162   │  │  Cache   │  │  :1431   │
              └──────────┘  └──────────┘  └──────────┘
```

---

## Servisler

### 1. Catalog.API — Ürün Kataloğu Servisi
**Port:** `6000` (HTTP) / `6060` (HTTPS)

Ürün yönetimi için tam CRUD işlemleri sağlar. Vertical Slice Architecture ile organize edilmiştir — her özellik (GetProducts, CreateProduct, vb.) kendi handler, endpoint ve validator dosyalarını içerir.

| Endpoint | Metot | Açıklama |
|---|---|---|
| `/products` | GET | Sayfalı ürün listesi |
| `/products/{id}` | GET | ID ile ürün getir |
| `/products/category/{category}` | GET | Kategoriye göre ürünler |
| `/products` | POST | Yeni ürün oluştur |
| `/products` | PUT | Ürün güncelle |
| `/products/{id}` | DELETE | Ürün sil |

**Teknolojiler:** Marten (PostgreSQL üzerinde document DB), Carter, MediatR, FluentValidation

---

### 2. Basket.API — Sepet Servisi
**Port:** `6001` (HTTP) / `6061` (HTTPS)

Kullanıcı alışveriş sepeti yönetimi sağlar. Sepet ödemesi (checkout) sırasında RabbitMQ üzerinden Ordering servisine event yayınlar. Ayrıca Discount.Grpc servisini çağırarak ürün indirimlerini gerçek zamanlı uygular.

| Endpoint | Metot | Açıklama |
|---|---|---|
| `/basket/{userName}` | GET | Kullanıcı sepetini getir |
| `/basket` | POST | Sepet oluştur / güncelle |
| `/basket/{userName}` | DELETE | Sepeti sil |
| `/basket/checkout` | POST | Sepeti öde (checkout) |

**Teknolojiler:** Marten, Redis (dağıtık önbellek), gRPC Client, MassTransit, Carter, MediatR, Scrutor (Decorator DI)

---

### 3. Discount.Grpc — İndirim Servisi
**Port:** `6002` (HTTP) / `6062` (HTTPS)

Ürün adı bazlı kupon/indirim yönetimi sağlar. Yalnızca gRPC protokolü üzerinden iletişim kurar — HTTP API'si yoktur. Basket servisi tarafından sepet hesaplanırken çağrılır.

**Operasyonlar:** `GetDiscount`, `CreateDiscount`, `UpdateDiscount`, `DeleteDiscount`

**Teknolojiler:** gRPC (Protobuf), Entity Framework Core, SQLite, Mapster

---

### 4. Ordering — Sipariş Servisi
**Port:** `6003` (HTTP) / `6063` (HTTPS)

Clean Architecture ve DDD prensipleri ile dört katmana ayrılmıştır:

- **Ordering.Domain** — Aggregate Root'lar, Entity'ler, Value Object'ler, Domain Event'ler
- **Ordering.Application** — CQRS Command/Query handler'ları, Integration Event tüketicileri
- **Ordering.Infrastructure** — EF Core + SQL Server, EF Interceptor'lar
- **Ordering.API** — Minimal API endpoint'leri, Feature Management

| Endpoint | Metot | Açıklama |
|---|---|---|
| `/orders` | GET | Sayfalı sipariş listesi |
| `/orders/{id}` | POST | Sipariş oluştur |
| `/orders` | PUT | Sipariş güncelle |
| `/orders/{id}` | DELETE | Sipariş sil |
| `/orders/customer/{customerId}` | GET | Müşteriye göre siparişler |
| `/orders/name/{name}` | GET | İsme göre sipariş ara |

**Teknolojiler:** EF Core + SQL Server, MassTransit (Consumer), MediatR, FluentValidation, Microsoft.FeatureManagement

---

### 5. YARP API Gateway
**Port:** `6004` (HTTP) / `6064` (HTTPS)

Tüm dış istekler bu gateway üzerinden geçer. Route tabanlı yönlendirme ve Ordering servisi için sabit pencere hız sınırlama (rate limiting) uygular.

**Yönlendirme Kuralları:**
| Gelen Path | Yönlendirilen Servis |
|---|---|
| `/catalog-service/{**}` | `catalog.api:8080` |
| `/basket-service/{**}` | `basket.api:8080` |
| `/ordering-service/{**}` | `ordering.api:8080` |

**Rate Limiter (Ordering):** 10 saniye pencerede maksimum 5 istek

---

### 6. Shopping.Web — Web Uygulaması
**Port:** `6005` (HTTP) / `6065` (HTTPS)

ASP.NET Core Razor Pages tabanlı e-ticaret arayüzü. Tüm API çağrılarını YARP Gateway üzerinden yapar.

**Sayfalar:** Ana Sayfa, Ürün Listesi, Ürün Detayı, Sepet, Ödeme, Sipariş Onayı, Sipariş Listesi

**Teknolojiler:** Razor Pages, Refit (type-safe HTTP client)

---

## Tasarım Kalıpları

### Mimari Kalıplar

| Kalıp | Nerede Kullanıldı |
|---|---|
| **Microservices Architecture** | Tüm proje |
| **API Gateway Pattern** | YARP ApiGateway |
| **Clean Architecture** | Ordering servisi (4 katman) |
| **Vertical Slice Architecture** | Catalog.API, Basket.API |
| **Event-Driven Architecture** | Basket → RabbitMQ → Ordering |

### Yazılım Tasarım Kalıpları

| Kalıp | Nerede Kullanıldı | Açıklama |
|---|---|---|
| **CQRS** | Tüm servisler | Command ve Query sorumlulukları ayrı handler'larda |
| **Mediator** | Tüm servisler | MediatR ile handler'lar birbirinden bağımsız |
| **Decorator** | Basket.API | `CachedBasketRepository` → `BasketRepository`'yi sarar |
| **Repository** | Basket.API, Ordering | `IBasketRepository`, `IApplicationDbContext` |
| **Domain Events** | Ordering.Domain | `OrderCreatedEvent`, `OrderUpdatedEvent` |
| **Aggregate Root** | Ordering.Domain | `Order : Aggregate<OrderId>` |
| **Value Object** | Ordering.Domain | `Address`, `Payment`, `OrderName` (C# record) |
| **Factory Method** | Ordering.Domain | `Order.Create()`, `Address.Of()`, `Payment.Of()` |
| **Pipeline Behavior** | Tüm servisler | `ValidationBehavior`, `LoggingBehavior` MediatR pipeline'ında |
| **Interceptor** | Ordering.Infrastructure | `AuditableEntityInterceptor`, `DispatchDomainEventsInterceptor` |
| **Publish/Subscribe** | Basket → Ordering | MassTransit + RabbitMQ üzerinden `BasketCheckoutEvent` |

### CQRS Akışı (Örnek: Sepet Ödeme)

```
HTTP POST /basket/checkout
    └─► CheckoutBasketEndpoint (Carter)
            └─► ISender.Send(CheckoutBasketCommand)           [MediatR]
                    └─► ValidationBehavior<,>                  [Pipeline]
                    └─► LoggingBehavior<,>                     [Pipeline]
                    └─► CheckoutBasketCommandHandler
                            ├─► IBasketRepository.GetBasket()
                            ├─► IPublishEndpoint.Publish(BasketCheckoutEvent) [RabbitMQ]
                            └─► IBasketRepository.DeleteBasket()

RabbitMQ → BasketCheckoutEventHandler (Ordering.Application)
                └─► ISender.Send(CreateOrderCommand)
                        └─► CreateOrderHandler
                                └─► Order.Create()  [Domain Factory]
                                └─► DbContext.SaveChangesAsync()
                                        ├─► AuditableEntityInterceptor   [CreatedAt/ModifiedAt]
                                        └─► DispatchDomainEventsInterceptor
                                                └─► mediator.Publish(OrderCreatedEvent)
```

---

## Teknoloji Yığını

### Backend
| Teknoloji | Versiyon | Kullanım Amacı |
|---|---|---|
| .NET / ASP.NET Core | 8.0 | Tüm servisler |
| MediatR | - | CQRS Mediator |
| Carter | 8.2.1 | Minimal API routing |
| FluentValidation | - | İstek doğrulama |
| Mapster | - | Nesne eşleme (mapping) |

### Veritabanları
| Teknoloji | Versiyon | Kullanılan Servis |
|---|---|---|
| PostgreSQL | 17 | Catalog.API, Basket.API |
| Marten | 8.x | Catalog.API, Basket.API (Document DB üstü) |
| SQL Server | 2022 | Ordering.API |
| Entity Framework Core | 8.0.26 | Ordering, Discount.Grpc |
| SQLite | - | Discount.Grpc |

### Altyapı
| Teknoloji | Kullanım Amacı |
|---|---|
| Redis | Basket servisi dağıtık önbelleği |
| RabbitMQ | Asenkron mesajlaşma (Pub/Sub) |
| MassTransit | RabbitMQ soyutlama katmanı |
| gRPC | Basket → Discount servis iletişimi |
| YARP | API Gateway / Reverse Proxy |
| Docker / Docker Compose | Konteynerizasyon |

### Web Katmanı
| Teknoloji | Kullanım Amacı |
|---|---|
| Razor Pages | Shopping.Web arayüzü |
| Refit 10.1.6 | Type-safe HTTP client |

### Cross-Cutting
| Teknoloji | Kullanım Amacı |
|---|---|
| HealthChecks (NpgSql, Redis) | Servis sağlık kontrolü |
| Microsoft.FeatureManagement | Ordering servisi feature flag'leri |
| Scrutor | Decorator pattern için DI desteği |

---

## Proje Yapısı

```
eshop-microservises/
├── BuildingBlocks/
│   ├── BuildingBlocks/
│   │   ├── CQRS/               # ICommand, IQuery, ICommandHandler, IQueryHandler
│   │   ├── Behaviors/          # ValidationBehavior, LoggingBehavior (MediatR Pipeline)
│   │   ├── Exceptions/         # BadRequestException, NotFoundException, CustomExceptionHandler
│   │   └── Pagination/         # PaginatedResult, PaginationRequest
│   └── BuildingBlocks.Messaging/
│       ├── Events/             # IntegrationEvent, BasketCheckoutEvent
│       └── MassTransit/        # AddMessageBroker extension
│
├── Services/
│   ├── Catalog/
│   │   └── Catalog.API/
│   │       ├── Products/       # Vertical slice: CreateProduct, GetProducts, vb.
│   │       ├── Models/
│   │       └── Data/
│   │
│   ├── Basket/
│   │   └── Basket.API/
│   │       ├── Basket/         # Vertical slice: GetBasket, StoreBasket, CheckoutBasket, vb.
│   │       ├── Data/           # BasketRepository + CachedBasketRepository (Decorator)
│   │       └── Models/
│   │
│   ├── Discount/
│   │   └── Discount.Grpc/
│   │       ├── Protos/         # discount.proto
│   │       ├── Services/       # DiscountService (gRPC)
│   │       └── Data/           # DiscountContext (EF Core + SQLite)
│   │
│   └── Ordering/
│       ├── Ordering.Domain/
│       │   ├── Abstractions/   # Entity, Aggregate, IAggregate, IDomainEvent
│       │   ├── Models/         # Order, OrderItem, Customer, Product
│       │   ├── ValueObject/    # Address, Payment, OrderName, OrderId, vb.
│       │   ├── Events/         # OrderCreatedEvent, OrderUpdatedEvent
│       │   └── Enums/          # OrderStatus
│       ├── Ordering.Application/
│       │   ├── Orders/
│       │   │   ├── Commands/   # CreateOrder, UpdateOrder, DeleteOrder
│       │   │   ├── Queries/    # GetOrders, GetOrdersByCustomer, GetOrdersByName
│       │   │   └── EventHandlers/
│       │   │       ├── Domain/       # OrderCreatedEventHandler, OrderUpdatedEventHandler
│       │   │       └── Integration/  # BasketCheckoutEventHandler (MassTransit Consumer)
│       │   └── Dtos/
│       ├── Ordering.Infrastructure/
│       │   └── Data/
│       │       ├── ApplicationDbContext.cs
│       │       ├── Configuration/  # EF Fluent API konfigürasyonları
│       │       └── Interceptors/   # AuditableEntityInterceptor, DispatchDomainEventsInterceptor
│       └── Ordering.API/
│           └── Endpoints/      # CreateOrder, UpdateOrder, DeleteOrder, GetOrders, vb.
│
├── ApiGateways/
│   └── YarpApiGateway/         # YARP konfigürasyonu + Rate Limiting
│
├── WebApps/
│   └── Shopping.Web/
│       ├── Pages/              # Index, ProductList, Cart, Checkout, Confirmation, vb.
│       └── Services/           # ICatalogService, IBasketService, IOrderingService (Refit)
│
├── docker-compose.yml
└── docker-compose.override.yml
```

---

## Kurulum ve Çalıştırma

### Gereksinimler
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 veya VS Code

### Docker ile Çalıştırma (Önerilen)

Tüm servisleri tek komutla ayağa kaldırmak için:

```bash
docker-compose -f docker-compose.yml -f docker-compose.override.yml up --build
```

Tüm konteynerleri durdurmak için:

```bash
docker-compose down
```

### Servis Adresleri (Docker)

| Servis | HTTP | HTTPS |
|---|---|---|
| Shopping.Web | http://localhost:6005 | https://localhost:6065 |
| YARP API Gateway | http://localhost:6004 | https://localhost:6064 |
| Catalog.API | http://localhost:6000 | https://localhost:6060 |
| Basket.API | http://localhost:6001 | https://localhost:6061 |
| Discount.Grpc | http://localhost:6002 | https://localhost:6062 |
| Ordering.API | http://localhost:6003 | https://localhost:6063 |

### Altyapı Adresleri

| Servis | Adres |
|---|---|
| RabbitMQ Management UI | http://localhost:15672 (guest/guest) |
| PostgreSQL (Catalog) | localhost:6161 |
| PostgreSQL (Basket) | localhost:6162 |
| SQL Server (Ordering) | localhost:1431 |
| Redis | localhost:6379 |

### Sağlık Kontrolü

Her servis `/health` endpoint'i üzerinden durum bilgisi sunar:

```
http://localhost:6000/health   → Catalog.API
http://localhost:6001/health   → Basket.API
http://localhost:6003/health   → Ordering.API
```

---

## Temel Akışlar

### Ürün Listeleme Akışı
```
Kullanıcı → Shopping.Web → YARP Gateway → Catalog.API → PostgreSQL (Marten)
```

### Sepete Ürün Ekleme + İndirim Uygulama
```
Kullanıcı → Shopping.Web → YARP Gateway → Basket.API
                                               ├─► gRPC → Discount.Grpc (indirim sorgula)
                                               └─► PostgreSQL + Redis (sepet kaydet/önbellekle)
```

### Sipariş Oluşturma Akışı (Asenkron)
```
Kullanıcı → Checkout → Basket.API
                           ├─► RabbitMQ: BasketCheckoutEvent yayınla
                           └─► Sepeti sil

RabbitMQ → Ordering.Application (MassTransit Consumer)
               └─► CreateOrderCommand → Order.Create() → SQL Server
                       └─► Domain Events → OrderCreatedEventHandler
```

---

## Ortam Değişkenleri

Servisler `docker-compose.override.yml` dosyasındaki ortam değişkenleri ile yapılandırılır. Yerel geliştirme için `appsettings.Development.json` dosyaları kullanılır.

| Değişken | Servis | Açıklama |
|---|---|---|
| `ConnectionStrings__Database` | Catalog, Basket, Ordering | Veritabanı bağlantı dizesi |
| `ConnectionStrings__Redis` | Basket | Redis bağlantı dizesi |
| `GrpcSettings__DiscountUrl` | Basket | Discount gRPC endpoint |
| `MessageBroker__Host` | Basket, Ordering | RabbitMQ host |
| `ApiSettings__GatewayAddress` | Shopping.Web | YARP Gateway URL |
| `FeatureManagement__OrderFullfilment` | Ordering | Sipariş karşılama feature flag |

---

## Katkıda Bulunma

1. Bu repo'yu fork'layın
2. Feature branch oluşturun (`git checkout -b feature/yeni-ozellik`)
3. Değişikliklerinizi commit edin (`git commit -m 'feat: yeni özellik eklendi'`)
4. Branch'i push edin (`git push origin feature/yeni-ozellik`)
5. Pull Request açın
