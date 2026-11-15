# sharpRPA MCP 设计

本文定义 MCP 层的契约设计：工具分层、响应与错误模型、分页、异步任务、原生资源、状态与并发、安全、产物模型、版本演进。

实现基线：`b1161b8` 起步；P0 契约加固与产物观测、P1 持久化/策略/资源提示/异步任务已落地（23 个工具，stdio transport，`ModelContextProtocol` 2.4.0）。

## 1. 设计原则

1. **契约优先**：工具名和参数是公共 API，改名或改必填参数 = 破坏性变更。
2. **默认只读**：写入/破坏性工具必须显式标注 `readOnly=false`、`destructive=true`。
3. **输出有界**：任何工具的返回都可能进 agent 上下文，必须分页、截断或摘要。
4. **长任务不阻塞**：超过 ~1s 的操作返回 id，轮询获取结果。
5. **状态进程无关**：权威状态落库；进程内状态必须有恢复路径。
6. **结构化错误**：带错误码和修复提示，不依赖异常文本。
7. **派生数据可重建**：索引、摘要删了能重算；权威数据（录制、脚本）必须可迁移。

## 2. 工具分层

命名统一为 `domain_action`，按域分组：

| 域 | 工具 | 状态 |
| --- | --- | --- |
| `script` | `list_scripts`, `get_script`, `validate_script` | ✅ |
| | `create_script`, `update_script` | ✅ |
| `command` | `list_commands`, `describe_command` | ✅ |
| `run` | `run_script`, `get_run_status`, `get_run_log`, `cancel_run`, `list_runs` | ✅ |
| `record` | `record_start`, `record_event`, `record_stop`, `record_abort`, `list_recordings`, `get_recording` | ✅ |
| | `generate_script_from_recording` | ⬜ P2 |
| `target` | `find_targets`, `rebuild_target_index` | ✅ |
| `artifact` | `list_artifacts`, `get_artifact` | ✅ |
| `job` | `get_job`（`rebuild_target_index` 返回 jobId） | ✅ |

`create_script` / `update_script` 不只是便利：脚本目录默认在 `Documents`，位于 agent workspace 之外，没有这两个工具就只能依赖 `external_directory` 文件授权。

## 3. 统一响应信封

所有工具返回同一形状，agent 只需判断 `ok`：

```json
{ "ok": true,  "data": { }, "page": { "nextCursor": null, "total": 46 } }
{ "ok": false, "error": { "code": "NOT_IN_PROGRESS", "message": "...", "hint": "..." } }
```

错误码（闭集，新增不破坏兼容）：

| code | 触发场景 | hint 示例 |
| --- | --- | --- |
| `INVALID_ARGUMENT` | 缺参数、参数越界 | "actionType is required: click, type, navigate" |
| `NOT_FOUND` | 脚本/录制/运行/命令不存在 | "use list_scripts to see available scripts" |
| `ALREADY_EXISTS` | `create_script` 目标已存在 | "Use update_script to modify it." |
| `ALREADY_FINISHED` | 重复 `record_stop` / `run` 已取消 | "this recording is already committed" |
| `NOT_IN_PROGRESS` | `record_stop` 找不到会话 | "the session may have been recovered as incomplete" |
| `PARSE_FAILED` | 脚本无法反序列化 | "see validate_script for details" |
| `UNSUPPORTED_COMMAND` | 当前构建缺少该命令的平台实现 | "window/Excel commands need the Windows build" |
| `PERMISSION_DENIED` | 被策略文件拒绝 | "StartProcessCommand is denied by sharprpa.policy.json" |
| `TIMEOUT` | 运行或任务超时 | "increase runTimeoutSeconds in the policy" |
| `INTERNAL` | 未预期异常 | 日志见 stderr |

MCP 协议层的 `isError` 标志同时为 `true`，`content[0].text` 装的就是上面这个 JSON。

实现要点（已验证）：**必须返回 `CallToolResult{IsError=true, Content=[text]}`，不能靠抛异常**——SDK 会把任何异常替换为通用文案 `An error occurred invoking '<tool>'.`，错误码和 hint 会全部丢失。因此 `ToolResult.Fail(...)` 是返回值而非 throw，工具内部对预期错误一律 `return`。

另一个已验证行为：SDK 序列化时省略 `null` 属性，所以**分页末页的 `page` 只有 `total`，没有 `nextCursor` 字段**（字段缺失 = 最后一页），而不是 `"nextCursor": null`。

## 4. 分页

统一参数：`limit`（默认 50，最大 200）+ `cursor`（不透明字符串，服务端解析为 keyset/offset）。响应给 `page.nextCursor`，为 `null` 表示取完。

适用：`list_scripts`、`list_commands`、`list_recordings`、`list_runs`、`find_targets`。

特例：

- `get_run_log`：改为 `beforeSeq` / `limit`（按序号取窗口），而不是现在的"尾部 N 行"——回看历史时尾部语义会丢中间信息。
- `get_script`：加 `mode = summary | full`。`summary` 只返回变量表 + 每个顶层命令一行；`full` 才展开嵌套命令。500 条命令的全量拍平会直接撑爆上下文。

