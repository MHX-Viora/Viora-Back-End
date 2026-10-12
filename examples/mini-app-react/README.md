# Website React tích hợp ANKT

Frontend tham chiếu chạy với Node BFF ở `../mini-app-oidc`, hoặc ASP.NET ở `../mini-app-oidc-dotnet`. Các route `/auth/ankt/*` phải cùng origin với website, được proxy tới backend; callback không được trả HTML của SPA.

```powershell
Copy-Item .env.example .env.local
# Điền SDK URL và host origin HTTPS do ANKT cung cấp.
npm ci
npm run dev
npm run build
```

Vite proxy mặc định trỏ tới Node BFF `http://127.0.0.1:4310`. Để dùng backend khác, đặt `$env:PARTNER_BFF_ORIGIN` trước khi chạy Vite. Production dùng reverse proxy HTTPS cùng origin cho tài sản React và `/auth/ankt/*`; không triển khai Vite dev server.

`VITE_ANKT_SDK_URL` là `<issuer>/mini-app-sdk/ankt-mini-app.js`; `VITE_ANKT_HOST_ORIGIN` là origin của frontend ANKT. Đây là metadata công khai. Client secret, PKCE verifier, access/ID token chỉ ở backend; không đưa vào biến `VITE_*`.

SDK configure BFF một lần, Auto Login khi nằm trong runtime ANKT, nút login khi mở website riêng, kiểm tra phiên trước khi lấy profile, logout riêng website. Nếu iframe/cookie bị chặn, mở website đối tác bên ngoài và dùng nút login. Không tự lặp redirect khi người dùng từ chối.

Thiết lập ANKT và kiểm thử phát hành: [SETUP.md](../../docs/mini-app-partner/SETUP.md).
