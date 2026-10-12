(function (global) {
  "use strict";
  if (global.ANKT && global.ANKT.version === "2.2") return;
  var pending = new Map(), binding = null, hostOrigin = null, sequence = 0;
  var nativeBridge = !!global.ReactNativeWebView;
  var methods = new Set(["getPlatformInfo", "getAppInfo", "requestLogin", "requestOAuthLogin", "getGrantedPermissions", "requestPermission", "closeMiniApp", "reportSsoStatus"]);
  var requestTimes = [];
  var readyWaiters = [];
  var bff = null;
  var autoAttempt = null;
  var hostWindow = global.parent !== global ? global.parent : global.opener;
  function fail(code, message) { return Object.assign(new Error(message), { code: code }); }
  function validId(value, maximum) { return typeof value === "string" && value.length > 0 && value.length <= maximum && /^[A-Za-z0-9_-]+$/.test(value); }
  function configure(options) {
    var parsed = new URL(options && options.hostOrigin);
    if (parsed.protocol !== "https:" || parsed.origin !== options.hostOrigin || parsed.username || parsed.password) throw fail("INVALID_HOST_ORIGIN", "Exact HTTPS host origin required");
    if (binding && !nativeBridge && parsed.origin !== hostOrigin) throw fail("ALREADY_READY", "Host origin is immutable after handshake");
    hostOrigin = parsed.origin;
    if (options.bff) {
      var paths = typeof options.bff === "object" ? options.bff : {};
      bff = { sessionPath: paths.sessionPath || "/auth/ankt/session", startPath: paths.startPath || "/auth/ankt/start", profilePath: paths.profilePath || "/auth/ankt/profile", logoutPath: paths.logoutPath || "/auth/ankt/logout" };
      Object.keys(bff).forEach(function (key) {
        var path = bff[key];
        if (typeof path !== "string" || !/^\/(?!\/)/.test(path) || path.includes("\\") || path.includes("?") || path.includes("#") || new URL(path, global.location.origin).origin !== global.location.origin) { bff = null; throw fail("INVALID_BFF", "BFF paths must belong to this website"); }
      });
    }
    hello();
  }
  async function bffRequest(path, csrfToken, body) {
    if (!bff || typeof global.fetch !== "function") throw fail("BFF_REQUIRED", "Configure this website's backend session integration");
    var response = await global.fetch(path, { method: csrfToken ? "POST" : "GET", credentials: "same-origin", redirect: "error", cache: "no-store", headers: csrfToken ? { "Content-Type": "application/json", "X-CSRF-Token": csrfToken } : { Accept: "application/json" }, ...(csrfToken ? { body: JSON.stringify(body || {}) } : {}) });
    if (!response.ok) throw fail("BFF_ERROR", "The website session is unavailable");
    return response.json();
  }
  function validAuthorize(value, issuer, params) {
    var url = new URL(value), trusted = new URL(issuer);
    var granted = (url.searchParams.get("scope") || "").split(" ");
    if (url.protocol !== "https:" || url.username || url.password || url.origin !== trusted.origin || url.pathname !== "/oauth/authorize" || url.searchParams.get("response_type") !== "code" || url.searchParams.get("redirect_uri") !== params.redirectUri || url.searchParams.get("state") !== params.state || url.searchParams.get("nonce") !== params.nonce || url.searchParams.get("code_challenge") !== params.codeChallenge || url.searchParams.get("code_challenge_method") !== "S256" || params.clientId && url.searchParams.get("client_id") !== params.clientId || !granted.includes("openid") || granted.some(function (scope) { return !params.scopes.includes(scope); })) throw fail("INVALID_AUTHORIZATION", "Invalid OAuth authorization URL");
    return url.href;
  }
  async function login(params, currentSession) {
    if (params) return request("requestOAuthLogin", params);
    if (!bff) throw fail("BFF_REQUIRED", "Configure a BFF or provide its OAuth transaction");
    var session = currentSession || await bffRequest(bff.sessionPath);
    if (!validId(session.csrfToken, 256)) throw fail("INVALID_BFF", "A website CSRF token is required");
    var transaction = await bffRequest(bff.startPath, session.csrfToken, binding ? { appId: binding.appId, runtimeSessionId: binding.sessionId } : {});
    if (!validParams("requestOAuthLogin", transaction.sdkParams)) throw fail("INVALID_BFF", "Invalid website OAuth transaction");
    var authorization = nativeBridge || global.parent !== global || hostWindow && binding ? (await request("requestOAuthLogin", transaction.sdkParams)).authorizeUrl : transaction.authorizeUrl;
    global.location.assign(validAuthorize(authorization, session.issuer, transaction.sdkParams));
  }
  function autoLogin() {
    if (!nativeBridge && !hostWindow) return Promise.resolve({ status: "standalone" });
    // One attempt per page. Denial/errors require an explicit page reload/retry.
    if (autoAttempt) return autoAttempt;
    autoAttempt = (async function () {
      await ready();
      if (!binding.autoLogin) return { status: "disabled" };
      var session = await bffRequest(bff.sessionPath);
      if (session.authenticated && session.runtimeSessionId === binding.sessionId) {
        await request("reportSsoStatus", { status: "authenticated" });
        return { status: "authenticated" };
      }
      await login(null, session);
      return { status: "redirecting" };
    })().catch(async function (error) {
      if (binding && binding.autoLogin) {
        try { await request("reportSsoStatus", { status: "failed" }); } catch (_) { /* Preserve the original error. */ }
      }
      throw error;
    });
    return autoAttempt;
  }
  async function logout() {
    if (!bff) throw fail("BFF_REQUIRED", "Configure this website's backend session integration");
    var session = await bffRequest(bff.sessionPath);
    if (!validId(session.csrfToken, 256)) throw fail("INVALID_BFF", "A website CSRF token is required");
    return bffRequest(bff.logoutPath, session.csrfToken);
  }
  function hello() {
    var message = { type: "ANKT_MINI_APP_HELLO" };
    if (nativeBridge) global.ReactNativeWebView.postMessage(JSON.stringify(message));
    else if (hostOrigin && hostWindow) hostWindow.postMessage(message, hostOrigin);
  }
  function establish(message) {
    if (!message || message.type !== "ANKT_MINI_APP_READY" || !validId(message.appId, 80) || !validId(message.sessionId, 80) || !validId(message.nonce, 200)) return;
    if (binding && (binding.appId !== message.appId || binding.sessionId !== message.sessionId || binding.nonce !== message.nonce)) return;
    binding = { appId: message.appId, sessionId: message.sessionId, nonce: message.nonce, autoLogin: message.autoLogin === true };
    readyWaiters.splice(0).forEach(function (waiter) { clearTimeout(waiter.timer); waiter.resolve(); });
  }
  // Only the host's native script injection calls this callback. Nested frame
  // window messages cannot establish the native binding.
  global.__ANKT_NATIVE_READY__ = function (message) { if (nativeBridge) establish(message); };
  function ready() {
    if (binding) return Promise.resolve();
    hello();
    if (binding) return Promise.resolve();
    return new Promise(function (resolve, reject) {
      var waiter = { resolve: resolve, reject: reject, timer: null };
      waiter.timer = setTimeout(function () { var index = readyWaiters.indexOf(waiter); if (index >= 0) readyWaiters.splice(index, 1); reject(fail("BRIDGE_TIMEOUT", "ANKT handshake timed out")); }, 10000);
      readyWaiters.push(waiter);
    });
  }
  function validParams(method, params) {
    if (!params || typeof params !== "object" || Array.isArray(params)) return false;
    if (method === "reportSsoStatus") return params.status === "authenticated" || params.status === "failed";
    if (method === "requestOAuthLogin" && (typeof params.nonce !== "string" || !/^[\w~-]{16,256}$/.test(params.nonce) || !Array.isArray(params.scopes) || !params.scopes.includes("openid") || params.scopes.length > 4 || params.scopes.some(function (scope) { return !["openid", "profile", "email", "phone"].includes(scope); }))) return false;
    if (method === "requestLogin" || method === "requestOAuthLogin") return typeof params.redirectUri === "string" && params.redirectUri.length <= 2048 &&
      typeof params.state === "string" && params.state.length >= 16 && params.state.length <= 512 &&
      typeof params.codeChallenge === "string" && /^[A-Za-z0-9_-]{43}$/.test(params.codeChallenge) && params.codeChallengeMethod === "S256";
    if (method === "requestPermission") return typeof params.permission === "string" && params.permission.length <= 100 && /^[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+$/.test(params.permission);
    return Object.keys(params).length === 0;
  }
  function request(method, params) {
    params = params || {};
    if (!methods.has(method) || !validParams(method, params)) return Promise.reject(fail("INVALID_REQUEST", "Invalid bridge method or parameters"));
    return ready().then(function () {
      return new Promise(function (resolve, reject) {
        var now = Date.now(); requestTimes = requestTimes.filter(function (time) { return now - time < 60000; });
        if (requestTimes.length >= 30) return reject(fail("RATE_LIMITED", "Too many bridge requests"));
        requestTimes.push(now);
        var id = "req_" + now.toString(36) + "_" + (++sequence).toString(36);
        var message = { type: "ANKT_MINI_APP_REQUEST", id: id, method: method, params: params, appId: binding.appId, sessionId: binding.sessionId, nonce: binding.nonce };
        var encoded = JSON.stringify(message);
        if (encoded.length > 16384) return reject(fail("INVALID_REQUEST", "Bridge message too large"));
        var consentMethod = method === "requestOAuthLogin" || method === "requestLogin" || method === "requestPermission";
        var timer = setTimeout(function () { pending.delete(id); reject(fail("BRIDGE_TIMEOUT", "ANKT bridge timed out")); }, consentMethod ? 120000 : 10000);
        pending.set(id, { resolve: resolve, reject: reject, timer: timer });
        if (nativeBridge) global.ReactNativeWebView.postMessage(encoded);
        else if (hostOrigin && hostWindow) hostWindow.postMessage(message, hostOrigin);
        else { clearTimeout(timer); pending.delete(id); reject(fail("BRIDGE_UNAVAILABLE", "Configure the trusted ANKT host origin")); }
      });
    });
  }
  global.addEventListener("message", function (event) {
    if (nativeBridge) { if (event.source !== null && event.source !== global) return; }
    else if (!hostOrigin || event.source !== hostWindow || event.origin !== hostOrigin) return;
    var message;
    try { message = typeof event.data === "string" ? JSON.parse(event.data) : event.data; } catch (_) { return; }
    if (!message || typeof message !== "object") return;
    if (message.type === "ANKT_MINI_APP_READY") { if (!nativeBridge) establish(message); return; }
    if (!binding || message.type !== "ANKT_MINI_APP_RESPONSE" || message.appId !== binding.appId || message.sessionId !== binding.sessionId || message.nonce !== binding.nonce) return;
    var item = pending.get(message.id); if (!item) return;
    clearTimeout(item.timer); pending.delete(message.id);
    if (message.error) item.reject(fail(message.error.code || "BRIDGE_ERROR", message.error.message || "Bridge error"));
    else item.resolve(message.result);
  });
  global.addEventListener("pagehide", function () {
    if (!nativeBridge && hostOrigin && binding && hostWindow) hostWindow.postMessage(Object.assign({ type: "ANKT_MINI_APP_NAVIGATING" }, binding), hostOrigin);
    binding = null;
    pending.forEach(function (item) { clearTimeout(item.timer); item.reject(fail("SESSION_CLOSED", "Mini App navigated or closed")); }); pending.clear();
    readyWaiters.splice(0).forEach(function (waiter) { clearTimeout(waiter.timer); waiter.reject(fail("SESSION_CLOSED", "Mini App navigated or closed")); });
  });
  global.ANKT = Object.freeze({
    version: "2.2", configure: configure, ready: ready,
    getPlatformInfo: function () { return request("getPlatformInfo"); },
    getAppInfo: function () { return request("getAppInfo"); },
    requestLogin: function (params) { return request("requestLogin", params); },
    requestOAuthLogin: function (params) { return request("requestOAuthLogin", params); },
    auth: Object.freeze({ login: login, autoLogin: autoLogin, logout: logout, getPermissions: function () { return request("getGrantedPermissions"); } }),
    user: Object.freeze({ getProfile: function () { if (!bff) return Promise.reject(fail("BFF_REQUIRED", "Configure this website's backend session integration")); return bffRequest(bff.profilePath); } }),
    getGrantedPermissions: function () { return request("getGrantedPermissions"); },
    requestPermission: function (permission) { return request("requestPermission", typeof permission === "string" ? { permission: permission } : permission); },
    closeMiniApp: function () { return request("closeMiniApp"); }
  });
})(window);
