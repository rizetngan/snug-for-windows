/* Snug Browser Zoom: no tabs/host permission, URLs, titles, or page content. */
const HOST = "com.rizetngan.snug.zoom";
const VERSION = 1;
let port = null;
let retryTimer = null;
let retryDelay = 1000;

function browserKind() {
  const brands = navigator.userAgentData?.brands || [];
  return brands.some(b => b.brand.includes("Microsoft Edge")) ||
    navigator.userAgent.includes("Edg/") ? "edge" : "chrome";
}

async function activeZoom(windowId) {
  const win = await chrome.windows.get(windowId);
  const tabs = await chrome.tabs.query({windowId, active: true});
  if (tabs.length !== 1 || !Number.isInteger(tabs[0].id)) throw new Error("No active tab");
  const tabId = tabs[0].id;
  const factor = await chrome.tabs.getZoom(tabId);
  if (!Number.isFinite(factor) || factor <= 0) throw new Error("Invalid zoom");
  // A tab may change while getZoom is pending. Never mix its identity with another page.
  const confirm = await chrome.tabs.query({windowId, active: true});
  if (confirm.length !== 1 || confirm[0].id !== tabId) throw new Error("Active tab changed");
  return {windowId, tabId, factor, focused: win.focused === true};
}

async function handleRequest(request) {
  if (!request || !Number.isInteger(request.id)) return null;
  const fail = {id: request.id, ok: false};
  try {
    let state;
    if (request.op === "focus") {
      const win = await chrome.windows.getLastFocused();
      if (!win.focused || !Number.isInteger(win.id)) return fail;
      state = await activeZoom(win.id);
      if (!state.focused) return fail;
    } else if (request.op === "read") {
      if (!Number.isInteger(request.windowId)) return fail;
      state = await activeZoom(request.windowId);
    } else if (request.op === "set" || request.op === "reset") {
      if (!Number.isInteger(request.windowId) || !Number.isInteger(request.tabId)) return fail;
      state = await activeZoom(request.windowId);
      if (!state.focused || state.tabId !== request.tabId) return fail;
      const factor = request.op === "reset" ? 0 : request.factor;
      if (!Number.isFinite(factor) || (request.op === "set" && (factor < 0.25 || factor > 5))) return fail;
      // The final command addresses the actual tab ID, never a foreground shortcut.
      // If a tab changes during the API call, the replacement tab is not modified.
      const focused = await chrome.windows.get(request.windowId);
      const active = await chrome.tabs.query({windowId: request.windowId, active: true});
      if (!focused.focused || active.length !== 1 || active[0].id !== request.tabId) return fail;
      await chrome.tabs.setZoom(request.tabId, factor);
      state = await activeZoom(request.windowId);
      if (state.tabId !== request.tabId) return fail;
    } else {
      return fail;
    }
    return {id: request.id, ok: true, ...state};
  } catch (_) {
    return fail;
  }
}

function updateBadge(connected) {
  chrome.action.setBadgeText({text: connected ? "" : "!"}).catch(() => {});
  chrome.action.setTitle({title: connected ? "Snug Browser Zoom — connected" : "Snug Browser Zoom — waiting for Snug"}).catch(() => {});
}

function scheduleReconnect() {
  if (retryTimer !== null) return;
  retryTimer = setTimeout(() => {
    retryTimer = null;
    connect();
  }, retryDelay);
  retryDelay = Math.min(10000, retryDelay * 2);
}

function connect() {
  if (port !== null) return;
  if (retryTimer !== null) { clearTimeout(retryTimer); retryTimer = null; }
  try {
    const current = chrome.runtime.connectNative(HOST);
    port = current;
    current.onMessage.addListener(async request => {
      retryDelay = 1000;
      updateBadge(true);
      if (request?.kind === "ready") return;
      const response = await handleRequest(request);
      if (response !== null && port === current) {
        try { current.postMessage(response); } catch (_) {}
      }
    });
    current.onDisconnect.addListener(() => {
      // Consume the runtime error without persisting browser data.
      void chrome.runtime.lastError;
      if (port !== current) return;
      port = null;
      updateBadge(false);
      scheduleReconnect();
    });
    current.postMessage({kind: "hello", version: VERSION, browser: browserKind()});
  } catch (_) {
    port = null;
    updateBadge(false);
    scheduleReconnect();
  }
}

chrome.runtime.onInstalled.addListener(connect);
chrome.runtime.onStartup.addListener(connect);
chrome.tabs.onActivated.addListener(connect);
chrome.tabs.onZoomChange.addListener(connect);
chrome.windows.onFocusChanged.addListener(connect);
connect();