# URL Shortener — Project Plan

> **Nguồn tham khảo chính:** Sandeep Verma, *System Design: Scalable URL shortener service like TinyURL*  
> https://medium.com/@sandeep4.verma/system-design-scalable-url-shortener-service-like-tinyurl-106f30f23a82
>
> **Mục tiêu của plan này:** bám theo tư duy và thứ tự mở rộng trong bài Medium, nhưng triển khai bằng **.NET + PostgreSQL + Redis + Docker** để phù hợp với lộ trình hiện tại.
>
> **Nguyên tắc:** Không xây toàn bộ hệ thống phân tán ngay từ đầu. Mỗi phase chỉ thêm infrastructure khi phase trước đã chạy ổn và có bottleneck rõ ràng.

---

# 0. Project Overview

## 0.1. Mục tiêu

Xây một URL Shortener tương tự TinyURL:

- Nhận một URL dài.
- Tạo một short code ngắn.
- Trả về short URL.
- Khi người dùng truy cập short URL, hệ thống redirect về URL gốc.
- Thu thập analytics về lượt redirect.
- Hỗ trợ custom alias.
- Có thể scale dần từ một server/database đơn giản lên nhiều application servers, cache và database shards.

## 0.2. Functional Requirements

- [ ] Tạo short URL từ long URL.
- [ ] Redirect từ short URL → long URL bằng HTTP 302.
- [ ] Short code mặc định dài 7 ký tự.
- [ ] Hỗ trợ custom alias.
- [ ] Custom alias tối đa 16 ký tự.
- [ ] Thu thập số lượt redirect.
- [ ] Có REST API.
- [ ] URL sau khi tạo được giữ lâu dài theo scope của project.

> Bài Medium giả định 100 triệu URL mới/tháng, tỷ lệ đọc/ghi 200:1, tương đương khoảng 40 create/s và 8.000 redirect/s ở mức trung bình. Đây là capacity model để học system design, không phải target bắt buộc ngay từ MVP.

## 0.3. Non-functional Requirements

- [ ] Redirect latency thấp.
- [ ] Application có thể chạy nhiều instance.
- [ ] Không có single application server là SPOF.
- [x] Cache giúp giảm tải database.
- [ ] Có thể tăng throughput bằng cách thêm application server.
- [ ] Có hướng mở rộng database khi dataset lớn.
- [ ] Analytics không làm chậm redirect path.

---

# 1. Tech Stack

## 1.1. Stack chính

| Thành phần | Công nghệ | Giai đoạn |
|---|---|---|
| Language | C# | Phase 1+ |
| Backend | ASP.NET Core / .NET 10 | Phase 1+ |
| API | REST / OpenAPI | Phase 1+ |
| ORM | EF Core | Phase 1+ |
| SQL tối ưu | Dapper (chỉ khi cần) | Phase 3+ |
| Database | PostgreSQL 18 | Phase 1+ |
| Cache | Redis | Phase 3+ |
| Reverse Proxy | NGINX | Phase 4+ |
| Container | Docker | Phase 1+ |
| Local orchestration | Docker Compose | Phase 2+ |
| Load Test | k6 | Phase 5+ |
| Logging | Serilog | Phase 2+ |
| Metrics | OpenTelemetry + Prometheus | Phase 5+ |
| Dashboard | Grafana | Phase 5+ |
| Message Queue | Kafka | Phase 6+ |
| Analytics | PostgreSQL trước, ClickHouse sau nếu cần | Phase 6+ |
| Kubernetes | k3d/kind → Kubernetes | Phase 8 |

## 1.2. Những thứ KHÔNG làm ngay

- [ ] MongoDB
- [ ] Cassandra / ScyllaDB
- [ ] ZooKeeper
- [ ] Kubernetes production
- [ ] Multi-region
- [ ] CDN
- [ ] Redis Cluster
- [ ] ClickHouse
- [ ] Microservices

> Các thành phần trên là **advanced extension**, chỉ thêm sau khi bản đơn giản đã hoạt động và benchmark được bottleneck.

---

# 2. Repository Structure

## 2.1. Structure mục tiêu

