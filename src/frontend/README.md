# Subs — React + TypeScript frontend

Giao diện quản trị tiếng Việt cho API Subscription Management, dùng React 19, TypeScript và Vite. Không dùng dữ liệu giả
khi chạy ứng dụng.

## Chạy cùng backend bằng Aspire

Yêu cầu .NET 10 SDK, Docker, Node.js **22.12+** và npm trên `PATH`. Từ thư mục gốc repo:

```bash
dotnet run --project src/AppHost
```

Aspire tự chạy `npm ci`, khởi động Vite tại **http://localhost:5173** sau khi API sẵn sàng, và truyền endpoint API vào
proxy qua `API_PROXY_TARGET`.
Không cần mở terminal FE riêng. Dashboard hiển thị resource `frontend` cùng log của Vite và bước cài dependencies.

Để chỉ chạy stack ứng dụng, không chạy bộ Grafana/Prometheus/Loki/Tempo:

```bash
dotnet run --project src/AppHost -- --Observability:Enabled=false
```

## Chạy FE riêng

Yêu cầu Node.js **22.12+** và npm. Chạy backend theo hướng dẫn ở [README chính](../../README.md#local-development), sau
đó mở terminal khác:

```bash
cd src/frontend
npm ci
npm run dev
```

Mở **http://localhost:5173**. Vite chuyển tiếp `/api/*` sang **http://localhost:5127/**, khớp với launch profile `http`
của API và Aspire. Backend không cần bật CORS.

Nếu backend chạy bằng Docker Compose ở cổng 8080, hoặc qua Kong ở cổng khác:

```bash
cd src/frontend
API_PROXY_TARGET=http://localhost:8080 npm run dev
```

Hoặc copy `.env.example` thành `.env.local`, sửa `API_PROXY_TARGET`, rồi khởi động lại Vite. Giá trị này chỉ dùng bởi
proxy phía server, không được đưa vào bundle trình duyệt.

## Chức năng

- **Đăng ký:** danh sách với cursor pagination (20 mục/trang), lọc trạng thái, tạo mới, xem chi tiết, tạm dừng, tiếp tục
  và hủy.
- **Gia hạn thủ công:** `/renew` tạo hóa đơn; thao tác “Đánh dấu đã trả” gọi `/pay`, chỉ ghi nhận thanh toán, không thu
  tiền qua gateway.
- **Gia hạn tự động:** `/renewals` trả 202; xem tiến trình với `/invoices/{id}/renewal`. Cần chạy đầy đủ Billing,
  Payments, Relay, RabbitMQ và bật outbox (Aspire đã cấu hình). Chọn “Kiểm tra lại” để cập nhật tiến trình; chọn “Làm
  mới” để cập nhật trạng thái đăng ký/hóa đơn. 404 ngay sau 202 có thể là do message chưa được xử lý; FE không tự tạo
  lại yêu cầu gia hạn.
- **Gói giá:** xem danh mục đang hoạt động, tạo gói theo tháng/năm, tiền tệ và số ngày dùng thử. Backend hiện chưa liên
  kết gói vào subscription; subscription luôn gia hạn theo tháng bằng USD.
- **Hóa đơn:** xem theo đăng ký hoặc tra cứu trực tiếp bằng ID hóa đơn; đánh dấu thanh toán, vô hiệu hóa đơn đang mở và
  tra cứu gia hạn.
- Loading/empty/error states, chi tiết lỗi validation và trace ID, chặn submit lặp, xác nhận thao tác tài chính/hủy,
  dialog hỗ trợ bàn phím, responsive.

Thống kê trên danh sách chỉ tính **trang hiện tại**, không phải tổng toàn hệ thống. Không hiển thị tổng doanh thu vì API
không cung cấp số liệu này.

## Kiểm tra và build

```bash
npm test
npm run build
npm run preview
```

`build` kiểm tra TypeScript và tạo `dist/`. `preview` phục vụ bản build ở cổng 4173 và dùng cùng `API_PROXY_TARGET`; đây
là công cụ xem thử local.

Xem [kết quả E2E gia hạn qua Aspire](../../docs/testing/frontend-aspire-renewal.md) cho ba ca thành công, từ chối và
timeout, cùng lệnh chạy lại system tests.

Khi triển khai production, phục vụ file trong `dist/` bằng static web server và cấu hình reverse proxy **cùng origin**:
`/api/*` → API backend, bỏ prefix `/api`. Đường dẫn màn hình dùng hash (`/#subscriptions/{id}`), không cần SPA rewrite
cho các màn hình. Vite dev server không dùng cho production.

Chart Helm hiện deploy FE bằng image Nginx từ `src/frontend/Dockerfile`, cùng toàn bộ dịch vụ gia hạn. Kong phục vụ `/`
từ FE và chuyển `/api/*` đến API.
Xem [hướng dẫn Kubernetes và kết quả E2E](../../docs/testing/frontend-kubernetes-renewal.md).
Cluster OrbStack hiện đã được Argo CD quản lý từ OCI chart, pin đủ 6 image digest:
[kết quả OCI/Argo CD và E2E mới](../../docs/testing/frontend-argocd-oci-renewal.md).
FE qua Kong tại **http://127.0.0.1:30080/**, Argo CD tại **http://argocd.localhost:30080**.

## Giới hạn hiện tại

- Backend chưa có authentication/authorization; frontend này dành cho môi trường quản trị tin cậy. Bảo vệ quyền truy cập
  ở gateway trước khi public ra Internet.
- Không cung cấp thao tác `/settle` của Relay; đây là command nội bộ gắn payment ID, không phải thao tác thanh toán thủ
  công.
- Giá nhập được gửi bằng decimal JSON nguyên gốc để tránh làm tròn qua `Number`. Giá đọc từ API hiển thị bằng JavaScript
  `Number` và tối đa 4 chữ số thập phân; giá trị vượt độ chính xác của JavaScript có thể được làm tròn khi hiển thị.
- Font Be Vietnam Pro được tải từ Google Fonts; khi offline dùng font hệ thống.
- Resource Vite trong Aspire chỉ dùng khi chạy local và được bỏ khỏi model khi Aspire publish. Kubernetes dùng image FE
  tĩnh trong chart Helm riêng.