`total` 只在计数便宜时返回（SQLite `COUNT` 可接受），不做精确计数的场景省略该字段。

## 5. 同步与异步边界

| 模式 | 工具 | 说明 |
| --- | --- | --- |
| 异步（返回 id） | `run_script` | 已实现：返回 `runId`，轮询 `get_run_status` |
| 同步（快） | 其余全部 | 目标 < 1s |
| 需要降级为异步 | `rebuild_target_index` | 扫全部脚本 + 解压全部录制 blob，秒级；改为返回 `jobId` |
| 需要降级为异步 | `validate_script`（超大脚本） | 命令数 > 1000 时返回 `jobId` |

统一 `Job` 抽象：

```json
{ "jobId": "...", "kind": "rebuild_index|validate|generate_script", "status": "running|succeeded|failed" }
```

`get_job(jobId)` 返回状态与结果引用。MVP 阶段只有 `run` 和 `record` 有 id，可先不建独立 job 表，但**接口形状现在就要定死**，避免以后改返回类型。

## 6. MCP 原生能力：resources 与 prompts

当前 17 个工具全是 tool，浪费了协议原生能力，也让"读内容"和"做动作"混在一起。

**Resources**（agent 用 `resources/list` + `resources/read`，不需要包一层 JSON 字符串）：

```text
sharprpa://scripts                 脚本清单
sharprpa://script/{path}           单个脚本 XML
sharprpa://recordings              录制清单
sharprpa://recording/{id}          录制元数据 + 事件
sharprpa://targets?q=...           目标索引查询
sharprpa://artifact/{id}           截图 / trace / DOM 快照
```

**Prompts**（把多步流程固化成可复用模板）：

```text
optimize-script   输入 scriptPath → 引导 validate → find_targets → run → 读日志 → 修改 → 复验
record-flow       输入 source → 引导 record_start → 采集 → record_stop → 生成脚本 → 回放
```

收益：工具数量不再随功能线性增长，读写语义分离，agent 的工具选择更准。

## 7. 状态与并发

| 状态 | 位置 | 恢复策略 |
| --- | --- | --- |
| 脚本 | XML 文件 | 天然持久 |
| 录制事件 | 暂存文件 → SQLite | 启动 `CollectStaging` 回收（已实现） |
| 录制会话 `active` | 进程内字典 | 会话只属于一个 server 进程；进程退出后由回收逻辑接管 |
| 运行状态/日志 | **进程内**（`ScriptRunManager`，上限 50 条） | ⬜ 需落库，否则重启丢失 |
| 目标索引 | SQLite | `rebuild_target_index` 全量重建 |

明确的会话语义：**一个录制会话只属于启动它的 server 进程**。`record_stop` 找不到会话时返回 `NOT_IN_PROGRESS`，hint 提示可能已被回收为 `incomplete`（而不是笼统的"未知 id"）。

并发：SQLite 已开 WAL + `busy_timeout=5000`，UI 与 MCP 两进程可同时读写。同一 server 内多个 `run` 可并行，运行之间无共享可变状态（变量/实例都在各自 `ScriptRunner` 上）。

跨进程的运行状态查询：落库后天然支持；在此之前，`get_run_status` 只能查到本进程启动的运行。

## 8. 安全

**工具标注**（现有 `ReadOnly` / `Destructive`，需补齐）：

| 标注 | 工具 |
| --- | --- |
| `readOnly=true` | 所有 list/get/describe/validate/find |
| `destructive=true` | `run_script`, `record_start`, `record_stop`, `update_script`, `rebuild_target_index` |
| `idempotent=true` | `get_*`, `list_*`, `validate_script`, `find_targets`, `rebuild_target_index` |
| `openWorld=true` | `run_script`（执行外部程序/网络，结果不可预测） |

**策略文件**（workspace 内 `sharprpa.policy.json`，或通过 MCP `environment` 注入）：

```json
{
  "scriptRoots": ["C:\\Users\\me\\Documents\\sharpRPA\\My Scripts"],
  "allowedCommandTypes": [],
  "deniedCommandTypes": ["StartProcessCommand", "RunScriptCommand", "SMTPSendEmailCommand"],
  "runTimeoutSeconds": 300,
  "maxRecordingEvents": 200000
}
```

- `scriptRoots` 之外的路径一律 `PERMISSION_DENIED`，防止 `run_script` 执行任意文件。
- 命令级 allow/deny：执行前遍历命令树检查，命中即拒绝并给出 `UNSUPPORTED`/`PERMISSION_DENIED`。
- 超时由 `ScriptRunManager` 的 `CancellationTokenSource` 统一取消。

**审计**：每次 `run_script` / `record_*` 写一行到 `audit` 表（clientInfo.name、动作、参数摘要、时间、结果）。MCP `initialize` 已经拿到 `clientInfo`，应该透传下去。

**隐私**：录制事件里 `Metadata["sensitive"] = "true"` 的（密码框、令牌）不入库或替换为 `***`；键盘钩子采集阶段就要屏蔽，不能等落库。

