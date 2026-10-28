# PicLoom

单人 Windows 本地生图工作台的 P0 框架。React + TypeScript + Vite / React Intl；.NET 10 Minimal API / Kestrel；EF Core SQLite；WinForms 托盘。生图走用户自带凭据（BYOK）的中转站：仓库内不含任何假上游，不内置 Key、不做本地推理，费用由用户与其上游服务商结算。

## 环境

- Windows x64，.NET **10 SDK**（不是只有 Runtime）。`dotnet --info` 应列出 10.0 SDK。
- Node.js 24 LTS 和 npm。
- 完整测试中的浏览器端到端用例使用本机 Google Chrome（独立无头实例，不读取用户配置）；无需安装浏览器扩展。应用本身仍可使用默认浏览器。
- 首次还原依赖需要网络；还原后本地数据管理不需要上游服务，但生图始终需要可用的中转站 Base URL 与 API Key。
- 本框架不是生产安装包，不承诺干净 Windows VM 的开箱安装验收。

## 安装、构建与测试

在仓库根目录执行（PowerShell 或 Git Bash）：

```sh
npm ci --prefix src/PicLoom.Web
npm run build --prefix src/PicLoom.Web
dotnet build PicLoom.sln
dotnet test PicLoom.sln
npm test --prefix src/PicLoom.Web -- --run
```

前端构建写入 `src/PicLoom.Api/wwwroot`，后端构建复制到输出目录。先构建 UI 再构建 .NET；仅 `dotnet build` 不会安装 npm 依赖。生成目录、数据库、测试产物不纳入 Git；npm 与 NuGet 锁文件保留。仓库 `NuGet.Config` 明确使用官方包源，不修改用户全局 NuGet 配置。

也可在 PowerShell 执行 `./scripts/verify.ps1`，它按上述顺序安装、构建与测试，并将原始输出和 TRX 保存到 `artifacts/verification`；任一命令失败即停止。

`dotnet test PicLoom.sln`：仅 `tests/PicLoom.LiveTests` 的 3 条真实冒烟（文生图 / 单参考图 `edits` / `count=2`）。未配置凭证时显示为 Skipped（不是失败），因此它**不是**构建门禁；真实回归请在配置好 BYOK 后手动跑。

真实上游调用由 `tests/PicLoom.LiveTests`（`Category=Live`）承担：三条真实调用 —— 文生图、单参考图 `edits`、`count=2` 多图，一次冒烟共 **3 次**上游调用。凭据优先取环境变量 `PICLOOM_LIVE_BASE_URL` / `PICLOOM_LIVE_API_KEY` / `PICLOOM_LIVE_MODEL`，其次取本机已保存的 BYOK（DPAPI 解密）；两者都没有时三条用例 **Skip 而不是失败**，不作为构建门禁。

```sh
dotnet test tests/PicLoom.LiveTests --filter Category=Live
```

排障看日志：`./scripts/logs.ps1 -TaskId <guid>`（不带 `-TaskId` 则看尾部若干行）。每个事件一行，字段分别为 `provider.dispatch`（`transport`/`path`/`size`/`count`/`quality`/`format`/`transparent`/`moderation`/`references`/`prompt` 前 60 字）、`provider.success`（`status`/`elapsedMs`/`images`/`bytes`）、`provider.failure`（`status`/`elapsedMs`/`errorKind`/`errorSnippet` 前 200 字）、`task.transition`（`phase`/`from`/`to`/`errorCode`）。API Key 永不入日志。

## 启动（同源 UI，推荐）

```sh
dotnet run --project src/PicLoom.Api --no-launch-profile -- --open-browser
```

浏览器由程序打开 `http://127.0.0.1:5278`，携带内存生成的短期一次性引导令牌；页面立即清理 fragment 并兑换 HttpOnly 会话。直接手输地址而没有会话会提示通过启动器打开，不会匿名开放 API。令牌不会输出至控制台；不要复制或分享含令牌的地址。

托盘方式：

```sh
dotnet run --project src/PicLoom.Host
```

托盘可重开界面或退出；关闭浏览器不会结束后台任务。Host 与 API 使用同一应用逻辑，Host 不实现 Provider 或存储。不要同时运行直接 API 和托盘实例占用相同端口/数据目录。

默认数据目录 `%LOCALAPPDATA%\PicLoom`，与代码/程序目录分开。可显式选择独立数据目录与端口：

```sh
dotnet run --project src/PicLoom.Api --no-launch-profile -- --PicLoom:Port 5279 --PicLoom:DataDirectory "D:/PicLoom Data" --open-browser
```

仅支持 `127.0.0.1` 绑定，不接受 `0.0.0.0` 或自定义 Kestrel endpoints；端口占用时失败，不扩大监听。文件独占锁防止两个后端同时使用数据目录。退出服务后备份整个数据目录（数据库及图片一起），不要在线复制 SQLite 主文件冒充完整备份。

## 开发模式（Vite 热更新）

终端 1：

```sh
npm run dev --prefix src/PicLoom.Web
```

