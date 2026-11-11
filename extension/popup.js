const $ = (id) => document.getElementById(id);

async function refresh() {
  const { token = '', port = 28123, enabled = true } =
    await chrome.storage.local.get({ token: '', port: 28123, enabled: true });
  $('token').value = token;
  $('port').value = port;
  $('enabled').checked = enabled;

  const status = $('status');
  if (!token) {
    status.textContent = '⚠ 尚未配对：请粘贴 NovaDown 设置中的令牌';
    return;
  }
  status.textContent = '检测中…';
  try {
    const resp = await fetch(`http://localhost:${port}/api/health`, {
      headers: { 'X-Nova-Token': token },
    });
    if (!resp.ok) throw new Error();
    const data = await resp.json();
    status.textContent = `✅ 已连接 ${data.name} v${data.version}`;
  } catch {
    status.textContent = '❌ 未连接（请先启动 NovaDown）';
  }
}

$('save').addEventListener('click', async () => {
  await chrome.storage.local.set({
    token: $('token').value.trim(),
    port: parseInt($('port').value, 10) || 28123,
    enabled: $('enabled').checked,
  });
  refresh();
});

refresh();
