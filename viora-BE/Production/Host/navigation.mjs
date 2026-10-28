export function externalApproval(value, expectedUrl, { hostOrigin, parentOrigin, now = Date.now }) {
  if (!value || typeof value !== 'object' || Object.keys(value).sort().join(',') !== 'expiresAt,url' || value.url !== expectedUrl) throw new Error('External link unavailable');
  const url = new URL(value.url), expiresAt = Date.parse(value.expiresAt);
  if (url.href !== expectedUrl || url.protocol !== 'https:' || url.username || url.password || url.hash ||
      [hostOrigin, parentOrigin].includes(url.origin) || !Number.isFinite(expiresAt) || expiresAt <= now()) throw new Error('External link unavailable');
  return { url: url.href, expiresAt };
}

export function createExternalOpener({ request, document, expectedUrl, hostOrigin, parentOrigin, isStopped, now = Date.now }) {
  let pending = false;
  return async function open(signal) {
    if (pending || signal.aborted || isStopped()) return false; pending = true;
    try {
      const approved = externalApproval(await request(), expectedUrl, { hostOrigin, parentOrigin, now });
      if (signal.aborted || isStopped()) return false;
      return await new Promise(resolve => {
        const dialog = document.createElement('dialog'), title = document.createElement('h2'), address = document.createElement('p');
        const link = document.createElement('a'), cancel = document.createElement('button'), previous = document.activeElement;
        title.id = 'external-link-title'; title.textContent = 'Mở website ở tab mới?'; dialog.setAttribute('aria-labelledby', title.id);
        address.textContent = approved.url; link.textContent = 'Mở website'; link.href = approved.url; link.target = '_blank'; link.rel = 'noopener noreferrer';
        cancel.textContent = 'Hủy'; dialog.append(title, address, link, cancel);
        let done = false;
        const finish = accepted => {
          if (done) return; done = true; clearTimeout(timer); signal.removeEventListener('abort', abort);
          dialog.removeEventListener('cancel', onCancel); link.removeEventListener('click', onConfirm); cancel.removeEventListener('click', onCancel);
          dialog.close(); dialog.remove(); if (previous?.isConnected) previous.focus(); resolve(accepted);
        };
        const abort = () => finish(false), onCancel = event => { event.preventDefault(); finish(false); };
        const onConfirm = event => {
          if (!event.isTrusted || signal.aborted || isStopped() || now() >= approved.expiresAt) { event.preventDefault(); finish(false); return; }
          finish(true);
        };
        const timer = setTimeout(abort, Math.min(15000, approved.expiresAt - now()));
        signal.addEventListener('abort', abort, { once: true }); dialog.addEventListener('cancel', onCancel);
        link.addEventListener('click', onConfirm); cancel.addEventListener('click', onCancel);
        try { document.body.append(dialog); dialog.showModal(); cancel.focus(); } catch { finish(false); }
      });
    } finally { pending = false; }
  };
}
