# TinyTodo 3.5.2 · 暹罗

## 当前头像资源

悬浮入口使用用户提供的 `siamese-idle-v5-ezgif.com-gif-maker.gif`（资源为 `assets/floating-icon.gif`）：透明背景、无白底和圆角底板，48 帧按素材时序循环。所有帧共用可见区域边界，完整等比例显示；去掉所有帧底部共有的透明行，窗口高度随之收紧，让脚底可以贴住任务栏上沿；隐藏或全屏避让时暂停动画，退出时释放帧与计时器。EXE、任务栏、托盘和标题栏继续使用静态头像。

应用可执行文件、任务栏、托盘与各窗口标题栏统一使用用户提供的 `siamese-head-bow-pixel-icon.png`。两份 PNG 已裁掉原图四周的纯透明边距（96×96 → 86×55），保留全部可见像素；显示时去除透明留白，保留横向比例，主体左右贴边、上下居中，以最近邻缩放保留像素边缘。标题栏为 34 DIP，悬浮入口为 36 DIP；托盘按系统小图标尺寸加载同一份 ICO，与 EXE、任务栏和应用内采用相同布局：只移除完全透明的外边距，完整头像按比例放大至左右撑满，上下居中，不裁切、不拉伸。`assets/app.ico` 包含 16–256 像素的 15 档图像，小尺寸采用标准 32 位 DIB，大尺寸采用 PNG，并嵌入 EXE。下方旧版本的生成素材说明仅作历史记录。


## 列表完成／恢复反馈

Store.Change 成功后才启动 TaskTable 的 520ms 临时反馈：前 280ms 显示新勾选状态、淡粉高亮及成功文字，后 240ms 将即将离开当前筛选的行淡出。过滤后的数据与临时退出行分别处理，不把退出行写回数据。关系列表中仍应存在的行只高亮，不移除。

每个任务独立计时，16ms UI 定时器只在动画期间运行，使用 Stopwatch 计时。同一任务反馈期间阻止重复操作，切换筛选／预览任务时清理旧行；鼠标按下和释放还需匹配任务 ID，避免动画移除行时误点后面的任务。


## 输入与焦点隔离

移除 MainForm 的 SetPinned 轮询。250ms 定时器仅读取全屏状态以调整悬浮入口可见性，不再改变任务窗口或模态弹窗的 Z 序。启动路径默认开启 Floating，并正常显示 MainForm；取消 SetVisibleCore 的首次隐藏拦截。ShowWithoutActivation 与不自动置顶的行为保持。

FloatingIcon 保留 WS_EX_NOACTIVATE／ShowWithoutActivation，并明确响应 WM_MOUSEACTIVATE 为 MA_NOACTIVATE；仍保留点击与拖动。Ui.ExactModifiers 严格匹配应用快捷键的修饰键，用线程键状态检查 Win 键后直接让出处理。无全局热键、键鼠钩子、键盘布局写入或系统快捷键设置修改。

测试覆盖 Ctrl+Space、Ctrl+Shift+Space、Alt+Space、Alt+Shift+Space、修饰键本身的透传，以及另一个进程的输入窗口在后台轮询和悬浮入口恢复时保持前台与键盘布局不变。测试不模拟系统级游戏快捷键；未复现的输入法异常不作为已确定根因处理。


树形任务节点统一使用普通按钮的 `Theme.Border`（#E6D7CC）与 1 屏幕像素边框，画布缩放时补偿线宽；选中节点通过浅粉底色区分。

## 当前星标与托盘菜单行为

当前树节点星标中心为 `(Right - 14, Top + 14)`，沿用金色，采用 7.5 DIP 外半径、4 DIP 内半径及贝塞尔圆角，让尖角和凹角都更圆润饱满；标题不再为左侧星标缩进，右侧改留 28 DIP，避免长标题与星标重叠。

托盘不使用 NotifyIcon 的同步 ContextMenuStrip 弹出路径；右键 MouseUp 后通过 BeginInvoke 显示共享菜单并将前台交给菜单 HWND，不激活主窗口。菜单 Opening／Closed 维护交互状态，后台轮询期间不调整普通图标可见性；进入全屏时仍关闭菜单并执行避让。


## 当前窗口行为补充