```text
url-shortener/
│
├── src/
│   ├── UrlShortener.Api/
│   │   ├── Controllers/
│   │   ├── Endpoints/
│   │   ├── Middleware/
│   │   ├── Extensions/
│   │   └── Program.cs
│   │
│   ├── UrlShortener.Application/
│   │   ├── DTOs/
│   │   ├── Features/
│   │   │   ├── CreateShortUrl/
│   │   │   ├── Redirect/
│   │   │   ├── GetUrl/
│   │   │   └── DeleteUrl/
│   │   ├── Interfaces/
│   │   └── Validators/
│   │
│   ├── UrlShortener.Domain/
│   │   ├── Entities/
│   │   ├── ValueObjects/
│   │   └── Constants/
│   │
│   ├── UrlShortener.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── DbContext/
│   │   │   ├── Configurations/
│   │   │   └── Migrations/
│   │   ├── Caching/
│   │   ├── IdGeneration/
│   │   └── Repositories/
│   │
│   └── UrlShortener.Workers/
│       └── AnalyticsWorker/
│
├── tests/
│   ├── UrlShortener.UnitTests/
│   ├── UrlShortener.IntegrationTests/
│   └── UrlShortener.LoadTests/
│
├── deploy/
│   ├── docker/
│   ├── nginx/
│   └── kubernetes/
│
├── docs/
│   ├── requirements.md
│   ├── architecture.md
│   ├── capacity-estimation.md
│   ├── base62.md
│   ├── caching.md
│   ├── load-balancing.md
│   ├── database-scaling.md
│   └── analytics.md
│
├── docker-compose.yml
├── .env.example
├── .gitignore
├── README.md
└── PLAN.md
```

## 2.2. Nguyên tắc structure

- [ ] Domain không phụ thuộc Infrastructure.
- [ ] Application không biết chi tiết PostgreSQL/Redis.
- [ ] API chỉ chịu trách nhiệm HTTP.
- [ ] Infrastructure implement interface của Application.
- [ ] Chưa tách microservice khi chưa có lý do.
- [ ] Worker chỉ xuất hiện khi có background workload thực sự.

---

# PHASE 0 — System Design & Capacity Estimation

## Mục tiêu

Hiểu bài toán trước khi code.

## Kiến thức cần nắm

- [ ] Functional requirement
- [ ] Non-functional requirement
- [ ] Read-heavy workload
- [ ] RPS
- [ ] Storage estimation
- [ ] Cache estimation
- [ ] SPOF
- [ ] Horizontal scaling
- [ ] Load balancing
- [ ] Sharding

## Task

- [ ] Đọc lại toàn bộ bài Medium.
- [ ] Ghi lại 5 functional requirements.
- [ ] Ghi lại 5 non-functional requirements.
- [ ] Viết capacity estimation.
- [ ] Tính create request/s.
- [ ] Tính redirect request/s.
- [ ] Tính storage.
- [ ] Tính cache size theo giả định 80/20.
- [ ] Vẽ architecture V1.
- [ ] Vẽ architecture V2.
- [ ] Viết tại sao V1 có bottleneck.
- [ ] Viết lý do thêm từng thành phần ở V2.

## Capacity model tham khảo

```text
New URLs:
100,000,000 / month

≈ 40 creates / second

Read / Write:
200 : 1

Redirect:
≈ 8,000 / second
```

## Deliverable

```text
docs/
├── requirements.md
├── capacity-estimation.md
└── architecture.md
```

## Hoàn thành phase khi

- [ ] Có sơ đồ architecture.
- [ ] Hiểu tại sao redirect là read-heavy.
- [ ] Hiểu tại sao cache sẽ quan trọng.
- [ ] Hiểu tại sao một web server là SPOF.

---

# PHASE 1 — Basic URL Shortener

## Mục tiêu

Tạo một URL shortener hoàn chỉnh nhưng chưa scale.

## Architecture

```text
Client
  │
  ▼
ASP.NET Core
  │
  ▼
PostgreSQL
```

## Backend

- [x] Tạo solution .NET 10.
- [x] Tạo project `UrlShortener.Api`.
- [x] Tạo project `UrlShortener.Application`.
- [x] Tạo project `UrlShortener.Domain`.
- [x] Tạo project `UrlShortener.Infrastructure`.
- [x] Cấu hình dependency injection.
- [x] Cấu hình Swagger/OpenAPI.
- [x] Cấu hình global exception handling.
- [x] Cấu hình validation.
- [x] Cấu hình Serilog.

## Database

### Bảng `users`

```sql
users
-----
id
name
email
api_key_hash
created_at
```

### Bảng `urls`

```sql
urls
----
id BIGINT
short_code VARCHAR(16)
original_url TEXT
user_id BIGINT
management_token_hash BYTEA
created_at TIMESTAMPTZ
```

### Constraint

- [x] Primary key cho `users.id`.
- [x] Primary key cho `urls.id`.
- [x] Unique index cho `urls.short_code`.
- [x] Foreign key `urls.user_id -> users.id`.
- [x] Index trên `urls.short_code`.

## API

### Create

```http
POST /api/v1/urls
```

Request:

```json
{
  "url": "https://example.com/some/very/long/path"
}
```

Response:

```json
{
  "shortCode": "1L9zO9O",
  "shortUrl": "http://localhost:5000/1L9zO9O",
  "managementToken": "<save this token; shown only once>",
  "createdAt": "<UTC timestamp>"
}
```

### Redirect

```http
GET /{shortCode}
```

Response:

```http
HTTP/1.1 302 Found
Location: https://example.com/some/very/long/path
```

### Get URL info

```http
GET /api/v1/urls/{shortCode}
```

### Delete URL

```http
DELETE /api/v1/urls/{shortCode}
X-Management-Token: <token returned at creation>
```

