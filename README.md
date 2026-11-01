# 桌面待办

一个简洁、透明的 Windows 桌面待办小组件。固定在工作区右上角，默认跟随桌面层级，普通窗口可以遮住它；也可临时置顶。数据保存在本地 JSON 文件中，无网络请求、数据库或额外包依赖。

## 下载运行

从 [Releases](https://github.com/eddyLfan/Desktop-TodoList/releases/latest) 下载 `DesktopTodo.exe`，放在可写文件夹中双击运行。也可以下载包含程序、图标和使用说明的便携 ZIP，解压后运行。

需要 **64 位 Windows 10 / 11 和 .NET Framework 4.8（或兼容的 4.8.1）**。程序可以单 exe 运行，ZIP 内的 `.exe.config` 是可选兼容配置；不需要安装器、联网账号或额外资源文件。系统需已安装上述框架，exe 不包含 .NET 运行时。

## 从源码构建

构建另需 Windows PowerShell 5.1。使用系统自带的 C# 编译器，无需安装 Visual Studio、.NET SDK 或 NuGet 包。

仓库只包含源码，不包含已编译的 exe。克隆或下载解压后，在项目目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -RunTests
.\outputs\DesktopTodo\DesktopTodo.exe
```

构建结果位于 `outputs\DesktopTodo`。如需连同已有数据一起移动程序，请保留整个文件夹。重复构建不会清空已有待办数据；更新正在运行的程序前，请先从托盘退出，或使用 `-OutputDirectory .\outputs\Preview` 构建到另一个目录。

GitHub Actions 在 Windows 上运行同一套构建和测试；成功后可从该次工作流的 Artifacts 下载 `DesktopTodo-Windows`，解压后直接运行 `DesktopTodo.exe`。推送 `vX.Y.Z` 标签会运行独立发布流程，检查通过后上传 exe、便携 ZIP 和 SHA-256 校验文件，再公开 Release。

## 使用

- 初始只有“默认”分类；`＋` 添加分类，右键重命名或删除。默认分类可重命名，删除其他分类时，事项移入默认分类。
- `＋ 添加事项` 新增待办；勾选完成，已完成区可折叠。事项的 `⋯` 菜单可编辑、移动或删除，删除后可短时撤销。
- DDL 可选，只填日期时默认 23:59。进入 24 小时、1 小时和逾期阶段时，倒计时、颜色和轻吐槽逐渐变化；提醒可跨分类跳转，不主动弹系统通知。
- 图钉切换始终置顶；`⋯` 或托盘菜单可调整底板透明度、浅深主题、动效和开机启动。
- 隐藏或关闭主窗会收进托盘；双击托盘图标或再次打开程序即可恢复。选择“退出”停止运行。
- `Ctrl+N` 添加事项，`Ctrl+,` 打开设置。

开机启动默认关闭，仅在主动启用后写入当前用户的启动项。移动程序前先关闭开机启动，移动后再启用。提醒只在程序运行时生效，电脑睡眠后会按实际时间重新计算。

## 数据与归档

程序旁的 `data.json` 保存当前数据，每次修改后写入；`data.backup.json` 保存有效备份。也可通过 `--data-dir <可写目录>` 指定数据位置。手工编辑数据前请先退出程序，避免被运行中的保存覆盖。

已完成或已删除事项跨日后移入 `archive\YYYY-MM-DD.json`，归档保留当天及前 29 天；**未完成事项一直保留**。归档记录当时的分类名称。

数据损坏时会尝试恢复有效备份，并保留 `data.corrupt.json`；无可用备份时停止加载，不会自动重置为空列表。

## 开发与验证

`build.ps1 -RunTests` 执行 14 组存储与倒计时测试，以及浅深两主题的对话框、菜单离屏检查。测试数据和渲染图写入 `work`，不会操作正在运行的桌面窗口。测试覆盖和实机验证边界见 [验证记录](https://github.com/eddyLfan/Desktop-TodoList/blob/main/docs/verification.md)。

| 目录 | 内容 |
| --- | --- |
| `src` | C# / WPF 源码、运行配置和图标 |
| `tests` | 数据、倒计时、控件与菜单回归检查 |
| `docs` | 验证记录 |
| `work` | 本地测试与中间产物，不入库 |
| `outputs` | 编译成品，不入库 |

桌面显示使用正常的 WPF 透明窗口，观察 Explorer 桌面层级并调整自身位置，不修改桌面图标、壁纸或其他应用窗口。窗口管理思路参考 [PaperTodo](https://github.com/snownico0722/PaperTodo) 的置底与最小化消息处理，本项目独立实现，未引入其源码或依赖。