终端 2，Git Bash：

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PicLoom.Api --no-launch-profile -- --open-browser --dev-ui
```

PowerShell 对应先执行 `$env:ASPNETCORE_ENVIRONMENT="Development"`，再运行相同的 `dotnet run` 命令。

Vite 固定监听 `127.0.0.1:5173`，将 `/api` 代理至 `127.0.0.1:5278`，浏览器仍只访问一个 origin。只有 Development 模式允许该固定代理 Host；生产模式拒绝它。没有通配 CORS，也不需要浏览器直接访问上游。`localhost` 与 `127.0.0.1` 不混用。

## 首次使用（BYOK）

1. 打开 Settings → API 连接，填写中转站 **Base URL**、**API Key** 与**模型名**（如 `gpt-image-1`，以中转站文档为准），点"保存"。保存后卡片显示"已配置"与 `sk-…` 掩码，Key 不回显明文。
2. 可选：点"测试连接"确认能否连上（只 `GET {base}/v1/models`，不产生生图计费、不新增任务）；返回"无法确认"时直接生成一次即可验证。
3. 回到任务页：模型下拉可选 `gpt-image-1` / `gpt-image-2` 或"自定义（填中转站模型名）"，写提示词、选尺寸（自动 / 24 挡 / 自定义比例 / 精确像素）后点"生成"，不需要预先创建模板、分辨率档案或任务组。未配置 Key 时"生成"按钮禁用并提示去配置，没有无凭据的试跑入口。
4. 图片由真实中转站返回：按实际字节落盘，MIME 由魔数判定，宽高以响应为准（不等于你请求的挡位大小）。
5. 上游已派发后失败（含 401 / 404 / 429 / 超时 / 断连）一律进入 `Unknown`，界面展示未知结果警告；后台**不自动发第二次 POST**、不换模型重试。
6. 修改或删除模板/分辨率后，任务历史保留创建时的快照。非空任务组删除返回冲突，避免意外级联删除历史。
7. 退出再启动，重新授权后可查询原任务及文件资产。中英切换不翻译用户内容、不改变 Provider/model ID 或状态枚举。

当前没有人工重试、取消操作入口或批量任务；`Cancelled` 状态与 Attempt 扩展字段只是领域骨架。

## SQLite 迁移

首次启动自动执行 EF Core migrations，不使用 `EnsureCreated` 跳过迁移。显式运行：

```sh
dotnet tool restore
dotnet ef database update --project src/PicLoom.Infrastructure
```

设计时路径遵循 Infrastructure 设计时工厂的环境变量 `PICLOOM_DATA_DIR`；运行时路径使用 `PicLoom:DataDirectory`，执行显式迁移时应指向同一目录并停止应用。新版本迁移前须备份；当前只有首个开发 schema，不提供生产升级/降级保证。

## 安全边界

- API 验证 loopback、Host、Origin、`Sec-Fetch-Site`；变更请求还需会话内存中的 CSRF 令牌。
- 引导令牌 2 分钟、单次兑换；会话进程内保存，8 小时到期，重启后需重新打开授权界面。
- HttpOnly、SameSite=Strict cookie；本地 HTTP 不设置 Secure，因此不能声称提供 HTTPS 传输保障。此模式不防同一 Windows 用户下的恶意进程。
- CSP 限制为本地资源，无远程脚本；静态 UI 同源提供，图片仅通过授权 API 读取。
- `ISecretStore` / Windows DPAPI CurrentUser 按 origin 加密保存中转站 Key，非 Windows 不退化明文；API Key 不回显明文、不入日志、不进数据库。
- 长期 Key 不写普通配置、日志、数据库、localStorage 或测试快照。浏览器仅允许持久化语言偏好。
- 文件原子写入不等于完成资产恢复协议；完整孤儿 reconcile、备份、安装升级和真实服务收费安全仍需后续 PoC。

模块与请求链路见 [architecture](docs/architecture.md)，后续工作见 [roadmap](docs/roadmap.md)。

## 本轮实测（2026-10-28）

- Windows x64；SDK 10.0.403，Node v24.22.1，npm 11.19.1。
- `dotnet build PicLoom.sln`：0 警告、0 错误。
- 测试口径：仓库仅保留 `tests/PicLoom.LiveTests`（3 条真实中转站调用：文生图、单参考图 `edits`、`count=2`；无凭证时 Skip，不作为构建门禁），日常回归以真实冒烟 + `scripts/logs.ps1` 日志字段核对为准。
- `npm ci`、`npm run build`：通过。`npm test -- --run` 当前没有前端测试文件（脚本为 `vitest run --passWithNoTests`），不构成前端回归证据。
- 首次 `dotnet ef database update`：成功创建 SQLite；`has-pending-model-changes`：无模型变化。
- `dotnet src/PicLoom.Host/bin/Debug/net10.0-windows/PicLoom.Host.dll --smoke-host --PicLoom:Port 5281 --PicLoom:DataDirectory artifacts/host-smoke`：启动、loopback 监听、停止成功。
- NuGet（含传递依赖）及 npm audit：当前源报告 0 已知漏洞。
- npm 对 esbuild postinstall 给出未授权脚本提示；未擅自放行脚本，实际构建仍通过。

原始输出/TRX 在 `artifacts/verification`，截图及行为结果在 `artifacts/browser-e2e`，这些是本地产物，不提交版本控制。外部 Chrome Connector 在操作中断连，改用独立无头 Chrome 验证，未把中断的人工浏览器流程计为通过。

未验证：真实托盘菜单点击、独立 OS 进程强杀恢复、干净 VM、安装升级/回滚、生产级 Provider 配额与计费矩阵、完整资产 reconcile 与密钥泄漏矩阵。以上不由构建成功或一次真实冒烟推定通过。