## 9. 产物模型（P0）

agent 优化质量的上限取决于它能不能"看见"，这是当前最大的能力缺口。

```json
{
  "artifactId": "...",
  "runId": "...",
  "kind": "screenshot | trace | dom | log",
  "path": "Documents/sharpRPA/runs/<runId>/step-12.png",
  "mime": "image/png",
  "bytes": 183420,
  "step": 12,
  "createdAt": "..."
}
```

- 落盘目录：`Documents/sharpRPA/runs/<runId>/`，与 SQLite 同级。
- 工具：`list_artifacts(runId)`、`get_artifact(artifactId, maxBytes=1MB)`。
  - `maxBytes` 以内：text 类 inline 返回，二进制 base64 返回；
  - 超过：只返回 `path` + `bytes` + `encoding=omitted`。

采集来源实现状态：

| 来源 | 状态 |
| --- | --- |
| 运行日志 `run.log` | ✅ 每次运行必产 |
| Playwright `page.ScreenshotAsync` → `screenshot` | ✅ 运行结束时尽力采集（浏览器会话仍存活才有效） |
| Playwright `page.ContentAsync` → `dom` | ✅ 同上 |
| Playwright trace zip → `trace` | ⬜ P1，需在 `BrowserSession` 启用 tracing |
| 桌面 `User32Functions.CaptureWindow` → `screenshot` | ⬜ P1，需在运行结束时截取目标窗口 |
| `ScreenshotCommand` 输出自动登记 | ⬜ P1，需把命令产物回传到 run 上下文 |
| `resources/read`（`sharprpa://artifact/{id}`） | ⬜ P1，与 resources/prompts 一起做 |

## 10. 版本与兼容

- `serverInfo.version` 跟随仓库版本（当前 `0.0.9`）。
- 变更分级：删工具/改名/改必填参数 = **major**；新增可选参数或新工具 = **minor**；改描述、修错误码文案 = **patch**。
- 每个工具的入参/出参写成契约表（`docs/mcp-tools.md`），集成测试逐字段断言——现有的"工具名列表断言"只是第一层。
- 错误码是闭集：新增可以，改语义不行。

## 11. 当前实现与目标的差距

| 现状 | 问题 | 目标 | 优先级 |
| --- | --- | --- | --- |
| 21 工具平铺，全靠 tool | 读写混杂，工具数线性增长 | 分层 + resources/prompts | P1 |
| ~~返回形状不一~~ | ~~agent 要为同一实体写多套解析~~ | 统一信封 `{ok,data,page}` | ✅ |
| ~~`record_abort` 复用 `CancelResponse{cancelled}`~~ | ~~语义错位~~ | 统一信封，`{id, aborted, status}` | ✅ |
| ~~错误靠抛异常~~ | ~~无 code、无 hint~~ | 结构化 error + 闭集错误码；返回 `CallToolResult` 而非抛异常，另有 `GuardedMcpServerTool` 兜底 INTERNAL | ✅ |
| ~~`list_*` 无分页~~ | ~~大输出撑爆上下文~~ | `limit` + `cursor`，`page.nextCursor` 末页缺省 | ✅ |
| ~~`get_script` 永远全量拍平~~ | ~~500 命令脚本返回爆炸~~ | `mode=summary\|full` | ✅ |
| ~~`rebuild_target_index` 同步执行~~ | ~~秒级阻塞请求~~ | 返回 `jobId` + `get_job` 轮询 | ✅ |
| 脚本目录在 workspace 外 | agent 文件工具改不了脚本 | `create_script` / `update_script` | ✅ |
| 无产物概念 | agent 看不见 UI，无法判断"改对没改对" | `list_artifacts` / `get_artifact`；运行日志必产，浏览器截图/DOM 尽力采集 | ✅ |
| ~~运行状态仅内存~~ | ~~重启丢状态~~ | `run` / `run_log` 落库，启动时把中断的 running 标为 failed；`list_runs` 支持按状态过滤 | ✅ |
| ~~无策略与审计~~ | ~~任意路径可执行~~ | `sharprpa.policy.json`（scriptRoots 默认锁脚本目录 / deniedCommandTypes / runTimeoutSeconds）+ `audit` 表 | ✅ |
| 无 prompts | 多步流程靠 agent 自己摸 | `optimize-script` / `record-flow` | P2 |

## 12. 实施顺序

1. **P0 — 契约加固** ✅：统一信封 + 错误码 + 分页 + `get_script` 模式 + `create/update_script`，92 个测试同步迁移。
2. **P0 — 观测** ✅：`list_artifacts` / `get_artifact`，运行日志必产、浏览器截图/DOM 尽力采集，打通"执行 → 看见 → 修改 → 复验"闭环。
3. **P1 — 持久与策略** ✅：`run`/`run_log` 落库 + `list_runs`；`sharprpa.policy.json` 与 `audit` 表；resources 与 prompts；`rebuild_target_index` 异步化 + `get_job`。
4. **P2 — 录制闭环**：采集钩子 → `generate_script_from_recording` → 回放命中率报告。
