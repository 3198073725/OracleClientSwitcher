# Oracle 客户端切换器

用于 Windows 上自动发现并快速切换多个 Oracle Instant Client / 完整客户端。

## 已实现

- 从磁盘、Oracle 注册表项和现有 PATH 自动发现客户端
- 扫描设置中心可选择快速、标准或完整模式，指定盘符、固定目录和排除目录
- 启动时可使用带有效期的扫描缓存，支持强制重新扫描与中途取消，并显示每个客户端的发现来源
- 仅从 `oci.dll` 被动读取真实 Oracle 版本和 32/64 位，扫描时不运行客户端程序
- 区分 Instant Client、Basic Lite 和完整客户端
- 启动时诊断系统/用户 PATH、`ORACLE_HOME`、`TNS_ADMIN`，区分已生效、配置失效和未配置
- 对失效路径自动匹配最接近的可用客户端并标记“建议修复”
- “环境诊断/查看问题”窗口逐项展示作用域、当前值、问题原因和建议修改，并可复制报告
- 显示 SQL*Plus、`TNS_ADMIN` 与 `ORACLE_HOME` 状态
- 独立客户端验证中心，在匹配位数的隔离进程中实际加载 OCI，不影响主程序
- 分析 OCI 的直接依赖 DLL 和 VC++ 运行库，并标记无法定位的组件
- 安全执行 `sqlplus -V`，带超时终止和输出展示
- 解析 `tnsnames.ora` 顶层服务别名，可按需执行 `tnsping` 网络服务测试
- 可选择第三方 EXE，检查目标程序与 OCI 客户端的 32/64 位兼容性
- 验证结果可查看明细并复制完整报告
- 可将所选 Oracle 客户端仅注入指定应用进程，实现不修改 Windows 环境变量的隔离启动
- 隔离启动前检查目标程序位数，预览 PATH、`TNS_ADMIN`、`ORACLE_HOME`，并记录最近使用的应用
- 默认使用可视化表单管理 TNS 服务，可搜索、新增、复制、修改和删除连接，无需直接编辑整份文件
- 表单草稿与服务列表刷新相互隔离；切换服务、页签、重载或保存时会保护尚未应用的修改
- 高级 TNS 可视化支持 RAC 多地址、`FAILOVER`、`LOAD_BALANCE`、连接超时和重试
- 支持 TCP/TCPS 混合地址及 Wallet 目录，可直接执行 `tnsping` 和逐地址端口可达性测试
- 自动识别多地址、多别名和高级参数配置；简单表单与高级配置分离，避免简化编辑丢失内容
- 保留 `tnsnames.ora` 与 `sqlnet.ora` 源码编辑，修改状态和保存结果清晰可见
- 检查括号、引号、重复服务别名及非常规名称，双击问题可跳转到对应行
- 可从其他 `.ora` 文件导入，选择合并缺失服务或完整替换
- 可将 `tnsnames.ora`、`sqlnet.ora` 同步到多个客户端，覆盖前自动备份
- 支持固定公共 TNS 目录，切换 Oracle 客户端时保持同一个 `TNS_ADMIN`
- 一键切换当前用户或系统级环境变量
- 切换前展示 PATH、`ORACLE_HOME`、`TNS_ADMIN` 的新旧值和风险提示
- 变更页使用简洁摘要，点击“查看列表”后逐项展示 PATH 的新增、移除和顺序变化
- 环境变量事务式写入并逐项校验，失败时自动回滚
- 切换和恢复前自动备份，可查看完整备份历史并恢复任意记录
- 管理员操作结果自动回传主窗口，无需根据界面状态猜测是否成功
- 清理同一作用域内旧的 Oracle PATH 项，不改动其他软件的 PATH
- 系统级修改通过 Windows UAC 提权，程序本身默认不以管理员运行
- 可为客户端设置显示名称、收藏置顶、隐藏扫描结果和用途备注
- 记住上次选择、列表排序方式以及是否显示隐藏客户端
- 记录扫描、验证、切换、TNS 测试和隔离启动结果，提供日志查看与清理
- 一键生成包含系统、客户端、扫描设置和近期日志的 ZIP 诊断报告，自动隐藏用户名和用户目录
- 捕获 UI、后台任务和进程级异常，记录后显示友好错误信息
- 启动时检查 Windows/系统架构兼容性，并提示同目录中的更新版本

## 使用

双击 `dist` 目录中最新版本：

- `OracleClientSwitcher-2.0.0.exe`：轻量版，需要 .NET 8 Desktop Runtime。
- `OracleClientSwitcher-2.0.0-Portable.exe`：免运行库便携版，可直接运行。

首次启动会自动扫描，选择目标客户端后点击“切换到选中版本”。

新启动的命令行、IDE、数据库工具才会读取新环境；已经运行的程序需要重启。

对于 Instant Client，通常无需设置 `ORACLE_HOME`。只有旧程序明确依赖它时才勾选兼容选项。

## 构建

```powershell
dotnet publish .\OracleClientSwitcher\OracleClientSwitcher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\dist

dotnet publish .\OracleClientSwitcher\OracleClientSwitcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\portable
```
