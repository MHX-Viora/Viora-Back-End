// Native port is installed by an origin-scoped platform API. This transport
// never accepts origin/isMainFrame claims from a partner window message.
export function connectNativeParent(window, parentOrigin) {
  const native = window.ANKTProductionHost;
  if (window.parent !== window || window.top !== window || typeof native?.postMessage !== 'function') return null;
  let stopped = false, terminalSent = false;
  const parent = { postMessage(value, origin) {
    if (origin !== parentOrigin) return;
    // Shell cleanup removes inbound listeners before its final close/error
    // notification. Permit one terminal receipt, never a new launch/status.
    if (stopped && (terminalSent || !['host.closed', 'host.error'].includes(value?.type))) return;
    const data = JSON.stringify(value);
    if (data.length <= 16384) {
      if (stopped) terminalSent = true;
      native.postMessage(data);
    }
  } };
  return {
    parent,
    listen(receive) {
      native.onmessage = event => {
        if (stopped || typeof event?.data !== 'string' || event.data.length > 16384) return;
        let data; try { data = JSON.parse(event.data); } catch { return; }
        if (data && typeof data === 'object' && !Array.isArray(data)) receive({ data, source: parent, origin: parentOrigin });
      };
    },
    dispose() { stopped = true; native.onmessage = null; },
  };
}
