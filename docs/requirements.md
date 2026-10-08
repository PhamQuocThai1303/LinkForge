# Phase 0 — Yêu cầu dự án LinkForge

> Trạng thái: đặc tả Phase 0, cập nhật với quyết định triển khai Phase 1. Phạm vi và thứ tự phase lấy từ [PLAN.md](../PLAN.md). Mô hình tải tham khảo bài [System Design: Scalable URL shortener service like TinyURL](https://medium.com/@sandeep4.verma/system-design-scalable-url-shortener-service-like-tinyurl-106f30f23a82) của Sandeep Verma; đây là giả định học tập, chưa phải số đo của LinkForge.

## 1. Mục tiêu và người dùng

LinkForge là dịch vụ rút gọn URL có REST API. Người tạo liên kết gửi một URL dài và nhận URL ngắn. Người truy cập URL ngắn được chuyển tới URL gốc. Dự án dùng dịch vụ này để học cách đo tải và mở rộng hệ thống theo từng bottleneck thực tế.

Thành công của đường đi cơ bản: tạo liên kết, lưu vào PostgreSQL, truy cập mã đã tạo nhận `302` với `Location` là URL gốc; mã không tồn tại nhận `404`. Các khả năng cache, nhiều API instance, analytics và custom alias xuất hiện ở các phase sau theo PLAN.

### Giả định đang dùng

1. Phase 1 cho phép tạo liên kết ẩn danh. `POST` trả management token một lần; `DELETE` cần token đó trong header và database chỉ lưu hash SHA-256. Tài khoản/API key thuộc phase sau.
2. "Lưu lâu dài" nghĩa là không tự hết hạn. Xóa chủ động với token vẫn được phép.
3. Mã tự sinh **đúng 7 ký tự** bằng cách đệm `0` bên trái kết quả Base62.
4. Cùng một URL dài gửi lại sẽ tạo mã mới. `POST` chưa hỗ trợ idempotency key, nên retry sau timeout có thể tạo bản ghi khác.

## 2. Yêu cầu chức năng

| ID | Khả năng và tiêu chí quan sát | Phase |
|---|---|---|
| FR-01 | `POST /api/v1/urls` nhận URL HTTP(S) hợp lệ, trả mã tự sinh duy nhất và short URL; mã tự sinh dài đúng 7 ký tự. | 1 |
| FR-02 | `GET /{shortCode}` trả `302 Found` với `Location` là URL gốc; mã không có trong kho dữ liệu trả `404`. | 1 |
| FR-03 | `GET /api/v1/urls/{shortCode}` trả thông tin liên kết theo hợp đồng API được chốt ở Phase 1. | 1 |
| FR-04 | `DELETE /api/v1/urls/{shortCode}` cần `X-Management-Token` đã cấp khi tạo; sau khi xóa, redirect trả `404`. | 1 |
| FR-05 | Người tạo có thể chọn custom alias tối đa 16 ký tự; alias phải hợp lệ, không thuộc danh sách reserved và không trùng mã đã có. | 8 |
| FR-06 | Hệ thống đếm lượt redirect và cung cấp `GET /api/v1/urls/{shortCode}/stats`; mất hoặc chậm analytics worker không được chặn redirect. | 6 |
| FR-07 | Liên kết không có TTL mặc định; chỉ biến mất khi được xóa hợp lệ. | 1 |

### Phạm vi theo thời điểm

- **Phase 1:** tạo mã tự động, redirect, đọc thông tin, quy tắc xóa được chốt, PostgreSQL và kiểm thử.
- **Phase 2–5:** Docker, Redis, nhiều API instance, load test và quan sát hệ thống. Chúng thay đổi cách vận hành, không thay đổi ý nghĩa của mã URL.
- **Phase 6:** analytics bất đồng bộ.
- **Phase 7:** bài thực hành database scaling.
- **Phase 8:** custom alias.
- **Ngoài phạm vi MVP:** microservices, multi-region, Kubernetes production, CDN, Redis Cluster và ClickHouse.

## 3. Yêu cầu phi chức năng

| ID | Yêu cầu | Cách xác minh dự kiến |
|---|---|---|
| NFR-01 | Redirect có độ trễ thấp, kể cả khi tải tăng. | Đo RPS, p50, p95, p99 và error rate bằng k6 ở Phase 5; đặt SLO sau khi có baseline. |
| NFR-02 | Không mất dữ liệu URL do Redis restart. | PostgreSQL là source of truth; kiểm thử cache miss và Redis unavailable ở Phase 3. |
| NFR-03 | Có thể tăng throughput bằng thêm API instance. | So sánh 1 và 3 instance; dừng một instance rồi kiểm tra redirect ở Phase 4–5. |
| NFR-04 | Analytics không nằm trên đường xử lý bắt buộc của redirect. | Dừng worker rồi xác minh `302` vẫn trả về ở Phase 6. |
| NFR-05 | Hệ thống có tín hiệu để tìm bottleneck. | Theo dõi request, cache, database và container metrics ở Phase 5. |
| NFR-06 | URL lưu lâu dài trong phạm vi dự án, trừ xóa chủ động. | Kiểm thử persistence qua restart ở Phase 2; xác định backup/restore trước khi coi là vận hành production. |

Chỉ tiêu 100 triệu URL mới/tháng và tỷ lệ đọc/ghi 200:1 là **capacity model**, không phải SLO hoặc cam kết throughput của MVP. Chi tiết tính toán ở [capacity-estimation.md](capacity-estimation.md).

## 4. Hợp đồng dữ liệu và kiến trúc dự kiến

- Mã tự sinh: alphabet `0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ`; sequence PostgreSQL cấp numeric ID, sau đó Base62 encode và đệm thành 7 ký tự.
- PostgreSQL giữ ánh xạ `shortCode -> originalUrl` và ràng buộc unique trên `short_code`.
- Redis ở Phase 3 chỉ cache ánh xạ; cache miss hoặc Redis unavailable phải quay về PostgreSQL.
- `302` được dùng để request redirect tiếp tục có thể đi qua backend phục vụ thống kê. Client hoặc proxy vẫn có thể cache phản hồi tùy chính sách HTTP, nên không được coi `302` là bảo đảm tuyệt đối mọi lượt truy cập đều được đếm.
- Analytics ở Phase 6 được gửi qua Kafka và xử lý bởi worker; chi tiết độ bền của event và hành vi khi Kafka unavailable cần thiết kế riêng trước khi triển khai.

Sơ đồ V1/V2, bottleneck và thứ tự bổ sung thành phần nằm ở [architecture.md](architecture.md).

## 5. Cấu trúc và quy ước cho Phase 1

PLAN dự kiến `src/UrlShortener.Api`, `src/UrlShortener.Application`, `src/UrlShortener.Domain`, `src/UrlShortener.Infrastructure` và `tests/UrlShortener.UnitTests`, `tests/UrlShortener.IntegrationTests`. `docs/` lưu quyết định và số đo. Domain không phụ thuộc Infrastructure; API xử lý HTTP; Infrastructure hiện thực các interface cần thiết cho Application.

Quy ước C# dự kiến: PascalCase cho kiểu/phương thức/public member, camelCase cho biến cục bộ, một kiểu request có tên rõ nghĩa; ví dụ phong cách, chưa phải code đã triển khai:

```csharp
public sealed record CreateShortUrlRequest(string Url);
```

### Lệnh và kiểm thử

Phase 1 đã có solution và lệnh build/test chính xác. Integration tests cần Docker Desktop:

```powershell
dotnet build LinkForge.slnx
dotnet test tests/UrlShortener.UnitTests/UrlShortener.UnitTests.csproj
dotnet test tests/UrlShortener.IntegrationTests/UrlShortener.IntegrationTests.csproj
```

Chiến lược kiểm thử: unit test Base62 round-trip và URL validation; integration test tạo URL, redirect `302`, `404`, unique constraint, URL không hợp lệ và token xóa. Lệnh chạy local và migration nằm trong [README.md](../README.md).

### Ranh giới thực hiện

- **Luôn làm:** validate URL đầu vào, giữ unique constraint ở database, kiểm thử hành vi trước khi đánh dấu hoàn thành, ghi lại giả định đo tải.
- **Đã chốt cho Phase 1:** metadata đọc công khai; delete cần management token; URL tối đa 2.048 ký tự, HTTP(S), DNS host, không credentials; mã tự sinh phân biệt chữ hoa/thường.
- **Không làm:** lưu secret trong git, coi Redis là nguồn dữ liệu chính, thêm công nghệ chỉ vì nằm trong sơ đồ cuối.

## 6. Tiêu chí hoàn thành Phase 0

- [x] Có ít nhất 5 yêu cầu chức năng và 5 yêu cầu phi chức năng, phân biệt phạm vi MVP và các phase sau.
- [x] Có phép tính create RPS, redirect RPS, storage và cache với giả định được ghi rõ.
- [x] Có sơ đồ V1/V2 và giải thích bottleneck, vai trò từng thành phần V2.
- [x] Nêu được vì sao workload đọc nhiều, cache hữu ích và một application server là SPOF.
- [x] Các quy tắc Phase 1 đã được chọn theo ủy quyền của người dùng.

## 7. Quyết định dành cho phase sau

1. Tài khoản/API key và quota sẽ cần thiết kế quyền riêng khi Phase 12 bắt đầu; bảng `users` đã có nhưng Phase 1 chưa cấp tài khoản.
2. Phase 8: custom alias có phân biệt chữ hoa/thường không, và alias 7 ký tự trùng không gian mã tự sinh sẽ được xử lý bằng chiến lược nào.
3. Quy tắc chặn đích nội bộ qua DNS và abuse protection cần xử lý ở Phase 12. Phase 1 không fetch URL đích.

