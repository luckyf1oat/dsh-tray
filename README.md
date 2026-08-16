# dsh-tray — DeepSeek Harness Windows 托盘管家

DeepSeek Harness 的 Windows 系统托盘管家：启动 / 重启 / 停止 / 崩溃自动拉起，全部在托盘右键完成。不用开终端、不怕误关窗口，配合浏览器 APP 模式效果更佳！

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform: Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%2F11-blue.svg)]()
[![Language: C#](https://img.shields.io/badge/language-C%23-239120.svg)]()

> 本仓库在 [KAIbsb/dsh-tray](https://github.com/KAIbsb/dsh-tray) v1.1.3 基础上做了重新审查、修复与测试：修复了一处真实的启动/停止并发竞态（停止操作可能残留正在启动的 harness），并把 `UpdateCheck` 的纯函数辅助方法改为 public 以便测试。全部改动见 [交付报告](交付报告.md)。

## 功能特性

- **生命周期管理**：启动 / 重启 / 停止 / 退出，全部在托盘右键菜单完成
- **单击托盘图标**：未运行时自动启动并打开窗口，运行中直接打开窗口
- **状态图标**：运行中 = 蓝色鲸鱼；停止 = 黑 / 白鲸，随系统深浅色实时切换
- **崩溃自动重启**（可开关）：harness 意外退出自动拉起，带冷却防死循环
- **开机自启**（可开关）：写注册表 `HKCU\...\Run`，免管理员
- **原生系统右键菜单**：Win11 圆角主题样式，深色模式自动跟随
- **无终端窗口**：隐藏拉起 `node dsh web`，输出重定向到独立日志文件 `harness.log`
- **重启后自动刷新窗口**：重启完成自动刷新浏览器 APP 模式窗口
- **按需提权**：harness 若以管理员身份运行，托盘自动以管理员身份执行 kill（UAC 为「从不通知」时静默）
- **手动主题**：跟随系统 / 亮 / 暗（设置窗切换，存 ini `theme` 键）
- **自动更新**：发现新版时设置窗一键下载 + 校验（sha256）+ 部署提示
- **日志**：`%LOCALAPPDATA%\dsh-tray\tray.log`，超过 5MB 自动轮转

## 下载与安装

- 仓库内已附带预编译的 `dsh-tray.exe`（或从本仓库 Releases 下载）
- **单文件，零依赖**：无需安装任何运行时（Windows 10/11 自带 .NET Framework 4.8），双击即用
- **首次运行提示**：未签名的小工具会被 SmartScreen 提示「未知发布者」→ 点「更多信息」→「仍要运行」
- **升级**：下载新 exe 直接覆盖旧文件即可，设置（开机自启、崩溃自动重启、`dshtray.ini`）不受影响

## 快速开始

### 1. 安装依赖

| 依赖 | 说明 |
| --- | --- |
| Windows 10/11 | .NET Framework 4.8 系统自带 |
| Node.js | 运行 harness 所需 |
| DeepSeek Harness | 安装方法见 [DeepSeek-Harness 仓库](https://github.com/deepseek-ai/DeepSeek-Harness) |
| 浏览器（Chrome / Edge 等 Chromium 系） | 可选，用于浏览器 APP 模式窗口显示 |

### 2. 运行 dsh-tray

双击 `dsh-tray.exe` → 托盘出现鲸鱼图标 → harness 自动启动（无终端窗口）。**以后不用再手动敲 `dsh web` 了。**

### 3. 使用说明

右键托盘图标：

```
打开窗口
────────
启动        ← harness 停止时可用
重启        ← harness 运行时可用
停止        ← 只停 harness，托盘不退
────────
设置…
────────
退出        ← 仅退出托盘，harness 保持运行（停止用「停止」）
```

| 操作 | 行为 |
| --- | --- |
| 左键单击托盘图标 | 未运行：启动并打开窗口；运行中：打开窗口 |
| 右键托盘图标 | 仅弹出菜单 |

状态图标：运行中 = 蓝色鲸鱼；已停止 = 黑 / 白鲸（随系统深浅色切换）。

## 配置

`dshtray.ini` 是托盘的唯一配置文件，首次运行自动在 exe 同目录生成（带注释模板）。默认 `url=http://127.0.0.1:3080`，改端口直接改这里（端口由 url 推导）。

```ini
url = http://127.0.0.1:3080   # 默认值;端口由 url 推导,改端口直接改这里
lang =                        # 界面语言 zh/en,留空 = 跟随系统
autorestart = true            # 崩溃自动重启 true/false
autostart = false             # 开机自启 true/false(同时写入 Windows 启动项)
theme =                       # 亮/暗主题:light/dark,留空 = 跟随系统
node =                        # Node.js 路径,留空 = 自动检测
dshentry =                    # dsh 入口脚本路径,留空 = 自动检测
dshworkdir =                  # dsh 工作目录,留空 = 自动推断
chrome =                      # Chromium 系浏览器路径,留空 = 自动查找 Chrome/Edge
```

### dev checkout（源码方式运行 harness）适配

若你的 DSH 是 git checkout 而非 npm 全局安装（入口形如 `node --import tsx/esm apps/cli/src/bin.ts "web"`），托盘的标准自动检测找不到它。本仓库提供适配入口：

```ini
dshentry   = <本仓库路径>\entry\dsh\bin.js
dshworkdir = <本仓库路径>\entry
```

`entry\dsh\bin.js` 是包装器：路径含 `\dsh\` 满足托盘的进程身份检查，内部用 tsx 拉起真实 dev harness，并转发终止信号使托盘的停止/重启（taskkill /T 树杀）能到达真实进程。标准 npm 全局安装用户留空这两行即可，此文件可删除。

## 构建

零依赖，用 Windows 自带编译器：

```bat
build.bat
:: 等价于: "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" @dsh-tray.rsp
```

产物：`dsh-tray.exe`（单文件，.NET Framework 4.8，anycpu）。

## 测试

```bat
:: headless 自检（结果写入 exe 同目录的 *-result.txt / menu-test.txt）
dsh-tray.exe --smoke
dsh-tray.exe --menu-test
dsh-tray.exe --find-window
dsh-tray.exe --ui-preview

:: 集成测试（42 项：生命周期 / 崩溃自动重启 / 孤儿收养 / 并发竞态回归 / ini / 版本比较等）
tests\build-and-run.bat
```

## 日志

- `%LOCALAPPDATA%\dsh-tray\tray.log` —— 托盘自身操作记录（启动 / 停止 / 重启 / 提权 / 自动重启等），超 5MB 自动轮转为 `tray.log.old`
- `%LOCALAPPDATA%\dsh-tray\harness.log` —— harness 输出，独立于托盘生命周期（托盘退出后仍在写入）
- 设置窗口可一键打开日志文件夹

## 常见问题

**dsh-tray 会访问网络吗？** 启动时后台静默检查一次 GitHub 最新版本（仅此一次；可离线，失败静默，不弹窗）；其余时候不访问网络。

**退出托盘后 harness 还在运行？** 这是设计行为：「退出」只退出托盘，harness 保持运行；需要完全停止请用菜单里的「停止」。

**为什么有时会弹出 UAC 提权？** 当 harness 以管理员身份启动时，停止 / 重启 / 退出需要管理员权限才能结束它，此时会弹 UAC。若系统 UAC 设置为「从不通知」则静默完成。

## 许可证

[MIT License](LICENSE) —— 可自由使用、修改、商用、再分发，仅需保留版权声明。

## 致谢与同类项目

- 上游：[KAIbsb/dsh-tray](https://github.com/KAIbsb/dsh-tray)（v1.1.3，本仓库的代码基座）
- 同类：[qing3a/dsh-tray](https://github.com/qing3a/dsh-tray)（DSH 插件式轻量托盘，TypeScript 实现）——定位不同：本仓库是独立 exe 生命周期管家，qing3a 版是随 DSH 进程加载的托盘插件
