# 教室屏幕操作锁

<img src="icon.png" alt="教室屏幕操作锁图标" width="128" />

ClassScreenGuard 是一个适用于 ClassIsland 2.x 的 Windows 屏幕操作锁插件。锁定后保留当前屏幕画面，不显示全屏遮挡层，同时隔离鼠标、触摸和普通键盘输入，适用于需要临时限制教室电脑操作的场景。

## 功能

- 当前画面和正在播放的内容保持可见。
- 透明输入隔离层覆盖所有显示器，拦截鼠标、触摸和长按操作。
- 锁定期间拦截普通按键及 Windows 键、Alt+F4、Alt+Tab 等组合键。
- 按 `Ctrl+Alt+L` 锁定；锁定后再次按下可打开独立密码框。
- 主显示器右下角提供常驻「解锁」按钮，可使用鼠标或触摸打开密码框。
- 密码框提供可点击的「解锁」按钮，也可以按 Enter 提交。
- 密码错误不限重试次数，没有错误次数锁定或冷却时间。
- 密码使用随机盐和 PBKDF2-SHA256 哈希保存，不保存明文。

## 运行环境

- ClassIsland 2.x（插件 API 2.0.0.0 或更高版本）
- Windows 10/11
- 构建项目需要 .NET 8 SDK

本插件使用 Windows 输入钩子和窗口 API，不支持 Linux 或 macOS。

## 安装

### 插件市场

上架审核通过后，在 ClassIsland 的插件市场中搜索「教室屏幕操作锁」并安装。

### 手动安装

1. 前往 [Releases](https://github.com/mjdds/ClassScreenGuard/releases) 下载最新版 `ClassScreenGuard.cipx`。
2. 在 ClassIsland 的插件设置中安装该 `.cipx` 文件。
3. 按 ClassIsland 提示重启或重新加载插件。

## 使用方法

1. 在 ClassIsland 设置中打开「教室屏幕操作锁」。
2. 设置并保存解锁密码，密码至少 4 个字符，建议使用 8 位以上密码。
3. 切换到需要保留的屏幕画面。
4. 点击「立即锁定操作」，或按 `Ctrl+Alt+L`。
5. 解锁时可再次按 `Ctrl+Alt+L`，或点击主屏幕右下角的「解锁」按钮。
6. 输入密码后按 Enter 或点击「解锁」。点击「取消」只关闭密码框，不会解除锁定。

## 安全说明

本插件是用户态操作限制工具，不是 Windows 安全边界：

- `Ctrl+Alt+Delete` 始终保留给 Windows。
- 无法阻止断电、强制重启、注销或管理员结束进程。
- 停止插件或退出 ClassIsland 会解除输入隔离。
- 不应将其用于保护高敏感数据或替代系统账户权限管理。

## 从源码构建

```powershell
dotnet build -c Release -p:CreateCipx=true
```

如果构建环境只有 Windows PowerShell，可额外指定：

```powershell
dotnet build -c Release -p:CreateCipx=true -p:PowershellBinaryName=powershell
```

插件包和 MD5 校验信息输出到 `cipx/` 目录。

## 开源许可

本项目采用 [MIT License](LICENSE) 开源。第三方依赖仍遵循其各自的许可证。
