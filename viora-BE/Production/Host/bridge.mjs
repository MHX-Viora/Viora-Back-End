const channel = 'ankt.miniapp', permissions = ['platform.getContext', 'platform.close'];
const record = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);

export function createProductionBridge({ origin, source, appId, validate, send, close, onConnected = () => {}, onClosed = () => {}, onInvalidated = () => {}, now = Date.now }) {
  let ready = false, paused = false, disposed = false, closing = false, generation = 0, inFlight = 0;
  let actions = new AbortController(), timestamps = []; const seen = new Set();
  function cancel() { generation++; actions.abort(); actions = new AbortController(); }
  function reset() { if (!disposed) { ready = false; cancel(); } }
  function dispose() { if (disposed) return; disposed = true; ready = false; actions.abort(); generation++; seen.clear(); timestamps = []; }
  function invalidate() { if (!disposed) { dispose(); onInvalidated(); } }
  function reply(data, ok, value) {
    try { send({ channel, version: data.version, type: 'response', id: data.id, ok, ...(ok ? { result: value } : { error: { code: value } }) }); }
    catch { invalidate(); }
  }
  async function handle(event) {
    const data = event.data;
    if (disposed || !event.source || event.source !== source() || event.origin !== origin || !record(data) ||
        data.channel !== channel || ![1, 2].includes(data.version) || data.type !== 'request' || !uuid(data.id)) return;
    if (data.version !== 1) { reply(data, false, 'BRIDGE_PROTOCOL'); return; }
    if (!['channel', 'version', 'type', 'id', 'method'].every(key => Object.hasOwn(data, key)) ||
        Object.keys(data).some(key => !['channel', 'version', 'type', 'id', 'method'].includes(key)) || typeof data.method !== 'string' || data.method.length > 64) {
      reply(data, false, 'BRIDGE_PROTOCOL'); return;
    }
    if (seen.has(data.id)) { reply(data, false, 'BRIDGE_PROTOCOL'); return; }
    timestamps = timestamps.filter(time => time > now() - 60000);
    if (inFlight >= 8 || seen.size >= 1024 || timestamps.length >= 64) { reply(data, false, 'BRIDGE_BUSY'); return; }
    seen.add(data.id); timestamps.push(now());
    if (closing || paused) { reply(data, false, 'BRIDGE_FORBIDDEN'); return; }
    if (data.method !== 'bridge.handshake' && !permissions.includes(data.method)) { reply(data, false, 'UNSUPPORTED_METHOD'); return; }
    if (data.method !== 'bridge.handshake' && !ready) { reply(data, false, 'BRIDGE_FORBIDDEN'); return; }
    const current = generation; inFlight++;
    try {
      const context = await validate(actions.signal);
      if (disposed || generation !== current) return;
      if (closing || paused) { reply(data, false, 'BRIDGE_FORBIDDEN'); return; }
      if (!record(context) || Object.keys(context).sort().join(',') !== 'appId,environment,runtime' ||
          context.appId !== appId || context.environment !== 'Production' || context.runtime !== 'controlled-web') throw new Error('Invalid runtime');
      if (data.method === 'bridge.handshake') { ready = true; reply(data, true, { permissions: [...permissions] }); onConnected(); }
      else if (data.method === 'platform.getContext') reply(data, true, { appId, environment: 'Production', runtime: 'controlled-web' });
      else {
        closing = true; actions.abort(); await close();
        if (disposed || generation !== current) return;
        reply(data, true, null); dispose(); onClosed();
      }
    } catch {
      if (!disposed && generation === current) { reply(data, false, 'BRIDGE_FORBIDDEN'); invalidate(); }
    } finally { inFlight--; }
  }
  return { handle, reset, dispose,
    pause() { if (!disposed) { paused = true; cancel(); } },
    resume() { if (!disposed) { paused = false; cancel(); } },
    get connected() { return ready && !paused && !disposed; }
  };
}