`TaskGraph.ResetView` 统一归位按钮和 Home 的行为：先将 Zoom 设为 1，再调用现有居中算法。普通重绘与画布大小变化不重置用户缩放。

预览标题的重要标记直接复用列表的 Ui.DrawImportant 圆头叹号，按标题与列表的字号比例缩放绘制和 12 DIP 前缀宽度，按首行中线对齐；图标与标题均不设额外 Margin。操作按钮继续使用 7×13 DIP 小叹号。

悬浮单击释放鼠标捕获后再排队处理。展开主窗口时先 Show，再恢复 Normal，避免隐藏的最小化窗口在 Show 时恢复旧状态。按 OwnedForms 查找最深的可见模态弹窗并激活；已被遮挡的主窗口不会被误判为需要收起。

复用 250ms UI 定时器读取前台进程、窗口范围和 Windows 通知状态。独占 D3D、全屏／演示或覆盖所在屏幕的外部窗口触发避让，桌面窗口不触发几何判定。任务窗口及模态弹窗始终使用普通窗口层级，全屏时隐藏悬浮入口；恢复图标使用其现有 WS_EX_NOACTIVATE，不调用 Activate。无全局键鼠钩子。检测存在最多一个轮询周期的延迟，游戏实机兼容性待验证。

Windows API 依据：[通知状态](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state)、[前台窗口](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)。


## 当前设计 · 中性微暖，第七轮

标签到表格共享 Ui.ViewTabGap = 3 DIP；主页收紧工具栏底部空间，预览去掉标签原有 3 DIP 顶部边距并补到底部，保留表格位置。添加关系行使用真正的 SoftButton 子控件，保留原行尺寸并随行滚动；背景、描边、鼠标和键盘反馈与编辑／完成／设为！一致。AppWindow 及独立弹窗统一设置 Theme.AppIcon。

主体 #FAFAF9；表头 #E8E0D8，未完成卡片 #FFFFFF；普通任务无描边，选中保持 #926546 的 1.6 DIP 轮廓；完成任务 #F2EFEC。正文、状态与箭头 #6B3E31。新增为 24 DIP 圆形按钮，#DEA9B7 底、2.4 DIP 圆头棕色加号，与未选中标签可见区域中线对齐并留 12 DIP 左侧间距；完成按钮白底。待办／历史与列表／树形共用 SoftButton 索引模式。表头为连续色带，不分四块。

TaskCard 在主列表和关系列表中共用；树形节点使用相同圆角和底色，边框按普通按钮样式单独绘制。列表与树形完成命中范围直接对应绘制的小方框。树形工具为 Graph 子控件，按客户区右上角布局，不应用图形坐标变换；切换待办／历史重置列表模式。切换悬浮时保留已打开窗口的可见性和位置。

