(function (global) {
  "use strict";
  if (global.ANKT && global.ANKT.version === "1.0") return;
  var pending = new Map();
  var sequence = 0;
  function request(method, params) {
    return new Promise(function (resolve, reject) {
      if (!global.ReactNativeWebView) return reject(new Error("ANKT bridge is unavailable"));
      var id = "req_" + Date.now().toString(36) + "_" + (++sequence).toString(36);
      var timer = setTimeout(function () { pending.delete(id); reject(new Error("ANKT bridge timeout")); }, 10000);
      pending.set(id, { resolve: resolve, reject: reject, timer: timer });
      global.ReactNativeWebView.postMessage(JSON.stringify({ version: "1.0", id: id, type: "request", method: method, params: params || {} }));
    });
  }
  global.addEventListener("message", function (event) {
    var message;
    try { message = typeof event.data === "string" ? JSON.parse(event.data) : event.data; } catch (_) { return; }
    if (!message || message.version !== "1.0" || message.type !== "response") return;
    var item = pending.get(message.id); if (!item) return;
    clearTimeout(item.timer); pending.delete(message.id);
    if (message.success) item.resolve(message.data);
    else item.reject(Object.assign(new Error(message.error && message.error.message || "Bridge error"), { code: message.error && message.error.code }));
  });
  global.ANKT = {
    version: "1.0",
    ready: function () { global.dispatchEvent(new CustomEvent("miniapp.ready")); return Promise.resolve(); },
    app: { getInfo: function () { return request("app.getInfo"); }, getTheme: function () { return request("app.getTheme"); }, close: function () { return request("app.close"); }, openExternalUrl: function (url) { return request("app.openExternalUrl", { url: url }); } },
    device: { getPlatform: function () { return request("device.getPlatform"); } },
    auth: { getSessionStatus: function () { return request("auth.getSessionStatus"); } }
  };
})(window);