## ID generation — bản đơn giản

Bài Medium dùng counter + Base62 để đảm bảo short code không bị collision. 

- [x] Tạo PostgreSQL sequence.
- [x] Dùng `BIGINT`.
- [x] Lấy một numeric ID.
- [x] Convert numeric ID → Base62.
- [x] Đảm bảo output có thể dài tối đa 7 ký tự.
- [x] Implement Base62 encode.
- [x] Implement Base62 decode.
- [x] Viết unit test encode/decode.
- [x] Test round-trip:
  `number -> base62 -> number`.

## Base62

Alphabet:

```text
0123456789
abcdefghijklmnopqrstuvwxyz
ABCDEFGHIJKLMNOPQRSTUVWXYZ
```

Ví dụ:

```text
125 -> 21 (theo hệ base62)
```

## Testing

- [x] Unit test Base62.
- [x] Unit test URL validation.
- [x] Integration test create URL.
- [x] Integration test redirect.
- [x] Integration test 404.
- [x] Integration test duplicate short code.
- [x] Test invalid URL.

## Deliverable

```text
POST long URL
      ↓
Database
      ↓
shortCode
      ↓
GET /{shortCode}
      ↓
302 Redirect
```

## Hoàn thành phase khi

- [x] Có thể chạy local.
- [x] Có thể tạo short URL.
- [x] Có thể redirect.
- [x] Database migration chạy được.
- [x] Unit tests pass.
- [x] Integration tests pass.

---

# PHASE 2 — Dockerize Application

## Mục tiêu

Chạy toàn bộ hệ thống bằng Docker.

## Infrastructure

```text
Docker Compose
├── app
└── postgres
```

## Task

- [x] Viết `Dockerfile`.
- [x] Viết `.dockerignore`.
- [x] Viết `docker-compose.yml`.
- [x] Containerize ASP.NET Core.
- [x] Containerize PostgreSQL.
- [x] Tạo persistent volume cho PostgreSQL.
- [x] Cấu hình connection string bằng environment variable.
- [x] Cấu hình health endpoint `/health/live` và `/health/ready`.
- [x] Cấu hình startup dependency.
- [x] Test database persistence.

## Environment

```text
ConnectionStrings__Default
ASPNETCORE_ENVIRONMENT
```

## Health Check

- [x] `/health/live`
- [x] `/health/ready`
- [x] PostgreSQL readiness check.

## Hoàn thành phase khi

- [x] `docker compose up` chạy toàn bộ.
- [x] Restart container không mất database.
- [ ] API truy cập được từ host.
- [x] Health check hoạt động.

---

# PHASE 3 — Add Redis Cache

## Mục tiêu

Giảm số lần redirect phải truy cập PostgreSQL.

## Architecture

```text
Client
  │
  ▼
ASP.NET Core
  │
  ├──────────────► Redis
  │
  └──────────────► PostgreSQL
```

## Kiến thức

- [x] Cache-aside pattern.
- [x] Cache hit.
- [x] Cache miss.
- [x] TTL.
- [x] Eviction.
- [x] Serialization.
- [x] Cache invalidation.

## Infrastructure

```text
Docker Compose
├── app
├── postgres
└── redis
```

## Cache key

```text
url:{shortCode}
```

## Flow redirect

```text
GET /abc123
      │
      ▼
Redis GET
      │
 ┌────┴────┐
 │         │
HIT      MISS
 │         │
 ▼         ▼
302     PostgreSQL
           │
           ▼
         Redis SET
           │
           ▼
          302
```

## Task

- [x] Add Redis package.
- [x] Add Redis connection configuration.
- [x] Tạo `ICacheService`.
- [x] Implement `RedisCacheService`.
- [x] Cache `shortCode -> originalUrl`.
- [x] Set TTL.
- [x] Handle cache miss.
- [x] Populate cache from DB.
- [x] Handle cache connection failure gracefully.
- [x] Invalidate cache khi URL bị xóa (update URL chưa có API ở phase này).
- [x] Viết unit test cho cache abstraction.
- [x] Viết integration test cache hit.
- [x] Viết integration test cache miss.

## Cache policy

Ban đầu:

```text
TTL = 1 hour
```

Sau khi benchmark có thể thay đổi.

## Hoàn thành phase khi

- [x] Redirect cache hit không query DB.
- [x] Cache miss query DB rồi cache lại.
- [x] Redis restart không làm mất source of truth.
- [x] API vẫn có thể fallback về DB nếu Redis unavailable.

---

# PHASE 4 — Multiple Application Servers + NGINX

## Mục tiêu

Giải quyết single application server và bắt đầu horizontal scaling.

## Architecture

```text
                    Client
                      │
                      ▼
                   NGINX
                      │
          ┌───────────┼───────────┐
          ▼           ▼           ▼
       API #1       API #2      API #3
          │           │           │
          └───────────┼───────────┘
                      │
                 ┌────┴────┐
                 ▼         ▼
              Redis    PostgreSQL
```

