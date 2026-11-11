# NovaDown — 自研多线程下载器

一个类似 IDM 的 Windows 下载器，核心引擎完全自研（不依赖 aria2 等现成核心）。当前版本 v0.3。

## 安装

发行包：`dist/NovaDown-0.3.0-setup.exe`（约 49MB，自包含运行时，无需预装 .NET）。
双击安装后从开始菜单/桌面启动；浏览器扩展位于安装目录 `extension\`，按使用说明加载。
重新构建安装包：先 `dotnet publish`（见 installer/setup.iss 注释），再运行 ISCC 编译 `installer/setup.iss`。

## 功能

### 下载引擎（NovaDown.Core）
- **多线程分段下载**：HTTP Range 切段并发（默认 8 段，小文件自动减段）
- **分段可视化**：任务列表每行下方实时显示各并发分段的进度格（悬停看分段明细），API 亦返回 segments 明细
- **任务删除**：暂停/完成/出错的任务可删除（保留磁盘文件，清除进度记录）；API `DELETE /api/tasks/{id}`
- **断点续传**：分段进度独立落盘（`*.novadown.json`），进程被杀/断电后重跑自动续传
- **限速**：全局限速（设置界面可调、实时生效）+ 单任务限速（CLI `--limit`），令牌桶算法
- **退避重试**：429/5xx/网络错误自动重试（指数退避 2/4/8/16/32s，尊重 Retry-After，上限 5 次），重试期间进度不丢
- **完整性校验**：完成后核对总字节数；续传用 ETag/If-Range 防服务器文件变更
- **安全防护**：仅允许 http/https；拦截 localhost/环回/私有/保留地址（含 DNS 解析结果）；重定向逐跳重新校验（防绕过）
- **探测兜底**：首选 `Range: 0-0` GET 探测（部分 CDN 会挂起 HEAD），HEAD 兜底，全程超时保护

### 控制中枢（本机 API）
桌面端内置 `http://localhost:{端口}/api` 服务（默认 28123），令牌鉴权（`X-Nova-Token`）：
- `GET  /api/health` 健康检查
- `GET  /api/extension-config` 下发接管规则（扩展配置的单一事实来源）
- `POST /api/tasks` `{url, referer?}` 创建并自动开始任务
- `GET  /api/tasks` / `GET /api/tasks/{id}` 列表与详情
- `POST|GET /api/tasks/{id}/pause|resume` 暂停/继续（GET 兼容 curl；浏览器 fetch 的 POST 自带 Content-Length: 0）

无界面环境可用 `NovaDown.Cli serve` 跑同一个 API（服务Agent/扩展，无需桌面）。

### 浏览器扩展（extension/，Chrome/Edge MV3）
- 浏览器开始下载 → 扩展按规则接管：取消浏览器下载 → 转交 NovaDown 多线程引擎（带 Referer 过防盗链）
- 接管规则（总开关/最小大小/扩展名/出错回退）**全部在 NovaDown 桌面端 ⚙ 设置里配置**，扩展启动时自动拉取
- **失败回退**：NovaDown 未运行 / 令牌错误 / 下载报错 → 自动回退浏览器普通下载，不会死循环
- 安装：`chrome://extensions` → 开发者模式 → 加载已解压的扩展程序 → 选 `extension/` 目录 → popup 里粘贴设置中的令牌

### Agent 插件（agents/mcp-server.mjs，MCP stdio）
让支持 MCP 的 AI Agent 直接管理下载：add_task / list_tasks / task_status / pause / resume。
```json
{ "mcpServers": { "novadown": { "command": "node",
    "args": ["<项目>/agents/mcp-server.mjs"],
    "env": { "NOVADOWN_PORT": "28123", "NOVADOWN_TOKEN": "<设置中复制>" } } } }
```

### 桌面端（NovaDown.App）
任务列表（进度/速度/状态/重试中提示）、添加/暂停/继续/打开目录、⚙ 设置（下载目录、全局限速、分段数、自动开始、API 端口、令牌、接管规则、出错回退）。

## 项目结构

```
NovaDown.sln
├── NovaDown.Core     # 引擎 + 任务中枢 + 控制API + 设置（无 UI 依赖）
│   ├── DownloadJob.cs      # 单任务：探测→分段并发→限速→重试→续传→校验
│   ├── SpeedLimiter.cs     # 令牌桶限速（可运行中调速）
│   ├── UrlGuard.cs         # SSRF 防护（协议/私网/DNS 解析校验）
│   ├── GuardedHttp.cs      # 手动重定向 + 逐跳安全校验
│   ├── TaskManager.cs      # 任务中枢（界面/API 共用）
│   ├── ApiServer.cs        # localhost 控制 API（HttpListener）
│   └── AppSettings.cs      # 设置持久化（%APPDATA%\NovaDown\settings.json）
├── NovaDown.App      # WPF 桌面端（含设置窗口）
├── NovaDown.Cli      # CLI：下载 / serve 无界面模式
├── extension/        # 浏览器扩展（MV3）
└── agents/           # MCP Agent 插件
```

## 运行

```powershell
C:\Users\asus\dotnet\dotnet.exe build NovaDown.sln -c Debug   # 用户级 SDK
NovaDown.App\bin\Debug\net8.0-windows\NovaDown.App.exe        # 桌面端（内置 API 自动启动）
NovaDown.Cli\bin\Debug\net8.0\NovaDown.Cli.dll serve          # 无界面模式
NovaDown.Cli\bin\Debug\net8.0\NovaDown.Cli.dll <url> [目录] [分段数] [--limit 字节/秒]
```

## 实测记录（2026-11-11，v0.2）

| 项目 | 结果 |
|---|---|
| 8 段并发 110MB | 11.3 MB/s，断点续传收尾 1.3s，字节数精确匹配 |
| 限速 2MB/s | 稳态 1.94–2.05 MB/s，精确钉住 |
| SSRF 拦截 | localhost / 127.0.0.1 / 192.168.1.1 / ftp 全部拦截 ✅ |
| API 全流程 | 令牌 401 / health / extension-config / 创建 / 暂停@22MB / 继续→Completed ✅ |
| MCP 插件 | 握手 / tools/list / add_task / list_tasks ✅ |
| 桌面端 | 启动 + 内置 API 自动运行 ✅ |

### 已知问题与教训
- OVH 短时间高频会 429（现已自动退避重试）
- npmmirror/Cloudflare 的 HEAD 会挂起 → 探测已改为 GET 优先
- http.sys 拒收无 Content-Length 的裸 POST（curl -X POST 不带头）→ 操作接口兼容 GET
- 教训：构建后必须确认"已成功生成"，tail 截断会漏看编译错误（本次曾因此带着旧二进制测试绕远路）

## 路线图（v0.3+）

1. 扩展通知中心（目前用图标角标）+ 接管白名单/黑名单界面
2. 下载队列与并发任务数上限
3. 视频嗅探（扩展配合解析媒体资源）
4. BT/磁力链（嵌 libtorrent 评估）
5. 安装包（Inno Setup）与开机自启
