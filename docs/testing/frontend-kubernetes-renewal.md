# Frontend và gia hạn tự động trên Kubernetes

Đã kiểm tra ngày **2026-11-18 (Asia/Ho_Chi_Minh)** trên context **orbstack**, Kubernetes 1.36.5 arm64,
Helm 4.3.2, chart ứng dụng **0.2.0**, Kong chart **0.24.0**. Release `subs`, namespace `subscriptions`.
Đây là triển khai Helm trực tiếp trên cluster local, không dùng các dịch vụ Aspire trên host.

Báo cáo này ghi nhận lần deploy Helm trực tiếp trước đó. Cluster hiện đã chuyển sang
[OCI registry + Argo CD, kèm E2E mới](frontend-argocd-oci-renewal.md);
dùng workflow trong báo cáo mới cho cluster hiện tại, không chạy Helm rollback song song Argo auto-sync.

## Thành phần và route

Chart triển khai API Native AOT + migrator, FE tĩnh Nginx, Billing/Payments/Relay JIT, PostgreSQL, Redis và RabbitMQ.
PostgreSQL và RabbitMQ dùng PVC. API bật outbox và gọi `http://subs-subscription-management-billing:8081` qua gRPC
HTTP/2;
Relay gọi API bằng Service trong cluster. Billing và Payments có database riêng `billing` và `payments`.

| Route qua Kong                                                            | Upstream                       | Xử lý path                        |
|---------------------------------------------------------------------------|--------------------------------|-----------------------------------|
| `/` và `/assets/*`                                                        | frontend Service → Nginx :8080 | Giữ path                          |
| `/api/*`                                                                  | API Service                    | Bỏ `/api`                         |
| `/subscriptions`, `/plans`, `/invoices`, `/health`, `/scalar`, `/openapi` | API Service                    | Giữ path để tương thích script cũ |

Hai kiểu host đều hoạt động: `subs.localhost` và host-less (`127.0.0.1`). Các route dùng KongPlugins hiện có:
correlation ID, rate limiting và giới hạn payload. Không expose Billing, Payments, Relay hay RabbitMQ bằng Ingress.
FE không chứa URL backend trong bundle: trình duyệt luôn gọi `/api` cùng origin.

Nginx chạy UID/GID 101, root filesystem chỉ đọc, chỉ `/tmp` được ghi. `index.html` trả `Cache-Control: no-store`;
asset có hash được cache immutable. Asset không tồn tại trả 404, không trả HTML thay thế.

## Chạy lại trên OrbStack

Từ root repo, với Docker/OrbStack, kubectl, Helm và Task đã cài:

```bash
release_tag=cluster-20261118
DOCKER_CONTEXT=orbstack task image:build REGISTRY=docker.io/library TAG="$release_tag"

helm repo add kong https://charts.konghq.com --force-update
helm upgrade --install kong kong/ingress --kube-context orbstack \
  -n kong --create-namespace --version 0.24.0 -f deploy/kong/values.yaml --wait --timeout 8m

helm upgrade --install subs deploy/helm/subscription-management --kube-context orbstack \
  -n subscriptions --create-namespace \
  -f deploy/helm/subscription-management/values-cluster.yaml \
  --set image.tag="$release_tag" --wait --timeout 10m

helm test subs --kube-context orbstack -n subscriptions --logs
kubectl --context orbstack -n kong port-forward svc/kong-gateway-proxy 9080:80 --address 127.0.0.1
```

Mở **http://127.0.0.1:9080**. OrbStack dùng chung image store giữa Docker và Kubernetes.
Nếu dùng kind, phải push image vào registry của repo (`task dev`/`task up`) hoặc `kind load docker-image` trên đúng
cluster.
Cluster nhiều node/remote cần registry mà các node có thể pull; không dùng tag chỉ có trên máy host.

Phiên kiểm thử đã build và deploy tag `cluster-e2e-20261117`, release revision **2**. Lượt nâng cấp giữ database/PVC
và đăng ký đã tạo trước đó; các pod cũ phục vụ trong lúc pod mới khởi động. Có thể đổi tag theo mỗi release như ví dụ
trên.

`values-cluster.yaml` là profile local gọn, có NetworkPolicy, không cần monitoring/Argo CRDs.
`values-local.yaml` giữ workflow kind đầy đủ với monitoring, HPA và Argo Rollouts.
Task build/push hiện xử lý đủ **6 image**; `app:deploy` và `app:publish` dùng chung `deploy/registry/image-values.sh`
để pin toàn bộ digest. Thiếu hoặc sai bất kỳ digest nào sẽ dừng trước khi deploy/publish.

## Secret, database và tài nguyên

- Credential mặc định trong chart chỉ dành cho local. Dùng `postgresql.auth.existingSecret` (key `postgres-password`)
  và `s2s.rabbitmq.auth.existingSecret` (keys `rabbitmq-password`, `connection-string`) cho môi trường được quản lý.
  `connection-string` là AMQP URI đầy đủ của broker trong cluster; không đưa secret vào image/ConfigMap/bundle FE.
