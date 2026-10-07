# Hồ sơ: chat, lời mời và CORS

## Sửa
- Program sử dụng WebCorsOrigins.Resolve, thêm exact origin https://mxh.ankt.vn cùng origin đã cấu hình. Giữ yêu cầu cấu hình Production, AllowAnyHeader/Method và AllowCredentials; không thêm wildcard.
- SocialRepository lấy notification mới, lưu cùng transaction quan hệ, commit/dispose rồi PublishAsync qua NotificationService (realtime và push). Dispatch lỗi được log, quan hệ không rollback hoặc trả lỗi giả sau commit.
- UserProfileFriendshipResponse bổ sung FriendshipId nullable tương thích; frontend dùng ID này để hủy lời mời/kết bạn.
- CreatePrivateConversation kiểm tra Friendship Accepted trước khi từ chối người nhận đã tắt AllowMessageEveryone; Pending/không là bạn vẫn bị chặn.

## Kiểm chứng
- ProfileSocialRegressionTests: preflight POST/DELETE/PUT, origin không tin cậy, commit/rollback/dispatch lỗi, lời mời trùng không phát lại, quyền chat, follow/unfollow.
- Toàn bộ Application.Tests: 54 đạt, 1 test PostgreSQL bỏ qua do thiếu môi trường.
- Preflight production ngày 2026-10-07 trả 204 nhưng không có header CORS từ mxh.ankt.vn. Chưa xác định từ response này liệu ASP.NET từ chối origin hay nginx đang chặn OPTIONS.

## Triển khai và kiểm tra
1. Triển khai backend và frontend mới.
2. Production vẫn phải có Cors__AllowedOrigins__0 và các origin admin/ứng dụng khác cần thiết; không thay thế bằng `*`.
3. Nginx phải chuyển OPTIONS tới API như các method khác. Nếu có nhánh `return 204` riêng cho OPTIONS mà thiếu CORS, bỏ nhánh đó; không để proxy strip Access-Control-Allow-* hoặc tạo hai header Allow-Origin.
4. Kiểm tra không đăng nhập, không thay đổi dữ liệu:

```bash
curl -i -X OPTIONS https://api.tvphapluat.com.vn/api/friends/783e5447-f513-42ea-85a3-8fa1961c3081 \
  -H 'Origin: https://mxh.ankt.vn' \
  -H 'Access-Control-Request-Method: DELETE' \
  -H 'Access-Control-Request-Headers: authorization'
```

Kỳ vọng 204 với Allow-Origin bằng https://mxh.ankt.vn, Allow-Credentials=true và Allow-Methods chứa DELETE. Làm tương tự POST cho /api/friends/request, /api/users/{id}/follow, /api/chat/conversations/private và PUT chấp nhận lời mời. Sau đó kiểm tra hai tài khoản: gửi lời mời nhận banner/danh sách, hủy lời mời, bỏ theo dõi, nhắn tin.

Tài liệu: [ASP.NET Core CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-8.0), [nginx reverse proxy](https://docs.nginx.com/nginx/admin-guide/web-server/reverse-proxy/).

Chưa triển khai server hoặc chỉnh nginx production trong phiên này. Không đổi DB/schema.

## Notification fetch follow-up (2026-10-07)

`/api/notifications?page=1&pageSize=20`: production OPTIONS GET returns 204 without CORS, and unauthenticated GET returns 401 without CORS. This reproduces the browser fetch failure. The existing exact-origin fix must be deployed; nginx must preserve CORS on both preflight and actual/error responses. Added GET preflight and 401 response-header regression coverage. Frontend provides an explicit retry button and Vietnamese connection error. No server changes were made during this follow-up.
