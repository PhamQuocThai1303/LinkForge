# Phase 0 — Capacity estimation

> Đây là phép tính theo giả định để học system design, không phải benchmark hay mức chịu tải đã được kiểm chứng. Các con số gốc 100 triệu URL/tháng, đọc/ghi 200:1, 500 byte/bản ghi và tuổi thọ 100 năm được nêu trong [bài tham khảo](https://medium.com/@sandeep4.verma/system-design-scalable-url-shortener-service-like-tinyurl-106f30f23a82). LinkForge sẽ đo lại ở Phase 5.

## 1. Đầu vào và đơn vị

| Biến | Giá trị giả định | Ý nghĩa |
|---|---:|---|
| `W_month` | 100.000.000 | URL mới trong 30 ngày |
| `seconds_month` | 30 × 24 × 3.600 = 2.592.000 | Giây trong tháng mô hình |
| `read_write_ratio` | 200:1 | 200 redirect cho mỗi lần tạo |
| `bytes_per_url` | 500 B | Ngân sách thô cho một URL và metadata; không phải kích thước PostgreSQL đã đo |
| `life` | 100 năm | Chỉ để so sánh với capacity model của bài tham khảo |

GB/TB dưới đây là đơn vị thập phân: 1 GB = 10^9 byte, 1 TB = 10^12 byte. Trung bình không mô tả peak; chưa có hệ số peak quan sát được.

## 2. Throughput

```text
create/s   = 100.000.000 / 2.592.000 = 38,58 ≈ 40 RPS
redirect/s = 38,58 × 200 = 7.716,05 ≈ 8.000 RPS
redirect/ngày theo số làm tròn = 8.000 × 86.400 = 691.200.000 request
```

Đường redirect chịu tải đọc lớn gấp 200 lần đường tạo theo giả định trên. Nếu mọi redirect đều query PostgreSQL, trung bình database phải phục vụ xấp xỉ 8.000 lookup/s, chưa tính request khác hoặc peak. Vì vậy cache trên đường đọc là ứng viên hợp lý, nhưng chỉ thêm và tinh chỉnh sau khi có baseline và kiểm thử đúng/sai khi cache lỗi.

## 3. Storage cho URL

```text
URL mới/năm       = 100.000.000 × 12 = 1.200.000.000
Dung lượng/tháng  = 100.000.000 × 500 B = 50 GB
Dung lượng/năm    = 1.200.000.000 × 500 B = 600 GB
URL trong 100 năm = 120.000.000.000
Dung lượng 100 năm = 120.000.000.000 × 500 B = 60 TB
```

Đây là **logical estimate** cho dữ liệu URL. Nó chưa tính index, row/page overhead, WAL, backup, replica, analytics, user records hoặc mức tăng trưởng khác. `500 B` không phải kích thước cố định: URL gốc có độ dài biến thiên. Khi có schema và dữ liệu mẫu, đo kích thước bảng/index thực tế rồi thay thế giả định này. Không dùng con số 60 TB để biện minh sharding ngay trong MVP.

## 4. Không gian mã Base62

| Độ dài | Số mã có thể biểu diễn |
|---|---:|
| 6 | `62^6 = 56.800.235.584` |
| 7 | `62^7 = 3.521.614.606.208` |

Nếu không có tái sử dụng mã, 6 ký tự không đủ cho 120 tỷ URL của mô hình 100 năm; 7 ký tự đủ về mặt số lượng. Số mã thực dùng có thể thấp hơn nếu reserved aliases, custom aliases hoặc chính sách khác chiếm một phần không gian. Sequence `BIGINT` chỉ là cách cấp ID; cần kiểm tra giới hạn 7 ký tự trước khi ID vượt `62^7 - 1`. Nếu muốn đúng 7 ký tự ngay từ ID nhỏ, thêm `0` bên trái phần Base62. Không được bỏ các số `0` này khi lookup theo `shortCode` nếu đó là định dạng canonical.

## 5. Cache theo giả định 80/20

Quy tắc 80/20 nói **20% URL được truy cập có thể tạo ra 80% request**. Nó không cho biết có bao nhiêu URL *duy nhất* được truy cập mỗi ngày. Vì vậy không thể suy ra RAM cache chỉ từ `8.000 redirect/s`.

Mô hình tham số:

```text
D = số short code duy nhất được truy cập trong một ngày
H = 0,2 × D mã nóng (giả định 80/20)
E = số byte Redis thực đo cho một entry (key + value + overhead)
RAM cơ sở ≈ H × E
RAM cấp phát ≈ RAM cơ sở × (1 + headroom)
```

Ví dụ minh họa, **không phải số liệu thực**: nếu `D = 10 triệu`, `H = 2 triệu`, `E = 800 B` và headroom = 30%, cần khoảng `2.000.000 × 800 × 1,3 = 2,08 GB`. TTL, eviction và mẫu truy cập sẽ quyết định cache có giữ được tập mã nóng đó hay không.

Bài tham khảo tính `20% × 691,2 triệu request/ngày × 500 B ≈ 69,12 GB` (làm tròn 70 GB). Phép tính ấy coi một phần request như một số lượng entry riêng biệt, nên chỉ là ước lượng rất thô, không chứng minh cần 70 GB Redis. Cần đo `D`, kích thước entry thực và cache hit ratio trước khi chọn cấu hình.

## 6. Điều cần đo ở các phase sau

| Khi nào | Số liệu | Quyết định được hỗ trợ |
|---|---|---|
| Phase 1–2 | Kích thước URL và bảng/index thực tế | Sửa `bytes_per_url` và storage estimate |
| Phase 3 | Unique active codes, hit/miss, Redis memory per entry | TTL và dung lượng cache |
| Phase 5 | RPS đạt được, p50/p95/p99, error rate, CPU và DB latency | Xác định bottleneck trước khi scale |
| Phase 6 | Event rate và consumer lag | Năng lực Kafka/worker và độ trễ analytics |
| Phase 7 | Query plan, index size, connection usage, replication lag | Có cần replica, partition hoặc sharding không |

