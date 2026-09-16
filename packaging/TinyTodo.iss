#ifndef PackageRoot
  #define PackageRoot AddBackslash(SourcePath) + "..\dist\TinyTodo-Windows-3.5.9"
#endif
#ifndef OutputRoot
  #define OutputRoot AddBackslash(SourcePath) + "..\dist"
#endif

[Setup]
AppId=TinyTodo
AppName=TinyTodo
AppVersion=3.5.9
AppPublisher=zhuzihan728
AppPublisherURL=https://github.com/zhuzihan728/TinyTodo
AppSupportURL=https://github.com/zhuzihan728/TinyTodo/issues
DefaultDirName={localappdata}\Programs\TinyTodo
DefaultGroupName=TinyTodo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
WizardStyle=modern
WizardSizePercent=110
UsePreviousTasks=no
DisableWelcomePage=no
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\bin\TinyTodo.exe
UninstallDisplayName=TinyTodo
AppMutex=Local\TinyTodo.2026.v1
CloseApplications=no
RestartApplications=no
OutputDir={#OutputRoot}
OutputBaseFilename=TinyTodo-3.5.9-Setup
Compression=lzma2
SolidCompression=yes
LZMAUseSeparateProcess=yes
VersionInfoVersion=3.5.9.0
VersionInfoDescription=TinyTodo 安装程序

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "startup"; Description: "开机自启动"; GroupDescription: "安装选项："
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "安装选项："

[Files]
Source: "{#PackageRoot}\bin\TinyTodo.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PackageRoot}\bin\version.txt"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PackageRoot}\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "INSTALL.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TinyTodo"; Filename: "{app}\bin\TinyTodo.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\TinyTodo"; Filename: "{app}\bin\TinyTodo.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\TinyTodo"; Filename: "{app}\bin\TinyTodo.exe"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\bin\TinyTodo.exe"; Description: "启动 TinyTodo"; Flags: nowait postinstall skipifsilent

[Code]
var
  ClearTaskData: Boolean;

function InitializeUninstall(): Boolean;
var
  Options: TSetupForm;
  Description: TNewStaticText;
  ClearBox: TNewCheckBox;
  ContinueButton, CancelButton: TNewButton;
begin
  ClearTaskData := False;
  if UninstallSilent then
  begin
    { Silent uninstall preserves task files unless explicitly requested. }
    ClearTaskData := ExpandConstant('{param:PURGETASKDATA|0}') = '1';
    Result := True;
    Exit;
  end;
  Options := CreateCustomForm(ScaleX(450), ScaleY(205), True, True);
  try
    Options.Caption := '卸载 TinyTodo';
    Options.Position := poScreenCenter;
    Description := TNewStaticText.Create(Options);
    Description.Parent := Options;
    Description.SetBounds(ScaleX(20), ScaleY(20), ScaleX(410), ScaleY(75));
    Description.AutoSize := False;
    Description.WordWrap := True;
    Description.Caption := '卸载会移除 TinyTodo、自启动和安装器创建的快捷方式。' + #13#10 +
      '勾选下方选项还会永久删除所有已登记数据目录中的任务、备份和配置；取消勾选可保留它们。';
    ClearBox := TNewCheckBox.Create(Options);
    ClearBox.Parent := Options;
    ClearBox.SetBounds(ScaleX(20), ScaleY(105), ScaleX(410), ScaleY(24));
    ClearBox.Caption := '同时清理任务文件和备份（不可恢复）';
    ClearBox.Checked := True;
    ContinueButton := TNewButton.Create(Options);
    ContinueButton.Parent := Options;
    ContinueButton.SetBounds(ScaleX(242), ScaleY(157), ScaleX(88), ScaleY(28));
    ContinueButton.Caption := '继续卸载';
    ContinueButton.ModalResult := mrOk;
    CancelButton := TNewButton.Create(Options);
    CancelButton.Parent := Options;
    CancelButton.SetBounds(ScaleX(340), ScaleY(157), ScaleX(88), ScaleY(28));
    CancelButton.Caption := '取消';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    CancelButton.Default := True;
    Result := Options.ShowModal() = mrOk;
    if Result then ClearTaskData := ClearBox.Checked;
  finally
    Options.Free();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
begin
  if (CurUninstallStep = usUninstall) and ClearTaskData then
  begin
    Log('TinyTodo: user selected task-data cleanup.');
    if not Exec(ExpandConstant('{app}\bin\TinyTodo.exe'), '--cleanup-data', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      MsgBox('无法启动任务清理。程序将继续卸载，任务文件仍需手动清理。', mbError, MB_OK)
    else if ExitCode <> 0 then
      MsgBox('任务清理未完成。程序将继续卸载；请按清理错误提示处理剩余数据。', mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    Log('TinyTodo selected tasks: ' + WizardSelectedTasks(False));
end;
