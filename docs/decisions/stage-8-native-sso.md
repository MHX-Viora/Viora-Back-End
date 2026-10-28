# Stage 8 native container and SSO gate

2026-10-23. User requested Android/iOS implementation after closing Web.
The existing approved dedicated-shell architecture remains authoritative.

## Container implementation slices

1. Shared exact-origin/route and native-envelope policy + adversarial tests.
2. Local Expo native module, Android WebView: origin-scoped AndroidX message
   listener, `isMainFrame` and actual URL validation; no addJavascriptInterface.
   Deny media/location/file chooser/download/new windows/TLS errors; no global
   Social permission or cookie changes. Unsupported WebView messaging fails closed.
3. Same isolated view on iOS using WKScriptMessage.frameInfo.isMainFrame plus
   securityOrigin/current URL, nonpersistent website data and explicit delegates.
   File chooser denial requires iOS 18.4+; public geolocation denial is available
   from iOS 27. This candidate conservatively stays unavailable below iOS 27;
   the application's global deployment target and Social permission stay unchanged.
4. Native adapter/controller integration and lifecycle tests, then Android debug
   build/device proof. iOS source/type checks are separate from Mac/Xcode proof.
   Each slice changes at most five authored files; packaging separate from logic.

Native container is experimental and disabled until device proof. Ordinary RN
WebView onMessage is not a trusted-origin boundary. Native callbacks attach origin
and main-frame metadata from platform APIs, never from partner-supplied JSON.
Only the trusted shell sends the existing private protocol. No Social token,
partner secret, cookie or PKCE verifier is injected into a document/navigation URL.

## SSO P0 remains unresolved

No new broker, credential, callback change or Public backend is approved here.
Native Public SDK and Confidential browser-to-WebView session handoff need actual
issuer/browser/partner proof. System browser cookies and WebView cookies are not
assumed shared. An SSO publication must not silently open as an authenticated
native success. Existing server native gate remains until these requirements and
device security evidence are satisfied. No cross-platform acceptance from types.

## Sources checked

- [AndroidX WebViewCompat](https://developer.android.com/reference/androidx/webkit/WebViewCompat): exact allowed origins and native main-frame callback.
- [Expo module API](https://docs.expo.dev/modules/module-api/): view props/events and UI-thread methods.
- [Apple frameInfo](https://developer.apple.com/documentation/webkit/wkscriptmessage/frameinfo): native source frame metadata.
- [WebKit iOS 18.4 changes](https://webkit.org/blog/16574/webkit-features-in-safari-18-4/): upload delegate only available starting iOS 18.4.

Actual build/runtime results and unresolved environment requirements belong in
`docs/handoffs/developer-platform-stage-8-native.md`.
