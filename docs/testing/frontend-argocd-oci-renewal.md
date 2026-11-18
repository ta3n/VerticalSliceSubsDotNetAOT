# OCI registry, Argo CD và E2E gia hạn trên OrbStack

Kiểm tra ngày **2026-11-18 (Asia/Ho_Chi_Minh)** trên context **orbstack**. Application
`subscription-management` ở namespace `argocd`, Helm release name `subs`, namespace ứng dụng `subscriptions`.
Argo CD **v3.5.6**, chart `argo/argo-cd` **10.11.1**. Kết quả cuối: **Synced / Healthy**, operation **Succeeded**,
10 pod ứng dụng Ready. Cluster tiếp tục chạy sau kiểm thử.

## Release và 6 image digest

Chart OCI: `oci://localhost:30501/charts/subscription-management`, version **0.2.0-dev.20261117175330**.

Chart digest: `sha256:b51af58c1265528f88fff1644e12d9673ea46a2bb3d5a84a3b5d7ecbc1b5ec19`.

Tất cả repository bên dưới có prefix `localhost:30501/` và tag `oci-20261118`.
`values-release.yaml` được đóng gói bên trong chart, pin đủ 6 digest; manifest dùng dạng `repository:tag@sha256:...`.
Đã đối chiếu digest từ registry (cả tag và digest), Deployment/init container và `imageID` thực tế trong pod.
Application đặt `imagePullPolicy: Always` cho đủ 6 image.

| Repository                         | Digest                                                                    |
|------------------------------------|---------------------------------------------------------------------------|
| `subscription-management`          | `sha256:dfe86273c44142b94b4856fc34afdd2d8a82a8a7fef8b4d3144d4688d5fc8c9a` |
| `subscription-management-migrator` | `sha256:5546acbd0b7a2a7ac380e7fe4afbf0b22a4406bfb2150c776e7f55dad6943f92` |
| `subscription-management-billing`  | `sha256:df46b46069a81b51cf703bfa72a0db698d172f05dd1d8332d6b6b3cc746e84fd` |
| `subscription-management-frontend` | `sha256:405a9a7e8db827401813802015084e8063a7524c0f394d25e901cc092ec3d2e2` |
| `subscription-management-payments` | `sha256:bf256a0a919d7e3c652e26c8125a2b283281482ae4ea619984821ff286d4f1d8` |
| `subscription-management-relay`    | `sha256:e06a09464e3d9fca3fac733b6b1ce3de354a99cfd22ee3d619721e2ba45520f0` |

PostgreSQL, Redis, RabbitMQ và image tiện ích là dependency của cluster; “6 image” ở đây là 6 artifact ứng dụng
API, migrator, FE, Billing, Payments, Relay.

Registry chạy StatefulSet `oci-registry` trong namespace `registry`, PVC **5Gi**.
Host và node OrbStack truy cập `localhost:30501`; Argo repo-server dùng
`oci-registry.registry.svc.cluster.local:5000/charts`. Repository Secret bật OCI và HTTP.
Profile cluster tắt ServiceMonitor của Argo vì cluster hiện tại không có Prometheus operator CRDs.

## Chạy lại trên cluster hiện tại

Từ root repo, dùng kubectl, Helm, Task và Docker/OrbStack đã cài:

```bash
kubectl config use-context orbstack
DOCKER_CONTEXT=orbstack task argocd:install REGISTRY_MODE=cluster
DOCKER_CONTEXT=orbstack task gitops:deploy REGISTRY_MODE=cluster TAG=oci-20261118
DOCKER_CONTEXT=orbstack task app:test REGISTRY_MODE=cluster
```

`gitops:deploy` build/push 6 image, tạo registry nếu cần, publish chart mới có digest, apply Application rồi chờ
**Synced + Healthy đúng version vừa publish**. Lần chạy mới tạo chart version mới; muốn thay đổi source hãy chọn tag
mới.
Application theo dõi range `>=0.2.0-0`, tự sync, prune và self-heal; giữ tên chart version bất biến trong registry.
`task up` dành cho tạo stack kind riêng. Profile hiện tại dùng Deployment thường, không bật Argo Rollouts/canary hay
monitoring stack.

| Thành phần       | URL                                                       |
|------------------|-----------------------------------------------------------|
| FE qua Kong      | http://127.0.0.1:30080/ hoặc http://subs.localhost:30080/ |
| API qua Kong     | http://127.0.0.1:30080/api/                               |
| Argo CD qua Kong | http://argocd.localhost:30080                             |
| Registry HTTP    | http://localhost:30501/v2/                                |

`/api` được Kong bỏ prefix; các route API cũ vẫn hoạt động. Billing, Payments, Relay và RabbitMQ không có public
Ingress.
Xem mật khẩu Argo admin tại máy bằng `task argocd:open KONG_HTTP_PORT=30080`; không ghi mật khẩu vào báo cáo.

## Bàn giao Helm sang Argo CD và giữ dữ liệu

Argo dùng annotation tracking để giữ nguyên `app.kubernetes.io/instance=subs` trong selector.
`gitops:adopt` apply Application, giữ Helm history và các tài nguyên/PVC hiện có. Không xóa database hoặc namespace.
Hai Secret lịch sử Helm `sh.helm.release.v1.subs.v1`, `sh.helm.release.v1.subs.v2` vẫn còn.
Chúng là lịch sử trước bàn giao, không mô tả trạng thái do Argo đang quản lý; không chạy Helm upgrade/rollback đồng thời
với auto-sync.