## Infrastructure

```text
Docker Compose
├── nginx
├── api-1
├── api-2
├── api-3
├── postgres
└── redis
```

## Task

- [ ] Tạo NGINX config.
- [ ] Configure upstream servers.
- [ ] Configure round-robin.
- [ ] Configure health check strategy phù hợp với môi trường.
- [ ] Chạy ít nhất 3 API containers.
- [ ] Đảm bảo API stateless.
- [ ] Không lưu application state quan trọng trên local filesystem.
- [ ] Test request distribution.
- [ ] Stop `api-1`.
- [ ] Kiểm tra traffic vẫn được xử lý.
- [ ] Stop `api-2`.
- [ ] Kiểm tra traffic vẫn được xử lý.
- [ ] Test restart một API instance.

## Round Robin

Ban đầu dùng:

```nginx
upstream url_shortener {
    server api-1:8080;
    server api-2:8080;
    server api-3:8080;
}
```

## Kiến thức

- [ ] Reverse proxy.
- [ ] Load balancing.
- [ ] Horizontal scaling.
- [ ] Stateless application.
- [ ] Health check.
- [ ] Fault tolerance cơ bản.

## Hoàn thành phase khi

- [ ] Request đi qua NGINX.
- [ ] Có nhiều API instances.
- [ ] Một API instance chết không làm hệ thống dừng.
- [ ] Có thể scale `api-1..N`.

---

# PHASE 5 — Load Testing & Observability

## Mục tiêu

Đo hệ thống thay vì chỉ cảm nhận bằng mắt.

## Tools

- [ ] k6.
- [ ] Serilog.
- [ ] OpenTelemetry.
- [ ] Prometheus.
- [ ] Grafana.

## Metrics cần theo dõi

### API

- [ ] Requests/sec.
- [ ] Error rate.
- [ ] p50 latency.
- [ ] p95 latency.
- [ ] p99 latency.
- [ ] 404 rate.
- [ ] 302 redirect rate.

### Cache

- [ ] Cache hit.
- [ ] Cache miss.
- [ ] Cache hit ratio.
- [ ] Redis latency.
- [ ] Redis errors.

### Database

- [ ] Connections.
- [ ] Query latency.
- [ ] CPU.
- [ ] Memory.
- [ ] Slow queries.

### Infrastructure

- [ ] CPU per API.
- [ ] Memory per API.
- [ ] Network.
- [ ] Container restarts.

## Load test scenarios

### Test A — Create URL

- [ ] 10 RPS.
- [ ] 50 RPS.
- [ ] 100 RPS.

### Test B — Redirect

- [ ] 100 RPS.
- [ ] 1K RPS.
- [ ] 5K RPS.
- [ ] 8K RPS.

### Test C — Hot URL

- [ ] Một short code được request rất nhiều lần.
- [ ] So sánh cache hit/miss.

### Test D — Cache disabled

- [ ] So sánh với Redis disabled.

### Test E — One server

- [ ] 1 API instance.

### Test F — Three servers

- [ ] 3 API instances.

## Benchmark table

| Scenario | RPS | p50 | p95 | p99 | Error |
|---|---:|---:|---:|---:|---:|
| PostgreSQL only | | | | | |
| Redis | | | | | |
| 1 API | | | | | |
| 3 API | | | | | |

## Task

- [ ] Tạo `tests/UrlShortener.LoadTests`.
- [ ] Viết k6 script.
- [ ] Chạy benchmark trước Redis.
- [ ] Chạy benchmark sau Redis.
- [ ] Chạy benchmark 1 API.
- [ ] Chạy benchmark 3 API.
- [ ] Ghi lại bottleneck.
- [ ] Viết kết luận vào `docs/load-testing.md`.

## Hoàn thành phase khi

- [ ] Có benchmark thực tế.
- [ ] Có dashboard cơ bản.
- [ ] Biết bottleneck hiện tại nằm ở đâu.
- [ ] Có dữ liệu trước/sau khi thêm Redis và load balancing.

---

# PHASE 6 — Analytics với Kafka

## Mục tiêu

Tách analytics khỏi redirect path.

## Vì sao

Redirect là hot path:

```text
GET /abc123
```

Không nên:

```text
GET
 ↓
DB query analytics
 ↓
UPDATE click_count
 ↓
302
```

Nên:

```text
GET
 ↓
Redis / DB
 ↓
302

sau đó

Redirect Event
 ↓
Kafka
 ↓
Analytics Worker
```

Bài Medium dùng HTTP 302 để các redirect tiếp tục đi qua backend, từ đó có thể gửi dữ liệu redirect vào Kafka để analytics thời gian thực.

## Infrastructure

```text
Docker Compose
├── nginx
├── api-1
├── api-2
├── api-3
├── postgres
├── redis
├── kafka
└── analytics-worker
```

## Event model

