# Frontend + Aspire: kiểm thử gia hạn tự động

Đã kiểm tra ngày **2026-11-17** với .NET SDK 10.0.404, Aspire 13.6.5 và Vite 8.3.8.

## Khởi động

Từ thư mục gốc repo, với Docker, Node.js 22.12+ và npm trên `PATH`:

```bash
dotnet run --project src/AppHost
```

FE ở **http://localhost:5173**, dashboard ở **http://localhost:15043**. Aspire tự chạy resource `frontend-installer`
với `npm ci`, sau đó chạy `frontend` với cổng nội bộ được cấp động. `/api` được proxy đến endpoint API của Aspire.
`strictPort` ngăn Vite tự đổi cổng ngoài cấu hình Aspire.

Phiên kiểm thử dùng cùng AppHost, tắt riêng bộ monitoring để giảm tài nguyên:

```bash
dotnet run --project src/AppHost --launch-profile http -- --Observability:Enabled=false
```

PostgreSQL, các standby, PgBouncer, Redis, RabbitMQ, migrator, hai replica của mỗi dịch vụ API/Billing/Payments/Relay
và frontend đều chạy qua Aspire. `frontend` báo `Running / Healthy`; `frontend-installer` hoàn tất thành công.

## Kết quả trên trình duyệt

Các đăng ký đều được tạo bằng form FE, sau đó mở trang chi tiết và bấm **Gia hạn tự động**, xác nhận yêu cầu.
Cả ba request qua `http://localhost:5173/api/subscriptions/{id}/renewals` đều trả **202 Accepted**.
Không mock HTTP, gRPC, database hoặc message broker. Gateway thanh toán là bộ mô phỏng sẵn có của repo.

| Ca                  | Giá trước thuế | Hóa đơn sau thuế 10% | Saga      | Hóa đơn | Đăng ký | Payment events                   |
|---------------------|---------------:|---------------------:|-----------|---------|---------|----------------------------------|
| Thu tiền thành công |         10 USD |               11 USD | Completed | PAID    | ACTIVE  | ChargeRequested → ChargeCaptured |
| Bị từ chối          |       6000 USD |             6600 USD | Declined  | VOID    | PAUSED  | ChargeRequested → ChargeDeclined |
| Không có phản hồi   |       9500 USD |            10450 USD | TimedOut  | VOID    | PAUSED  | ChargeRequested → ChargeCanceled |

Đã bấm **Kiểm tra lại** để xác nhận saga trên FE, sau đó **Làm mới** để xác nhận hóa đơn/trạng thái đăng ký.
Đã đối chiếu thêm payment snapshot và event stream qua Payments, gồm amount/currency của ca thành công.
Ca timeout dùng đúng `Billing:RenewalTimeoutSeconds=30`, không rút ngắn timer để tạo kết quả.

GET renewal đầu tiên có thể trả 404 trước khi Billing nhận message. FE hiển thị thông báo đang chờ và cập nhật đúng khi
kiểm tra lại;
404 này cũng xuất hiện trong network/console của trình duyệt. Không phát hiện lỗi JavaScript runtime.

Các bản ghi kiểm thử được giữ lại với tên `FE-Aspire-E2E-{captured|declined|timeout}-20261117`, không chỉnh sửa đăng ký
có sẵn:

| Ca         | Subscription ID                      | Invoice ID                           |
|------------|--------------------------------------|--------------------------------------|
| Thành công | e201871b-92d4-4a7a-9731-1533af9bd091 | 3a68557c-ca19-4a41-bc70-10825bcad468 |
| Từ chối    | 4bc64668-a923-4bcc-b59a-deecadf0eac7 | 4103c713-3d65-44fd-8ce8-700934804c6e |
| Timeout    | cccf67d1-4ce5-481d-b736-098929375663 | b0b12b31-0b3e-4c61-8645-190ecdea6b18 |

Có thể mở `http://localhost:5173/#subscriptions/{subscriptionId}` để xem lại. Ảnh kiểm thử nằm trong
`output/playwright/aspire-renewal-{completed|declined|timedout}.png` của workspace kiểm thử (không commit).

## Kiểm tra tự động đã chạy

```bash
dotnet build src/AppHost
dotnet test tests/SubscriptionManagement.SystemTests --filter 'FullyQualifiedName~RenewalSagaSystemTests'
cd src/frontend
npm test
npm run build
```

- AppHost build: **0 warnings, 0 errors**.
- System tests của renewal saga: **6 passed, 0 failed, 0 skipped**; có capture, decline, timeout, concurrency, unknown
  invoice và Relay phục hồi từ outbox.
- Frontend tests: **16 passed** sau khi chuyển vào `src/frontend`.
- Frontend TypeScript và production build: thành công.

Đây là kiểm tra local orchestration và ba kịch bản trình duyệt; không thêm một bộ browser test chạy tự động vào CI.
Chưa kiểm thử gateway thanh toán ngoài repo hoặc triển khai frontend lên Kubernetes. Resource Vite chỉ có trong run
mode;
Docker image backend bỏ `src/frontend` khỏi build context để không copy Node dependencies vào image .NET.
