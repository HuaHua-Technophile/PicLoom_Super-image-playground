# PicLoom 后续阶段

P0 是可扩展开发框架，不是生产验收。进程内假 Provider 已下线，任何"能出图"的结论只能来自真实中转站调用（`tests/PicLoom.LiveTests`，`Category=Live`，3 次上游调用）；以下工作尚不可按现有冒烟结果宣称通过。

## P1：Provider 与数据安全 PoC

- 独立实现 OpenAI Images、Gemini Interactions、Generic OpenAI Images 的版本化能力档案与请求规划；不得混用字段语义。
- 先完成协议契约测试（不依赖真实网络）：认证、禁止字段、尺寸/MIME、429/5xx、连接断开、JSON/SSE 解析失败、缺图、重复 final；真实上游只由 `Category=Live` 冒烟覆盖，无凭证时 Skip。
- 真实和付费请求必须由用户明确授权密钥及预算；每个模型/协议/中转独立保存脱敏证据，失败不静默改模型/尺寸。
- UNKNOWN 人工处置：风险提示、新 Attempt 关联、可验证 RemoteId 的 GET 查询恢复；禁止自动生成第二 POST。
- 完成资产 temp/materialized/committed 协议、孤儿隔离与 reconcile、取消/批量归档/导出、离线数据管理与一致性备份。

## P2：DPAPI 与本地安全

- 基于现有 ISecretStore/CurrentUser 实现补设置页密码输入、只返回 isSet 的读取接口、origin 绑定确认、秘密删除与迁移。
- 使用测试 Canary 验证明文不进入数据库、日志、配置、导出、浏览器存储或崩溃快照；不提交 Canary 原文与快照。
- 验证 DPAPI 跨用户拒绝、损坏密文、不可用 fail closed、秘密权限；当前骨架不代表这些场景已验收。
- 针对 DNS rebinding、Origin/Host/CSRF、引导重放、过期、浏览器扩展和 loopback 企业策略补 Windows 专项测试。
- 真实 Provider 前实现 Base URL/SSRF、跨主机重定向、下载大小/超时/MIME/魔数限制与秘密不跨 origin 转发。

## P3：托盘体验与 Windows PoC

- 完善图标、中文/英文系统菜单、启动错误呈现、端口冲突、日志诊断与可访问性。
- 验证重复启动只重开界面、关闭浏览器后任务继续、系统注销/退出行为、睡眠恢复；不承诺系统级保活。
- Windows 11 x64 非管理员、中文及空格路径、浏览器企业策略、干净 VM 上反复冷启动测试。
- 无壳只有出现可复现阻断才评估直接 .NET WebView2 回退；不默认引入 Electron/Tauri。

## P4：安装、升级与发布

- win-x64 自包含目录、每用户签名 Inno Setup；安装目录与数据目录隔离。
- 完整依赖/素材许可证审查、SBOM 与漏洞检查；当前版本锁定不等于供应链完整验收。
- 更新签名独立信任根、迁移前一致性备份、升级中断、旧 schema 兼容及回滚、卸载保留数据。
- 通过干净 VM 3/3 安装、10/10 冷启动与真实中转站全流程冒烟（`Category=Live` 三条用例 + `scripts/logs.ps1 -TaskId` 核对 `provider.*` 与 `task.transition` 字段）；生产 Provider 证据另行验收。

任何未授权本地访问、模糊失败第二 POST、明文 Key 泄漏、篡改更新被接受或升级导致数据丢失都应停止发布。P0 不制作安装包、不接账号/支付/云同步/视频/本地模型/插件体系。