Đã giữ nguyên UID và volume của PVC PostgreSQL (`14ed160c-1777-46a4-a688-9ca98fd6a3af`)
và RabbitMQ (`901ef92a-8437-4bd9-a928-aa7e4483b20a`). Ba ca E2E từ lần deploy Helm trước vẫn có trạng thái
PAID/VOID/VOID.
Backup trước bàn giao gồm Helm Secret, PVC JSON và `pg_dump -Fc` của subscriptions/billing/payments, lưu quyền riêng tư
tại
`/tmp/subs-argocd-handoff.MPNhr1` (đường dẫn cũng ghi trong `.task/argocd-handoff-backup-dir`).
Backup tạm trên máy, chưa có bản sao ngoài máy; không commit nội dung Secret/backup.

Hai lỗi startup được sửa ở chart và xác minh trong rollout thực tế:

- fsGroup khi remount PVC làm cookie Erlang có quyền group, khiến RabbitMQ từ chối khởi động.
  Dùng `fsGroupChangePolicy: OnRootMismatch` và init container chạy cùng UID để đưa cookie hiện có về mode 600.
  Giữ nguyên nội dung cookie và dữ liệu; mode thực tế đã kiểm tra là `600 999 999`.
- Headless Service chưa có DNS khi broker chưa Ready. Init container của Billing/Payments/Relay chờ cổng AMQP trước khi
  chạy ứng dụng,
  tái sử dụng image PostgreSQL client đã có. Các pod mới khởi động thành công sau broker.

Đã kiểm thử self-heal bằng cách scale frontend từ 2 xuống 1 replica; Argo tự đưa về **2 replica Ready** trong 60 giây.
Pod mới vẫn chạy đúng digest FE. Nếu cần rollback, chọn chart OCI đã kiểm chứng, cập nhật Application đến version đó
và bỏ range tự theo dõi mới trong thời gian rollback; giữ PVC và đối soát saga đang chạy.
Hai chart trung gian `0.2.0-dev.20261117174501` và `0.2.0-dev.20261117174711` chưa có đủ bản sửa startup, không dùng làm
mốc rollback.
Chưa thực hiện rollback sống hay diễn tập restore backup.

## E2E qua FE và Kong

Tạo ba đăng ký mới bằng form FE, vào chi tiết, chọn **Gia hạn tự động** và xác nhận.
Cả ba POST `/api/subscriptions/{id}/renewals` trả **202 Accepted**. UI kiểm tra bằng **Kiểm tra lại** và **Làm mới**.
HTTP, gRPC, RabbitMQ và database đều chạy thật trong cluster; gateway thanh toán là bộ mô phỏng của repo.

| Ca         | Giá trước thuế | Hóa đơn USD | Saga      | Hóa đơn | Đăng ký | Payment events                   |
|------------|---------------:|------------:|-----------|---------|---------|----------------------------------|
| Thành công |             10 |          11 | Completed | PAID    | ACTIVE  | ChargeRequested → ChargeCaptured |
| Từ chối    |           6000 |        6600 | Declined  | VOID    | PAUSED  | ChargeRequested → ChargeDeclined |
| Timeout    |           9500 |       10450 | TimedOut  | VOID    | PAUSED  | ChargeRequested → ChargeCanceled |

| Ca         | Subscription ID                      | Invoice ID                           |
|------------|--------------------------------------|--------------------------------------|
| Thành công | 241bf731-3558-450e-9890-ae179b6c0383 | 63353877-daac-498a-820d-092c553831eb |
| Từ chối    | 5fa27717-d572-4c75-9249-2bbb5f0f6ca7 | 556864d3-22ec-4f60-b7de-d8001408f873 |
| Timeout    | 1b963400-b242-4f69-b03b-cda01bdf271c | af7a7ed2-4bdd-4d48-941a-43163049fd56 |

Assert qua API/Kong: mỗi đăng ký đúng một hóa đơn, trạng thái saga/hóa đơn/đăng ký, amount và USD.
Đọc payment snapshot/event stream bằng port-forward riêng tới Payments Service trong cluster:
status Captured/Declined/Canceled, version 2, đúng hai event theo thứ tự version 1/2,
invoice ID/subscription ID/amount/currency/periodEnd của snapshot và ChargeRequested khớp hóa đơn.
Port-forward kiểm thử đã đóng sau khi hoàn tất.

Giữ timeout thật **30 giây**; ChargeCanceled được ghi sau ChargeRequested **34,425626 giây**.
Lượt refresh trước deadline vẫn thấy trạng thái chờ; refresh sau khi xử lý đạt TimedOut.
Các HTTP 404 tạm thời ở endpoint renewal trước khi saga nhận message được FE xử lý thành chờ; không có lỗi JavaScript
runtime.

Artifacts trong workspace (được gitignore):

- `output/playwright/argocd-release-verification.json`: chart, digest và imageID thực tế.
- `output/playwright/argocd-renewal-results.json`: kết quả assert và payment events.
- `output/playwright/argocd-browser-requests.txt`: request/response từ browser.
- `output/playwright/argocd-renewal-{completed|declined|timedout}.png`: UI ở trạng thái cuối.

Kiểm tra thêm: Helm lint default/local/cluster, digest helper self-check, task dry-run chọn đúng cluster profile/port,
in-cluster readiness/plans qua `app:test`, `git diff --check`, Argo operation và PVC identity.
Không chạy lại toàn bộ .NET/frontend unit suite vì lượt này chỉ đổi deployment/config; E2E dùng chính 6 image đã push.

## Giới hạn

Registry HTTP vô danh và credential ứng dụng local chỉ phù hợp cluster tin cậy; chưa có TLS/auth cho production.
Argo CD có đăng nhập riêng, nhưng FE/API chưa có authentication/authorization. Không public stack này ra Internet.
Chưa kiểm thử registry remote, cluster nhiều node, gateway thu tiền thật hoặc phục hồi khi mất máy/PVC registry.