```json
{
  "eventId": "uuid",
  "shortCode": "abc123",
  "timestamp": "2026-10-08T09:00:00Z",
  "ipAddress": "...",
  "userAgent": "...",
  "referer": "..."
}
```

> Với dữ liệu production thật, cần xem xét privacy/security trước khi lưu IP hoặc user-agent.

## Task

- [ ] Add Kafka.
- [ ] Tạo topic `url-redirected`.
- [ ] Tạo event contract.
- [ ] Publish event từ redirect path.
- [ ] Không chặn redirect nếu analytics consumer đang chậm.
- [ ] Tạo `AnalyticsWorker`.
- [ ] Consumer event từ Kafka.
- [ ] Aggregate click count.
- [ ] Lưu analytics vào PostgreSQL.
- [ ] Implement retry.
- [ ] Handle consumer failure.
- [ ] Theo dõi consumer lag.

## Analytics API

```http
GET /api/v1/urls/{shortCode}/stats
```

Response:

```json
{
  "shortCode": "abc123",
  "totalClicks": 12345
}
```

## Hoàn thành phase khi

- [ ] Redirect vẫn hoạt động khi analytics worker down.
- [ ] Kafka giữ event trong thời gian worker down.
- [ ] Worker có thể tiếp tục consume sau restart.
- [ ] Có thống kê click cơ bản.

---

# PHASE 7 — Database Scaling

## Mục tiêu

Hiểu và thực hành scaling database theo đúng hướng bài Medium.

## Important

Không nhảy ngay sang sharding.

Thứ tự:

```text
PostgreSQL
   ↓
Indexing
   ↓
Query optimization
   ↓
Read replica
   ↓
Partitioning
   ↓
Sharding
```

## 7.1. Database optimization

- [ ] Kiểm tra query plan bằng `EXPLAIN ANALYZE`.
- [ ] Tạo index phù hợp.
- [ ] Đo query latency.
- [ ] Kiểm tra connection pool.
- [ ] Kiểm tra slow query.
- [ ] Tối ưu redirect query.

## 7.2. Read Replica — optional learning

Architecture:

```text
                 API
              /       \
             ▼         ▼
         Primary     Replica
          write        read
```

- [ ] Setup PostgreSQL primary/replica trong lab.
- [ ] Phân biệt read/write workload.
- [ ] Đo replication lag.
- [ ] Chuyển một số read sang replica.
- [ ] Hiểu khi nào không nên đọc replica.

## 7.3. Sharding simulation

Không cần triển khai hàng trăm DB.

Lab:

```text
Shard 0
IDs 1 - 1,000,000

Shard 1
IDs 1,000,001 - 2,000,000

Shard 2
IDs 2,000,001 - 3,000,000
```

- [ ] Chọn `id` làm sharding key.
- [ ] Tạo shard map.
- [ ] Viết routing logic.
- [ ] Convert `shortCode -> numeric ID`.
- [ ] Xác định shard từ numeric ID.
- [ ] Redirect tới đúng shard.
- [ ] Test URL ở từng shard.
- [ ] Test khi một shard unavailable.
- [ ] Viết tài liệu về trade-off.

## 7.4. Không cần ZooKeeper ngay

Bài Medium dùng ZooKeeper để giữ metadata về active database servers và range assignment.

Trong project này:

- [ ] Chưa triển khai ZooKeeper.
- [ ] Mô phỏng shard map bằng config file/database.
- [ ] Hiểu trước cơ chế range allocation.
- [ ] Chỉ nghiên cứu ZooKeeper sau khi hiểu distributed coordination.

## Hoàn thành phase khi

- [ ] Hiểu tại sao DB trở thành bottleneck.
- [ ] Hiểu sharding key.
- [ ] Hiểu range-based sharding.
- [ ] Hiểu trade-off giữa đơn giản và scale.
- [ ] Có demo routing tới nhiều DB.

---

# PHASE 8 — Custom Alias

## Mục tiêu

Cho phép user tự chọn short code.

## API

```http
POST /api/v1/urls
```

Request:

```json
{
  "url": "https://example.com",
  "customAlias": "my-link"
}
```

## Rule

- [ ] Max 16 characters.
- [ ] Chỉ cho phép character hợp lệ.
- [ ] Không trùng alias.
- [ ] Không cho phép reserved words.
- [ ] Case sensitivity được quyết định rõ ràng.
- [ ] Có validation error rõ ràng.

## Reserved words

Ví dụ:

```text
api
admin
health
swagger
metrics
login
register
docs
```

- [ ] Tạo `ReservedAlias` configuration.
- [ ] Validate custom alias.
- [ ] Tạo unique constraint.
- [ ] Test duplicate alias.
- [ ] Test invalid alias.
- [ ] Test reserved alias.

## Design

Có thể giữ chung bảng:

```text
urls
----
id
short_code
original_url
user_id
created_at
is_custom
```

Không cần tách database cho custom alias ở version đầu.

## Hoàn thành phase khi

