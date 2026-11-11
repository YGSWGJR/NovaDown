#!/usr/bin/env node
// NovaDown MCP 服务器（stdio）— Agent 插件
// 让 AI Agent（支持 MCP 的客户端）直接管理 NovaDown 下载任务。
//
// 前置：NovaDown 正在运行（桌面端，或 CLI 的 serve 模式）。
// 配置示例（ZCode / Claude 等 MCP 客户端）：
// {
//   "mcpServers": {
//     "novadown": {
//       "command": "node",
//       "args": ["<本项目>/agents/mcp-server.mjs"],
//       "env": {
//         "NOVADOWN_PORT": "28123",
//         "NOVADOWN_TOKEN": "<在 NovaDown ⚙ 设置 中复制的令牌>"
//       }
//     }
//   }
// }

const PORT = process.env.NOVADOWN_PORT || '28123';
const TOKEN = process.env.NOVADOWN_TOKEN || '';
const BASE = `http://localhost:${PORT}/api`;

async function api(path, options = {}) {
  const resp = await fetch(BASE + path, {
    ...options,
    headers: { 'X-Nova-Token': TOKEN, ...(options.headers || {}) },
  });
  const text = await resp.text();
  let data = null;
  try { data = JSON.parse(text); } catch { /* 非 JSON */ }
  if (!resp.ok) throw new Error(data?.error || `HTTP ${resp.status}`);
  return data;
}

const TOOLS = [
  {
    name: 'novadown_add_task',
    description: '添加下载任务到 NovaDown（默认自动开始下载）',
    inputSchema: {
      type: 'object',
      properties: {
        url: { type: 'string', description: '文件直链（http/https）' },
        referer: { type: 'string', description: '可选 Referer 头，防盗链站点需要' },
      },
      required: ['url'],
    },
  },
  {
    name: 'novadown_list_tasks',
    description: '列出全部下载任务与进度',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'novadown_task_status',
    description: '查询单个任务状态',
    inputSchema: { type: 'object', properties: { id: { type: 'string' } }, required: ['id'] },
  },
  {
    name: 'novadown_pause',
    description: '暂停任务',
    inputSchema: { type: 'object', properties: { id: { type: 'string' } }, required: ['id'] },
  },
  {
    name: 'novadown_resume',
    description: '继续任务（也可用于出错后的重试）',
    inputSchema: { type: 'object', properties: { id: { type: 'string' } }, required: ['id'] },
  },
];

function write(obj) {
  process.stdout.write(JSON.stringify(obj) + '\n');
}

async function callTool(name, args = {}) {
  if (name === 'novadown_add_task') {
    return api('/tasks', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url: args.url, referer: args.referer }),
    });
  }
  if (name === 'novadown_list_tasks') return api('/tasks');
  if (name === 'novadown_task_status') return api(`/tasks/${args.id}`);
  if (name === 'novadown_pause') return api(`/tasks/${args.id}/pause`, { method: 'POST' });
  if (name === 'novadown_resume') return api(`/tasks/${args.id}/resume`, { method: 'POST' });
  throw new Error(`未知工具: ${name}`);
}

async function handleMessage(msg) {
  if (msg.method === 'initialize') {
    write({
      jsonrpc: '2.0',
      id: msg.id,
      result: {
        protocolVersion: '2024-11-05',
        capabilities: { tools: {} },
        serverInfo: { name: 'novadown', version: '0.2.0' },
      },
    });
    return;
  }
  if (msg.method === 'notifications/initialized') return; // 通知无需响应

  if (msg.method === 'tools/list') {
    write({ jsonrpc: '2.0', id: msg.id, result: { tools: TOOLS } });
    return;
  }

  if (msg.method === 'tools/call') {
    const { name, arguments: args } = msg.params || {};
    try {
      const data = await callTool(name, args);
      write({
        jsonrpc: '2.0',
        id: msg.id,
        result: { content: [{ type: 'text', text: JSON.stringify(data, null, 2) }] },
      });
    } catch (err) {
      write({
        jsonrpc: '2.0',
        id: msg.id,
        result: { content: [{ type: 'text', text: `出错：${err.message}` }], isError: true },
      });
    }
    return;
  }

  if (msg.id !== undefined) {
    write({ jsonrpc: '2.0', id: msg.id, error: { code: -32601, message: 'method not found' } });
  }
}

let lineBuf = '';
let pending = 0;      // 在途异步请求数（tools/call 可能要等下载）
let stdinEnded = false;

function maybeExit() {
  if (stdinEnded && pending === 0) process.exit(0);
}

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  lineBuf += chunk;
  let idx;
  while ((idx = lineBuf.indexOf('\n')) >= 0) {
    const line = lineBuf.slice(0, idx).trim();
    lineBuf = lineBuf.slice(idx + 1);
    if (!line) continue;
    let msg;
    try {
      msg = JSON.parse(line);
    } catch {
      continue;
    }
    // 等在途请求完成再退出，避免 tools/call 的响应被提前杀掉
    pending++;
    handleMessage(msg)
      .catch(() => {})
      .finally(() => {
        pending--;
        maybeExit();
      });
  }
});
process.stdin.on('end', () => {
  stdinEnded = true;
  maybeExit();
});