TextViewport 预留固定滚动槽宽度。RichEdit 通过 ContentsResized／EM_REQUESTRESIZE 获取完整文档高度，固定客户区高度作为 Page，用 EM_GETSCROLLPOS／EM_SETSCROLLPOS 同步像素位置；不再用末尾可见行倒推可视页大小。普通编辑 TextBox 则使用固定字号的行数模型。MessageFilter 仅将鼠标实际命中的文本视口滚轮交给该视口，销毁时注销。官方消息接口参考：[EM_GETSCROLLPOS](https://learn.microsoft.com/en-us/windows/win32/controls/em-getscrollpos)、[EM_SETSCROLLPOS](https://learn.microsoft.com/en-us/windows/win32/controls/em-setscrollpos)。

207 项 UI 检查覆盖底部重复滚动、拖动超出底部、文末可见、视口缩放、非焦点滚轮、精确勾选、浮层固定、模式可见性、初始关系选择和提示框。225% 缩放已检查实际窗口截图。核心 73 项通过。

## 历史设计记录

设计方向：极简暹罗主题，文渊圆体，干净轻微暖感的中性底。整套界面共享 5–8 DIP 的轻圆角、黑棕文字、纤细轮廓与微弱纸面高光。只在背景作浅色混合，引用使用灰米色，链接／前置关系使用青灰色。鼠标按下显示强调描边，松开后取消；选中按钮只保留浅粉底。

| 基色 | 用途 |
|---|---|
| #E2A5AD | 与黑棕混合形成偏深粉色强调 |
| #EDCDCE | 以 23% 混入 #F6F6F6，作为浅选中背景 |
| #F6F6F6 | 中性基底 |
| #B5C7C9 | 以 16% 混入基底形成前置浅底；深混合用于链接和箭头 |
| #C9C0B5 | 引用、边框、哑光纸面阴影 |
| #413530 | 正文与选中任务的黑棕描边 |

哑光材质：相同方向的微弱垂直明暗变化，不添加随机纹理或装饰图案。使用最终不透明混合色，避免不同 WinForms 绘制路径出现透明叠色差异。胶囊模式开关使用窗口／圆球图形，位于设置左侧。索引卡仅替换列表／树形导航形态，不改变应用信息架构。

应用和托盘使用 `assets/app.ico`，对应原始生成图为 `assets/app-icon.png`。悬浮图标使用 `assets/floating-icon.png`。两图均为内置 imagegen 生成的带透明通道资产，属于同一暹罗角色方向；其他小工具图标用原生绘图统一圆润线条，保证缩放时清晰。ICO 为标准格式和尺寸转换，没有用手工位图替代生成图。

字体采用用户最新提供的文渊圆体（WenYuan Rounded SC），仅附常规和粗体两个原始静态 TrueType 文件，不转换、不裁剪。通过进程私有加载使用，无需安装到系统。每个字重包含 35018 个字形，字符映射覆盖 33013 个字符；界面中文已核对覆盖。遇到未覆盖字符或不支持的样式时回退到 Microsoft YaHei UI。来源说明见 `assets/Font-source-notice.txt`，作者提供的 SIL OFL 1.1 许可证及贡献者声明见 `assets/Font-license.txt`，原文来源：https://github.com/takushun-wu/WenYuanFonts/blob/main/LICENSE.md。

重要标记采用原生矢量绘制的圆头、轻微倾斜红色叹号，正文约 13 DIP 高；菜单和按钮均按文字加图标的整体宽度定位，不左右分散对齐。

源描述文本仍直接存储；Markdown 只影响显示，原文可随时编辑。Markdown 不依赖浏览器或第三方渲染引擎。

## 3.3–3.5 历史图标生成提示词

模式：内置 imagegen。

### 应用图标

Use case: logo-brand. Final standalone application icon for a minimal Windows todo app. Subject: a very simple adorable Siamese cat HEAD ONLY, frontal symmetrical emblem, oversized triangular deep-brown ears with dusty-rose inner ears, warm-white round head, an unmistakable soft deep-coffee Siamese mask over the central face, two tiny calm blue eyes as the only cool-color accent, a tiny bean-pink nose. The outside head silhouette is outlined in deep dark brown, thick clean confident outline so it reads at 16 and 32 pixels. Minimal Japanese chibi/anime stationery charm, flat 2D vector-like fills, no realistic fur, no hair strands, no gradients, no fine hatching, no sparkles, no props, no text, no badge, no checkmark. Very simplified and iconic, grown-up cute. PALETTE limited to warm white #FAF8F3, deep brown #4D342D, milk coffee #B18B74, deep muted rose #B46179 and light muted rose #E9BAC7; blue only as a tiny eye detail. Keep body/head light and the brown mask defined, not a dark brown square. Transparent alpha background, not a checkerboard baked into the picture, no shadows outside the silhouette. One large centered HEAD, with ~6% transparent margin. Square image 1024x1024. Not a sprite sheet. This is a NEW icon replacing an earlier memo-note direction.

### 悬浮图标

Use case: logo-brand. Create a minimalist chibi anime SIAMESE CATGIRL HEAD icon for a 36x36 Windows floating todo button. This is the anthropomorphic companion to a simple Siamese cat app icon. HEAD ONLY, no body, no shoulders, no hands, no cursor arrow, no labels, no sheet. A tiny adult-character chibi face framed by a warm-white short bob with only 3 broad bangs, dark espresso brown triangular cat ears with dusty-rose interiors, two broad milk-coffee hair-tip patches like Siamese points. Gentle blue eyes with very simple single-dot highlights, a tiny smiling mouth, subtle dusty-rose cheek accents. One tiny deep rose hair clip, no ribbons or other accessories. Minimal big shapes and very clean dark brown outline, light flat fills, anime-inspired Japanese icon charm; not realistic, not intricate character art, no texture or fine strands, no 3D. PALETTE: warm white #FAF8F3, deep brown #4D342D, milk coffee #B18B74, deep muted rose #B46179, light muted rose #E9BAC7. Blue confined to tiny eyes. Keep face warm-neutral and light; no yellow cast, no large dark backdrop. A balanced simple silhouette readable at 36 pixels, centered large with 6% clear margin. Transparent alpha background (no checkerboard baked in), square 1024x1024. One finished icon.

## 实机验证范围

本环境可检查源码语法、字体结构、透明通道、ICO 和打包一致性，无法运行 Windows 桌面。真实字号、缩放、ClearType、托盘以及原生菜单绘制以 Windows 验收为准，见 VALIDATION.txt。

3.5 树形连线：保持上游在上、下游在下；直线中段用小半径转折连接，末端为圆线帽空心 V 箭头，首尾不触碰任务框。当前任务的金色星标上移，其他重要标记保持原样。

开源设计调研：AntdUI（https://github.com/antdui/AntdUI）的自绘界面方向；BorderlessForm（https://github.com/mganss/BorderlessForm）的边缘／四角控制思路。未复制其代码。


## 3.5.2 历史生成素材与提示词

模式：内置 imagegen；用户附件 06ff467e-5f1c-4066-b5c3-88df25927206.png 仅用作画风参考。猫头保存为 assets/app-icon.png 及多尺寸 assets/app.ico；猫娘保存为 assets/floating-icon.png。两图均保留生成的透明通道。以下为实际最终提示词。

### 猫娘头像

Use case: logo-brand. Asset for TinyTodo, a minimalist desktop todo app. Input image is STYLE REFERENCE ONLY: use the soft expressive chibi anime linework, broad cat ears and gentle face from the supplied collage. Do NOT copy its blue hair palette, poses, props or collage layout. Our theme is SIAMESE: warm-white #F6F6F6, dark espresso brown #413530, milk coffee #C9C0B5, muted rose #E2A5AD and pale dusty pink #EDCDCE. Blue-gray #B5C7C9 only for a tiny eye accent. Soft matte flat cel colors, clean brown outline, a little natural charm, no glossy 3D, no busy details. ONE large centered head, approximately 88% of image width, perfectly square canvas, real transparent alpha background (no baked-in checkerboard), no text or symbols outside the head, no shadows outside the silhouette, no labels, no grid. Subject: an original adult-character SIAMESE CATGIRL HEAD ONLY, no neck, body, shoulders or hands. Warm-white hair with milk-coffee side locks and short soft bangs, deep espresso triangular cat ears with dusty-pink inner ears. Gently smiling expression, large simple expressive anime eyes in muted blue-gray. Hair contours and eyelids should feel softly hand drawn like the reference, but simplified for a 36-pixel floating icon. Keep only a few broad hair masses, no thin strands, no ribbons, no headdress, no bow. A subtle Siamese pointed-color identity. Head and hair silhouette is close and compact rather than long narrow hair. Cute, quiet and approachable.

### 猫头

Use case: logo-brand. Asset for TinyTodo, a minimalist desktop todo app. Input image is STYLE REFERENCE ONLY: use the soft expressive chibi anime linework, broad cat ears and gentle face from the supplied collage. Do NOT copy its blue hair palette, poses, props or collage layout. Our theme is SIAMESE: warm-white #F6F6F6, dark espresso brown #413530, milk coffee #C9C0B5, muted rose #E2A5AD and pale dusty pink #EDCDCE. Blue-gray #B5C7C9 only for a tiny eye accent. Soft matte flat cel colors, clean brown outline, a little natural charm, no glossy 3D, no busy details. ONE large centered head, approximately 88% of image width, perfectly square canvas, real transparent alpha background (no baked-in checkerboard), no text or symbols outside the head, no shadows outside the silhouette, no labels, no grid. Subject: the animal companion to that catgirl: a SIAMESE CAT HEAD ONLY, no body. Wide soft warm-white cheeks, deep espresso brown triangular ears, milk-coffee and dark brown central Siamese facial mask, muted blue-gray eyes, tiny dusty-rose nose, gentle tiny smiling mouth. Chibi anime expressive charm corresponding to the reference, with two or three broad face shapes and clean simplified contours readable at 16–32 pixels. Not a human face, no hair, no accessories, no paws. Avoid stiff circular mascot geometry; keep the ears expressive and the cheeks gently curved.
