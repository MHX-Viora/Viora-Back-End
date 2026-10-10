(function (global) {
  "use strict";
  if (global.ANKT && global.ANKT.version === "2.0") return;
  var pending = new Map(), binding = null, hostOrigin = null, sequence = 0;
  var nativeBridge = !!global.ReactNativeWebView;
  var methods = new Set(["getPlatformInfo", "getAppInfo", "requestLogin", "getGrantedPermissions", "requestPermission", "closeMiniApp"]);
  var requestTimes = [];
  var readyWaiters = [];
  function fail(code, message) { return Object.assign(new Error(message), { code: code }); }
  function validId(value, maximum) { return typeof value === "string" && value.length > 0 && value.length <= maximum && /^[A-Za-z0-9_-]+$/.test(value); }
  function configure(options) {
    if (binding) throw fail("ALREADY_READY", "Host configuration is immutable after handshake");
    var parsed = new URL(options && options.hostOrigin);
    if (parsed.protocol !== "https:" || parsed.origin !== options.hostOrigin || parsed.username || parsed.password) throw fail("INVALID_HOST_ORIGIN", "Exact HTTPS host origin required");
    hostOrigin = parsed.origin;
  }
  function establish(message) {
    if (!message || message.type !== "ANKT_MINI_APP_READY" || !validId(message.appId, 80) || !validId(message.sessionId, 80) || !validId(message.nonce, 200)) return;
    if (binding && (binding.appId !== message.appId || binding.sessionId !== message.sessionId || binding.nonce !== message.nonce)) return;
    binding = { appId: message.appId, sessionId: message.sessionId, nonce: message.nonce };
    readyWaiters.splice(0).forEach(function (waiter) { clearTimeout(waiter.timer); waiter.resolve(); });
  }
  // Only the host's native script injection calls this callback. Nested frame
  // window messages cannot establish the native binding.
  global.__ANKT_NATIVE_READY__ = function (message) { if (nativeBridge) establish(message); };
  function ready() {
    if (binding) return Promise.resolve();
    return new Promise(function (resolve, reject) {
      var waiter = { resolve: resolve, reject: reject, timer: null };
      waiter.timer = setTimeout(function () { var index = readyWaiters.indexOf(waiter); if (index >= 0) readyWaiters.splice(index, 1); reject(fail("BRIDGE_TIMEOUT", "ANKT handshake timed out")); }, 10000);
      readyWaiters.push(waiter);
    });
  }
  function validParams(method, params) {
    if (!params || typeof params !== "object" || Array.isArray(params)) return false;
    if (method === "requestLogin") return typeof params.redirectUri === "string" && params.redirectUri.length <= 2048 &&
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
        var message = Object.assign({ type: "ANKT_MINI_APP_REQUEST", id: id, method: method, params: params }, binding);
        var encoded = JSON.stringify(message);
        if (encoded.length > 16384) return reject(fail("INVALID_REQUEST", "Bridge message too large"));
        var timer = setTimeout(function () { pending.delete(id); reject(fail("BRIDGE_TIMEOUT", "ANKT bridge timed out")); }, 10000);
        pending.set(id, { resolve: resolve, reject: reject, timer: timer });
        if (nativeBridge) global.ReactNativeWebView.postMessage(encoded);
        else if (hostOrigin && global.parent !== global) global.parent.postMessage(message, hostOrigin);
        else { clearTimeout(timer); pending.delete(id); reject(fail("BRIDGE_UNAVAILABLE", "Configure the trusted ANKT host origin")); }
      });
    });
  }
  global.addEventListener("message", function (event) {
    if (nativeBridge) { if (event.source !== null && event.source !== global) return; }
    else if (!hostOrigin || event.source !== global.parent || event.origin !== hostOrigin) return;
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
    binding = null;
    pending.forEach(function (item) { clearTimeout(item.timer); item.reject(fail("SESSION_CLOSED", "Mini App navigated or closed")); }); pending.clear();
    readyWaiters.splice(0).forEach(function (waiter) { clearTimeout(waiter.timer); waiter.reject(fail("SESSION_CLOSED", "Mini App navigated or closed")); });
  });
  global.ANKT = Object.freeze({
    version: "2.0", configure: configure, ready: ready,
    getPlatformInfo: function () { return request("getPlatformInfo"); },
    getAppInfo: function () { return request("getAppInfo"); },
    requestLogin: function (params) { return request("requestLogin", params); },
    getGrantedPermissions: function () { return request("getGrantedPermissions"); },
    requestPermission: function (permission) { return request("requestPermission", typeof permission === "string" ? { permission: permission } : permission); },
    closeMiniApp: function () { return request("closeMiniApp"); }
  });
})(window);
