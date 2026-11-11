// NovaDown 接管助手 — service worker
// 流程：浏览器开始下载 → onCreated → 符合规则则取消并转交 NovaDown 本机 API；
// NovaDown 未运行 / 转交失败 / 下载报错 → 自动回退浏览器普通下载。
// 接管规则（开关、最小大小、扩展名、是否回退）统一由 NovaDown 桌面端设置下发。

const DEFAULT_PORT = 28123;
const CONFIG_TTL_MS = 10 * 60 * 1000;

let cachedConfig = null;

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function getLocalSettings() {
  return chrome.storage.local.get({ token: '', port: DEFAULT_PORT, enabled: true });
}

async function getAppConfig(token, port) {
  if (cachedConfig && Date.now() - cachedConfig._ts < CONFIG_TTL_MS) return cachedConfig;
  try {
    const resp = await fetch(`http://localhost:${port}/api/extension-config`, {
      headers: { 'X-Nova-Token': token },
    });
    if (!resp.ok) return null;
    const data = await resp.json();
    cachedConfig = { ...data, port, _ts: Date.now() };
    return cachedConfig;
  } catch {
    return null; // NovaDown 未运行
  }
}

async function getSession(key, fallback) {
  const obj = await chrome.storage.session.get({ [key]: fallback });
  return obj[key];
}

async function setSession(obj) {
  await chrome.storage.session.set(obj);
}

// 由 fallbackToBrowser 发起的下载要放行，避免二次接管造成死循环
async function isBypassId(id) {
  const list = await getSession('bypassIds', []);
  return list.includes(id);
}

async function addBypassId(id) {
  const list = await getSession('bypassIds', []);
  list.push(id);
  await setSession({ bypassIds: list.slice(-200) });
}

// 最近回退过的 URL 短期内不再接管（下载记录去重兜底）
async function recentlyFellBack(url) {
  const list = await getSession('fallbackUrls', []);
  const now = Date.now();
  const fresh = list.filter((x) => now - x.ts < 5 * 60 * 1000);
  await setSession({ fallbackUrls: fresh });
  return fresh.some((x) => x.u === url);
}

async function markFallback(url) {
  const list = await getSession('fallbackUrls', []);
  list.push({ u: url, ts: Date.now() });
  await setSession({ fallbackUrls: list.slice(-100) });
}

function setBadge(symbol) {
  try {
    chrome.action.setBadgeText({ text: symbol });
    chrome.action.setBadgeBackgroundColor({ color: '#2563eb' });
    setTimeout(() => chrome.action.setBadgeText({ text: '' }), 8000);
  } catch {}
}

async function fallbackToBrowser(url, message) {
  setBadge('!');
  if (!/^https?:/i.test(url || '')) return;
  if (await recentlyFellBack(url)) return;
  await markFallback(url);
  try {
    const id = await chrome.downloads.download({ url });
    await addBypassId(id);
  } catch {}
}

async function watchTask(taskId, url, token, port, cfg) {
  // 观察一段时间：进入下载/暂停/完成即算接管成功；出错则按配置回退
  for (let i = 0; i < 20; i++) {
    await sleep(3000);
    try {
      const resp = await fetch(`http://localhost:${port}/api/tasks/${taskId}`, {
        headers: { 'X-Nova-Token': token },
      });
      if (!resp.ok) continue;
      const task = await resp.json();
      if (task.state === 'Downloading' || task.state === 'Completed' || task.state === 'Paused') return;
      if (task.state === 'Error') {
        if (cfg.fallbackOnError) await fallbackToBrowser(url, 'NovaDown 下载出错，已回退浏览器下载');
        return;
      }
    } catch {}
  }
}

chrome.downloads.onCreated.addListener(async (item) => {
  try {
    const { token, port, enabled } = await getLocalSettings();
    if (!enabled || !token) return; // 扩展未配对或被用户关闭 → 普通下载
    if (await isBypassId(item.id)) return;

    const url = item.finalUrl && item.finalUrl !== 'about:blank' ? item.finalUrl : item.url;
    if (!/^https?:/i.test(url)) return;
    if (await recentlyFellBack(url)) return;

    const cfg = await getAppConfig(token, port);
    if (!cfg || !cfg.enabled) return; // 桌面端未运行或总开关关闭 → 普通下载

    // 接管规则：扩展名命中即接管；否则按文件大小判断（大小未知时接管）
    let size = 0;
    try {
      const [fresh] = await chrome.downloads.search({ id: item.id });
      size = fresh?.fileSize || 0;
    } catch {}
    let path = '';
    try {
      path = new URL(url).pathname.toLowerCase();
    } catch {
      return;
    }
    const extHit = (cfg.extensions || []).some((e) => e && path.endsWith(e.toLowerCase()));
    if (!extHit && size > 0 && size < (cfg.minSizeBytes || 0)) return;

    // 开始接管：取消浏览器下载，转交 NovaDown
    try {
      await chrome.downloads.cancel(item.id);
    } catch {}

    const resp = await fetch(`http://localhost:${port}/api/tasks`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Nova-Token': token },
      body: JSON.stringify({ url, referer: item.referrer || undefined }),
    });
    if (!resp.ok) throw new Error('HTTP ' + resp.status);
    const task = await resp.json();

    setBadge('↓'); // 已转交
    watchTask(task.id, url, token, port, cfg);
  } catch {
    // NovaDown 不可达 / 令牌错误 / 其他异常 → 转为普通链路
    await fallbackToBrowser(item.finalUrl || item.url, '');
  }
});

chrome.runtime.onInstalled.addListener(() => {
  chrome.action.setBadgeText({ text: '' });
});