- [ ] User có thể chọn alias.
- [ ] Alias unique.
- [ ] Alias validate đúng.
- [ ] Redirect custom alias hoạt động.

---

# PHASE 9 — Advanced ID Generation (Optional)

## Mục tiêu

Tìm hiểu các kỹ thuật khác trong bài Medium.

## Technique 1 — Random

- [ ] Generate random 7-char Base62.
- [ ] Check DB.
- [ ] Retry collision.
- [ ] Đo số lần retry.
- [ ] Đo performance khi dataset lớn.

## Technique 2 — Counter + Base62

- [ ] Đã hoàn thành ở Phase 1.
- [ ] Hiểu tại sao không cần collision check.
- [ ] Hiểu giới hạn của centralized counter.

## Technique 3 — MD5

- [ ] Hash long URL.
- [ ] Lấy một phần hash.
- [ ] Check collision.
- [ ] Retry bằng segment khác.
- [ ] Đo collision behavior.

> Chỉ dùng cho mục đích học tập. Không dùng MD5 như cơ chế bảo mật.

## Technique 4 — KGS

- [ ] Tạo Key Generation Service.
- [ ] Pre-generate short keys.
- [ ] Store unused keys.
- [ ] Atomic claim key.
- [ ] Move claimed key sang used set/table.
- [ ] Cache một batch key trong memory.
- [ ] Test key loss khi worker crash.
- [ ] Thiết kế standby KGS.

## Hoàn thành phase khi

- [ ] Có thể giải thích 4 techniques.
- [ ] Biết ưu/nhược điểm của từng technique.
- [ ] Biết vì sao project chính dùng Counter + Base62.

---

# PHASE 10 — Advanced Cache Scaling (Optional)

## Mục tiêu

Mở rộng cache theo hướng bài Medium.

## Current

```text
API
 ↓
Redis
```

## Advanced

```text
            ┌── Redis 1
API ────────┼── Redis 2
            └── Redis 3
```

## Task

- [ ] Nghiên cứu cache replication.
- [ ] Nghiên cứu Redis Sentinel.
- [ ] Nghiên cứu Redis Cluster.
- [ ] Benchmark một Redis instance.
- [ ] Benchmark nhiều Redis nodes.
- [ ] Test cache failover.
- [ ] Theo dõi cache hit ratio.
- [ ] Theo dõi hot key.

---

# PHASE 11 — Kubernetes (Optional)

## Mục tiêu

Đưa application scaling vào container orchestration.

## Local

Chọn một:

- [ ] kind
- [ ] k3d

## Resources

```text
deploy/kubernetes/
├── namespace.yaml
├── configmap.yaml
├── secret.yaml
├── postgres.yaml
├── redis.yaml
├── url-api-deployment.yaml
├── url-api-service.yaml
├── ingress.yaml
├── hpa.yaml
└── analytics-worker.yaml
```

## Task

- [ ] Build Docker image.
- [ ] Run API trên Kubernetes.
- [ ] Expose Service.
- [ ] Add Ingress.
- [ ] Add readiness probe.
- [ ] Add liveness probe.
- [ ] Scale replicas manually.
- [ ] Add HPA.
- [ ] Kill one pod.
- [ ] Observe replacement pod.
- [ ] Load test cluster.
- [ ] Compare Docker Compose vs Kubernetes.

## Hoàn thành phase khi

- [ ] API chạy được trên Kubernetes.
- [ ] Có nhiều replicas.
- [ ] Pod failure được self-heal.
- [ ] Có HPA cơ bản.

---

# PHASE 12 — Security & Abuse Protection

## Mục tiêu

Biến project từ demo thành service có khả năng chống abuse cơ bản.

## Task

- [ ] Validate long URL.
- [ ] Giới hạn request body.
- [ ] Rate limit create endpoint.
- [ ] Rate limit anonymous users.
- [ ] API key cho client/service.
- [ ] Không đưa API key vào request body.
- [ ] Log suspicious requests.
- [ ] Reserved aliases.
- [ ] Chặn malformed URLs.
- [ ] Chống oversized input.
- [ ] HTTPS khi deploy.

## Có thể làm sau

- [ ] Domain blocklist.
- [ ] Malware/phishing check.
- [ ] CAPTCHA cho anonymous abuse.
- [ ] Per-user quotas.

---

# PHASE 13 — CI/CD

## Mục tiêu

Tự động test và build.

## GitHub Actions / GitLab CI

Pipeline:

```text
Push
 ↓
Restore
 ↓
Build
 ↓
Unit Test
 ↓
Integration Test
 ↓
Docker Build
 ↓
Push Image
```

## Task

- [ ] Tạo CI pipeline.
- [ ] Build solution.
- [ ] Run unit tests.
- [ ] Run integration tests.
- [ ] Build Docker image.
- [ ] Tag image.
- [ ] Push image registry.
- [ ] Add branch protection.
- [ ] Add PR check.
- [ ] Add versioning.

