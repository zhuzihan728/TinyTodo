# 构建

Windows 10/11，.NET Framework 4.x。

- `build.cmd`：编译到 `bin/TinyTodo.exe`。
- `test.cmd`：业务测试。
- `ui-test.cmd`：界面测试；先退出 TinyTodo，测试使用临时数据。
- `powershell -ExecutionPolicy Bypass -File packaging/build-package.ps1 -Compiler "C:\Path\To\ISCC.exe"`：生成安装包与便携 ZIP。

安装包使用 [Inno Setup 6.7+](https://jrsoftware.org/isdl.php)。仓库不包含编译器；本地便携编译器可放在 `tools/InnoSetup`。

## 目录

- `src` / `tests`：源码与测试。
- `assets`：运行资源；`assets/source` 保留用户提供的原始素材。
- `packaging`：安装脚本和中文安装说明。
- `docs/screenshots`：演示数据截图。
- `dist`：本地生成的安装包和便携包，通过 GitHub Releases 分发。
- `artifacts` / `archive`：本地测试产物和历史归档，不提交。

安装器默认勾选开机自启动和桌面快捷方式，可取消。仅安装到当前用户目录，交互卸载默认勾选清理任务文件与备份，可取消保留数据。静默卸载默认保留数据；仅显式传入 `/PURGETASKDATA=1` 时清理。

字体许可证与来源见 `assets/Font-license.txt` 和 `assets/Font-source-notice.txt`。安装器中文翻译取自 [Inno Setup 翻译文件](https://github.com/jrsoftware/issrc/blob/main/Files/Languages/ChineseSimplified.isl)，保留文件内署名。

## 发布验证

2026-09-16：73 项业务测试、337 项 UI 检查通过（225% 缩放）；安装、取消卸载、默认勾选清理任务与静默卸载检查通过。`tools/TestInstaller.ps1` 在工作区临时目录验证安装包，不清理个人任务。

## 3.5.3 悬停修复

鼠标停留于原任务或 Markdown 预览卡片时保持预览；离开两者后短暂延迟关闭，原任务与卡片间切换不重建窗口。列表和树节点均使用实际命中范围；切换到其他应用仍关闭预览。升级请直接覆盖安装，安装器不写入任务数据目录。

验证：73 项业务测试、345 项 UI 检查通过；隔离执行 3.5.2 → 3.5.3 覆盖安装，默认自启动/桌面快捷方式与版本升级正常，已登记任务文件的 SHA256 前后一致，原安装登记未改变。可运行 `tools/TestUpgrade.ps1` 复现（需要旧版和新版发布目录及 Inno Setup）。
