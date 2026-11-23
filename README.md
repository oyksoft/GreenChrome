# GreenChrome

绿色版 Chrome 管理工具 —— 把官方 Chrome 离线包，整理成可以放在任意目录的绿色版。

![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-blue)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8.1-purple)
![License](https://img.shields.io/badge/license-closed%20source-orange)

## 功能

- **自动检测**已绑定的绿色版 Chrome：版本号、架构（x64 / x86 / arm64）、Chrome++ 版本
- **版本对比**：拉取线上各通道（STABLE / BETA / DEV / CANARY）的最新版本，与本机对比
- **一键应用**：下载官方离线包 → 校验 SHA256 → 提取内嵌 7z → 解包 → 写入 `App\<版本>\`
- **从零制作**：不需要现成的绿色版，一键下载解包并配好 Chrome++，生成一套完整可用的绿色版
- **Chrome++ 集成**：自动检测与更新 [Chrome++ Next](https://github.com/Bush2021/chrome_plus)
- **绿色友好**：所有文件都在同一个目录里，不写注册表、不装服务、可整体移动
- **单文件**：所有依赖都打包进 `GreenChrome.exe`，解压即用

## 使用

1. 下载并解压 Release 里的 `GreenChrome-*.zip`
2. 运行 `GreenChrome.exe`
3. 首次使用会让你选：
   - **选择已有目录** —— 绑定一套现成的绿色版 Chrome
   - **从零制作一套** —— 选个空目录（对话框里可以点「新建文件夹」），程序会下载解包并配好 Chrome++
4. 在仪表盘点「检查更新」，需要时点「应用到 <版本>」

> 更新前如果这套绿色版的 Chrome 正在运行，程序会提示先关闭它（也可以让程序强制结束）。

## 目录结构

制作或更新后的绿色版长这样：

```
MyChrome\
├── App\
│   ├── <版本号>\        Chrome 程序文件
│   ├── chrome.exe       启动器
│   ├── version.dll      Chrome++（绿色化代理）
│   └── chrome++.ini     Chrome++ 配置
├── Data\                用户数据
└── Cache\
```

`App\chrome++.ini` 是你自己的配置，升级时**不会被覆盖**。

## 配置文件

`GreenChrome.exe` 同目录下的 `greenchrome.conf`（JSON），可以直接用记事本改：

| 字段 | 说明 |
| --- | --- |
| `chromeDir` | 绑定的绿色版 Chrome 目录 |
| `channel` | 通道：`stable` / `beta` / `dev` / `canary` |
| `ghMirrors` | GitHub 国内镜像列表（Chrome++ 下载用），先镜像后官方；写 `[]` 则只走官方 |
| `api` | 版本来源，固定 `https://coi.oyk.pub/api`，不需要改 |
| `proxy` | 预留，当前版本未使用 |
| `autoCheck` | 预留，当前版本未使用（启动不自动联网，只有点「检查更新」才请求） |

## 环境要求

- Windows 10 / 11
- .NET Framework 4.8.1（Win10 1903+ 与 Win11 系统自带，无需另外安装）

## 自检

```bat
GreenChrome.exe --selftest
```

会真实跑一遍打包进 exe 的依赖（JSON 解析、7z 解压、原生对话框），结果写到同目录 `selftest.log`。

## 致谢

- [Chrome++ Next](https://github.com/Bush2021/chrome_plus) —— 提供绿色化所需的 `version.dll`
- Chrome 离线包来自 Google 官方