---

# 14. Final Architecture — Learning Version

Sau khi hoàn thành các phase chính:

```text
                         Client
                           │
                           ▼
                        NGINX
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
           API #1        API #2        API #3
              │            │            │
              ├────────────┼────────────┤
              │                         │
              ▼                         ▼
           Redis                   PostgreSQL
              │
              │
              └──────────────┐
                             │
                             ▼
                           Kafka
                             │
                             ▼
                     Analytics Worker
                             │
                             ▼
                         PostgreSQL
```

---

# 15. Final Architecture — Advanced Learning

Chỉ làm sau khi hiểu toàn bộ version ở trên.

```text
                            Client
                              │
                              ▼
                         Edge / CDN
                              │
                              ▼
                       Load Balancer
                              │
             ┌────────────────┼────────────────┐
             ▼                ▼                ▼
        Redirect API     Redirect API     Redirect API
             │                │                │
             └────────────────┼────────────────┘
                              │
                     L1 application cache
                              │
                              ▼
                       Redis cluster
                              │
                              ▼
                     DB routing layer
                     /       |       \
                    ▼        ▼        ▼
                 Shard 0  Shard 1  Shard 2
                    │        │        │
                 Replica  Replica  Replica

Redirect Event
      │
      ▼
    Kafka
      │
      ▼
 Analytics Workers
      │
      ▼
   Analytics DB
```

---

# 16. Phase Tracking

## Core path — bắt buộc

- [x] Phase 0 — System Design & Capacity
- [x] Phase 1 — Basic URL Shortener
- [x] Phase 2 — Docker
- [x] Phase 3 — Redis
- [ ] Phase 4 — NGINX + Multiple API
- [ ] Phase 5 — Load Testing + Observability
- [ ] Phase 6 — Kafka + Analytics
- [ ] Phase 7 — Database Scaling
- [ ] Phase 8 — Custom Alias

## Advanced path — tùy chọn

- [ ] Phase 9 — Advanced ID Generation
- [ ] Phase 10 — Advanced Cache Scaling
- [ ] Phase 11 — Kubernetes
- [ ] Phase 12 — Security & Abuse Protection
- [ ] Phase 13 — CI/CD

---

# 17. Milestones

## Milestone 1 — Working URL Shortener

```text
ASP.NET Core
+
PostgreSQL
+
Base62
```

- [x] Create URL
- [x] Redirect
- [x] DB schema
- [x] Tests

## Milestone 2 — Fast Redirect

```text
+
Redis
```

- [x] Cache hit
- [x] Cache miss
- [x] Fallback DB
- [ ] Benchmark

## Milestone 3 — Horizontally Scalable API

```text
+
NGINX
+
3 API instances
```

- [ ] Load balance
- [ ] API stateless
- [ ] Failure test
- [ ] Benchmark

## Milestone 4 — Observable System

```text
+
k6
+
OpenTelemetry
+
Prometheus
+
Grafana
```

- [ ] Metrics
- [ ] Dashboard
- [ ] Load tests
- [ ] Bottleneck report

## Milestone 5 — Asynchronous Analytics

```text
+
Kafka
+
Analytics Worker
```

- [ ] Event publish
- [ ] Consumer
- [ ] Aggregation
- [ ] Failure recovery

## Milestone 6 — Database Scaling Lab

```text
+
Replica
+
Sharding simulation
```

- [ ] Shard routing
- [ ] Range allocation concept
- [ ] Failure scenario

---

# 18. Suggested Development Order Inside Each Phase

Mỗi phase nên làm theo thứ tự:

```text
1. Read / Study
       ↓
2. Design
       ↓
3. Implement
       ↓
4. Test
       ↓
5. Benchmark
       ↓
6. Find bottleneck
       ↓
7. Document
       ↓
8. Move to next phase
```

Không chuyển phase chỉ vì code "chạy được".

Chỉ chuyển phase khi bạn có thể trả lời:

> Thành phần mới giải quyết vấn đề gì?

---

# 19. Questions You Should Be Able to Answer

## Sau Phase 1

- [ ] Base62 là gì?
- [ ] Tại sao 7 ký tự?
- [ ] Counter + Base62 có collision không?
- [ ] Tại sao dùng BIGINT?
- [ ] Tại sao redirect dùng 302?

## Sau Phase 3

- [x] Cache-aside là gì?
- [x] Cache hit/miss là gì?
- [x] Tại sao cache giảm DB load?
- [x] Nếu Redis chết thì sao?
- [x] Cache invalidation xử lý thế nào?

## Sau Phase 4

- [ ] Vertical vs horizontal scaling?
- [ ] Load balancer làm gì?
- [ ] Round robin là gì?
- [ ] Tại sao application phải stateless?
- [ ] SPOF là gì?

## Sau Phase 5

- [ ] p95/p99 là gì?
- [ ] RPS là gì?
- [ ] Bottleneck được xác định thế nào?
- [ ] Vì sao nhiều API instance tăng throughput?

