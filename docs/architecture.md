# PicLoom P0 架构

## 模块边界

```text
Web ──同源 HTTP──> Api ──IPicLoomService──> Infrastructure ──> Core
Host ──启动/停止──> Api
tests/PicLoom.LiveTests ──> Api + 真实中转站（3 条真实调用，无凭证 Skip）
```

- `PicLoom.Core`：领域实体、不可变快照、状态机、Provider/密钥/资产/应用用例接口。无 EF 或 ASP.NET 依赖。
- `PicLoom.Infrastructure`：EF Core 映射与迁移、SQLite 用例实现、作业执行器、真实中转站 HTTP 传输（`openai-images`，handler 可注入以便测试）、本地文件与 DPAPI。
- `PicLoom.Api`：Kestrel loopback、启动迁移、独占数据目录、会话/来源校验、端点与稳定错误码、同源静态内容。业务校验不堆在 Endpoint。
- `PicLoom.Host`：WinForms 通知图标、单实例、重开浏览器、优雅退出；复用 API，不复制业务逻辑。
- `PicLoom.Web`：React/TypeScript、React Intl、中英消息、表单与轮询；只访问本地 API，无长期 Key。

## 请求链路

```text
Host/--open-browser
  → 创建内存引导令牌（2 分钟）
  → 浏览器 fragment → 立即清除 fragment
  → POST /api/session/bootstrap（单次兑换）
  → HttpOnly 会话 cookie + 内存 CSRF
  → POST /api/tasks
  → 校验本地模板、分辨率、组、provider（须在注册表中）与 model
  → SQLite 保存 Queued + 不可变快照
  → 202 + taskId；有界 Channel 唤醒
  → Worker 从 SQLite 读取 Queued
  → 原子持久化 Dispatching + Attempt
  → Running → 生图 POST 到中转站（自定义 HttpMessageHandler 管连接，不跟随重定向）
  → PNG 文件落盘 → SQLite Asset + Succeeded
  → UI GET 轮询 → 授权资产读取
```

数据库是任务事实来源，Channel 不是持久化队列。单 Worker 串行执行，启动恢复不依赖内存队列。数据库的任务状态使用并发检查；组关系使用外键；模板和分辨率快照不依赖来源行存续。模板修订递增，不提供独立修订版本浏览器。

验证一律走真实中转站：`tests/PicLoom.LiveTests`（`Category=Live`）三条用例（文生图 / 单参考图 `edits` / `count=2` 多图）共 3 次上游调用，凭据优先取环境变量 `PICLOOM_LIVE_BASE_URL`/`PICLOOM_LIVE_API_KEY`/`PICLOOM_LIVE_MODEL`，其次取本机已保存的 BYOK（DPAPI 解密）；两者都没有时三条用例 **Skip 而非失败**。逐任务核对用 `scripts/logs.ps1 -TaskId <guid>`。

## 状态机和不重发

```text
Queued → Dispatching → Running → Succeeded
  │          │             ├──→ Unknown
  ├→ Failed  ├──→ Unknown  └──→ Failed
  └→ Cancelled
```

`Dispatching` 可在明确失败时进入 `Failed`；P0 模糊失败只进入 `Unknown`。终态不允许回到 `Queued`。

- I/O 前保存 Attempt；如果这之后崩溃，即使无法确定是否真的发送，也保守进入 UNKNOWN。
- 启动时遗留 `Dispatching` / `Running` 转 `Unknown`；仅 `Queued` 自动继续。
- 已派发生图请求的连接异常、取消、坏响应等不进入自动重试，不能因为 HTTP 429/5xx 使用通用重试策略。
- `JobAttempt` 预留 IdempotencyKey、RemoteId、RetryOfAttemptId；P0 不公开人工重试或远端查询恢复，也不宣称本地键能保证远端幂等。
- 每次派发前写一行 `provider.dispatch`（`transport`/`path`/`size`/`count`/`references` 等），失败再写一行 `provider.failure`（`status`/`elapsedMs`/`errorKind`/`errorSnippet`）；日志行数即"是否只发了一次 POST"的证据，人工排障与自动断言用同一口径。API Key 永不入日志。

## 数据和资产

SQLite 使用 WAL，首次 migration 创建模板、分辨率、组、任务、尝试、资产。时间使用 UTC。`exactPixels` 和 `aspectClass` 保持互斥，另一模式字段必须为 null；这些只是本地通用配置，不代表已验证任何真实模型的参数能力。

文件存储与 SQLite 元数据分开。上游返回的字节以**魔数**判定 MIME（PNG/JPEG/WebP）并据此决定扩展名，保存实际宽高与 SHA-256；P0 不做真实图片缩放，缩略图与原图同字节。内部生成相对文件引用，读取时验证路径不越过资产目录。

`temp → move → 数据库提交` 缩小不完整写入窗口，但文件与数据库无法形成一个事务。崩溃后可能留孤儿文件，P0 不自动删除；完整 materialized/committed 标记、启动 reconcile 和重复内容块幂等是后续验收项。

删除非空任务组被拒绝，不级联删除任务历史。删除模板或分辨率不删除任务快照。未实现任务删除、资产清理、批量归档或导出。

## 本地安全边界

- 固定 IPv4 loopback 监听，不由 URL 参数指定上游或任意监听地址。
- Host/origin 精确校验；Development 仅允许明确 Vite 代理 origin，无通配 CORS。
- 所有 `/api` 请求需会话，只有 bootstrap 由一次性令牌授权；`/health` 仅返回非敏感运行状态，仍校验来源边界。
- 会话与 CSRF 只保留内存，浏览器刷新使用 HttpOnly cookie 重新获取 CSRF；服务重启失效。
- 引导失败 fail closed；CSP、nosniff、禁止 framing、不缓存敏感响应；未开放公网身份系统。
- DPAPI 绑定 CurrentUser 和 Provider origin；不提供明文降级或 API Key 读取接口。
- 此方案不是操作系统安全沙箱，不能防同用户恶意进程、扩展或受感染浏览器。

## Provider 扩展点

真实 Provider 接入时应独立实现能力档案、参数规划、传输、解析、错误归一化，依赖 `IImageProvider` 而非 Endpoint。OpenAI Images、Gemini Interactions、兼容中转是不同适配器，不能凭模型名或 URL 猜协议。使用 HttpClientFactory 并确保生图 POST 没有隐式自动重试。

当前注册表里只有 `openai-images` 一个适配器，`/health` 的 `providers` 也只列出它；产品内不再有进程内假 Provider，任何"能出图"的结论都必须来自真实中转站调用。

接入前补充能力版本、SSRF/重定向/下载限制、实际字节验证、RemoteId 查询恢复与人工新 Attempt 确认；未验证的模型/协议默认关闭。Host 的变更不影响 Provider 与领域层。