- Init container của Billing/Payments dùng PostgreSQL client tạo database nếu thiếu, với session advisory lock để chống
  race
  khi nhiều replica khởi động. Với database external do DBA quản lý, tạo sẵn `billing`/`payments` và đặt
  `s2s.provisionDatabases=false`.
- Relay chạy cùng migrator init container như API, nên chỉ drain khi schema/outbox đã tồn tại. Migrator có advisory lock
  và không xóa dữ liệu.
- Chart tính connection budget của API và cả ba dịch vụ, gồm pod surge khi upgrade. Budget mặc định 150 < 197
  connections khả dụng;
  profile HPA local dùng `postgresql.maxConnections=300`. Render bị từ chối nếu budget vượt giới hạn.
- RabbitMQ là một node có PVC, chưa phải cụm broker HA. Đổi password trong values không thay thế quy trình rotation
  tài khoản của PostgreSQL/RabbitMQ đã có dữ liệu; quản lý rotation trước rồi restart các client dùng Secret.

NetworkPolicy chỉ cho Kong/test pod vào FE; API vào Billing gRPC; Relay vào API; các dịch vụ vào PostgreSQL/RabbitMQ.
Đã kiểm tra pod không có label của release: gọi qua Kong được, nhưng gọi trực tiếp FE bị chặn cả ba lần;
pod `helm test` có label phù hợp truy cập FE và các health endpoint thành công.

## Kết quả E2E qua giao diện

Tạo cả ba đăng ký bằng form FE, mở chi tiết, bấm **Gia hạn tự động** và xác nhận. Cả ba POST
`http://127.0.0.1:9080/api/subscriptions/{id}/renewals` trả **202 Accepted**. Không mock HTTP/gRPC/broker/database.
Gateway thanh toán là bộ mô phỏng sẵn có của repo; các dịch vụ thực sự chạy trong pod.

| Ca         | Giá trước thuế | Hóa đơn sau thuế | Saga      | Hóa đơn | Đăng ký | Payment events                   |
|------------|---------------:|-----------------:|-----------|---------|---------|----------------------------------|
| Thành công |         10 USD |           11 USD | Completed | PAID    | ACTIVE  | ChargeRequested → ChargeCaptured |
| Từ chối    |       6000 USD |         6600 USD | Declined  | VOID    | PAUSED  | ChargeRequested → ChargeDeclined |
| Timeout    |       9500 USD |        10450 USD | TimedOut  | VOID    | PAUSED  | ChargeRequested → ChargeCanceled |

Đã kiểm tra UI qua **Kiểm tra lại** / **Làm mới** và assert dữ liệu qua API/Kong. Payment snapshot/event stream được
đọc bằng port-forward tới Payments Service trong cluster; kiểm tra cả invoice ID, subscription ID, amount và currency.
Timeout giữ cấu hình thật **30 giây**. 404 tạm thời khi saga chưa nhận message được FE xử lý thành trạng thái chờ;
console chỉ ghi các HTTP 404 này, không có lỗi JavaScript runtime.

| Ca         | Subscription ID                      | Invoice ID                           |
|------------|--------------------------------------|--------------------------------------|
| Thành công | 06b91bb3-0998-46bf-9d51-c4dbaad5b73a | 4f620e17-f114-4276-8e20-4e4fe4b740d9 |
| Từ chối    | c7f4c1ca-996a-4482-af82-037c62d5e3d7 | 6f279a49-cc20-4d59-a160-9e8fc590c219 |
| Timeout    | b4e05131-e565-4c4f-8730-6b228fa9a19f | 8cc69f53-7595-4598-a6bd-bc432db4ef66 |

Các bản ghi kiểm thử được giữ lại; không sửa dữ liệu cũ. Mở `/#subscriptions/{id}` để xem lại.
Ảnh `output/playwright/cluster-renewal-{completed|declined|timedout}.png` và file `cluster-renewal-results.json`
nằm trong workspace kiểm thử, không commit.

Kiểm tra bổ sung đã đạt: `task app:lint` (default/local/cluster + digest helper self-check), backend-only chart render,
server-side dry-run của manifest, từ chối connection budget sai, encoding AMQP credential đặc biệt,
`task image:build` cho đủ 6 image, `helm test`, 16 frontend tests và TypeScript/production build.
Kong `/api` + legacy paths, host-specific route, cache headers và asset 404 đã được assert trực tiếp.

## Rollback và giới hạn

```bash
helm history subs --kube-context orbstack -n subscriptions
helm rollback subs <revision> --kube-context orbstack -n subscriptions --wait --timeout 10m
```

Đã chạy rollback **server dry-run** tới revision 1; release đang chạy vẫn ở revision 2. Chọn revision có đủ các dịch vụ
gia hạn, theo dõi các saga đang xử lý và giữ PVC. Không xóa namespace/PVC để rollback. Nếu quay về chart cũ không có
outbox consumer, phải drain/đối soát các gia hạn đang xử lý trước khi dừng các dịch vụ.

Chưa kiểm thử thực tế Argo CD/OCI registry trên cluster này (không cài Argo/monitoring) hoặc gateway thanh toán ngoài
repo.
Cấu hình local hiện chưa có xác thực và chứng thư TLS cho domain; không public môi trường này trực tiếp ra Internet.