## Sau Phase 6

- [ ] Tại sao analytics không nên nằm trong request path?
- [ ] Kafka giải quyết vấn đề gì?
- [ ] Producer/consumer là gì?
- [ ] Consumer lag là gì?
- [ ] Nếu worker chết thì event có mất không?

## Sau Phase 7

- [ ] Khi nào database trở thành bottleneck?
- [ ] Replication là gì?
- [ ] Sharding là gì?
- [ ] Shard key là gì?
- [ ] Range-based sharding hoạt động thế nào?
- [ ] Tại sao sharding làm hệ thống phức tạp hơn?

## Sau Phase 9+

- [ ] KGS giải quyết vấn đề gì?
- [ ] ZooKeeper giải quyết vấn đề gì?
- [ ] Redis Cluster khác Redis single node thế nào?
- [ ] Hot key là gì?
- [ ] Database failover là gì?

---

# 20. Project Definition of Done

Project core được coi là hoàn thành khi:

- [ ] User có thể tạo short URL.
- [ ] User có thể redirect.
- [ ] Short code được sinh tự động bằng Counter + Base62.
- [ ] Custom alias hoạt động.
- [ ] PostgreSQL lưu source of truth.
- [x] Redis cache redirect mapping.
- [ ] NGINX load balance nhiều API instances.
- [ ] Một API instance chết không làm hệ thống dừng.
- [ ] Có load test.
- [ ] Có metrics.
- [ ] Có analytics bằng Kafka.
- [ ] Có documentation cho architecture.
- [ ] Có benchmark trước/sau các bước scale.

---

# 21. Rules For This Project

### Rule 1

**Không thêm technology nếu chưa hiểu problem mà nó giải quyết.**

### Rule 2

**Mỗi phase phải có benchmark hoặc failure test khi phù hợp.**

### Rule 3

**PostgreSQL là source of truth.**

### Rule 4

**Redis chỉ là cache, không phải database chính của URL.**

### Rule 5

**Redirect path phải càng ngắn càng tốt.**

### Rule 6

**Analytics là asynchronous.**

### Rule 7

**Microservice chỉ xuất hiện khi có bounded workload hoặc scaling problem rõ ràng.**

### Rule 8

**Bản đơn giản chạy tốt quan trọng hơn kiến trúc "xịn" nhưng chưa hiểu.**

---

# 22. Reference Mapping — Medium → Project

| Bài Medium | Project Phase |
|---|---|
| Requirements | Phase 0 |
| Traffic estimation | Phase 0 |
| Storage estimation | Phase 0 |
| Rudimentary design | Phase 0/1 |
| REST endpoints | Phase 1 |
| DB schema | Phase 1 |
| Base62 | Phase 1 |
| Counter | Phase 1 |
| Random technique | Phase 9 |
| MD5 technique | Phase 9 |
| KGS | Phase 9 |
| MongoDB sharding | Phase 9 / Study |
| SQL sharding | Phase 7 |
| Cache | Phase 3 |
| Cache replication | Phase 10 |
| Load balancer | Phase 4 |
| Custom tiny URL | Phase 8 |
| Kafka analytics | Phase 6 |

---

# 23. Recommended First Commit

Commit đầu tiên chỉ nên chứa:

```text
PLAN.md
README.md
docs/
├── requirements.md
├── capacity-estimation.md
└── architecture.md
```

Commit thứ hai:

```text
.NET solution
+
empty projects
+
tests
```

Commit thứ ba:

```text
PostgreSQL
+
users
+
urls
+
EF Core migrations
```

Sau đó bắt đầu Phase 1 implementation.

---

# 24. Current Target

```text
                    NOW
                     │
                     ▼
            ┌─────────────────┐
            │ Phase 0         │
            │ System Design   │
            └────────┬────────┘
                     ▼
            ┌─────────────────┐
            │ Phase 1         │
            │ .NET + Postgres │
            │ Base62          │
            └────────┬────────┘
                     ▼
            ┌─────────────────┐
            │ Phase 2         │
            │ Docker          │
            └────────┬────────┘
                     ▼
            ┌─────────────────┐
            │ Phase 3         │
            │ Redis           │
            └────────┬────────┘
                     ▼
            ┌─────────────────┐
            │ Phase 4         │
            │ NGINX + API x3  │
            └────────┬────────┘
                     ▼
            ┌─────────────────┐
            │ Phase 5         │
            │ k6 + Monitoring │
            └────────┬────────┘
                     ▼
            ┌─────────────────┐
            │ Phase 6         │
            │ Kafka Analytics │
            └────────┬────────┘
                     ▼
            ┌─────────────────┐
            │ Phase 7         │
            │ DB Scaling      │
            └────────┬────────┘
                     ▼
            ┌─────────────────┐
            │ Phase 8         │
            │ Custom Alias    │
            └─────────────────┘
```

**Start here: Phase 0 → Phase 1.**

