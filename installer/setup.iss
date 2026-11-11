; NovaDown 0.3.0 安装包脚本
; 编译：ISCC.exe installer\setup.iss

#define MyAppName "NovaDown"
#define MyAppVersion "0.3.0"
#define MyAppExeName "NovaDown.App.exe"
#define MyAppGuid "{{7E1A4C2B-9D3F-4A58-8C6D-21B0E5F7A930}"

[Setup]
AppId={#MyAppGuid}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\NovaDown
DefaultGroupName=NovaDown
UninstallDisplayName=NovaDown 下载器
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=NovaDown-{#MyAppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
; 卸载时保留用户数据（%APPDATA%\NovaDown 设置与下载目录）
CloseApplications=yes

[Messages]
SelectDirDesc=NovaDown 安装位置
SelectDirLabel3=安装程序将把 NovaDown 安装到以下文件夹。点击"浏览"可更改（建议安装在非系统盘）。
FinishedLabelNoIcons=NovaDown 已安装到 [dir]。%n%n浏览器扩展位于安装目录的 extension 文件夹，按使用说明加载即可开启下载接管。
FinishedLabel=NovaDown 已安装到 [dir]。%n%n浏览器扩展位于安装目录的 extension 文件夹，按使用说明加载即可开启下载接管。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务："

[Files]
; 桌面端（自包含，无需 .NET 运行时）
Source: "..\publish\App\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; 命令行版（serve 无界面模式）
Source: "..\publish\Cli\*"; DestDir: "{app}\cli"; Flags: recursesubdirs ignoreversion
; 浏览器扩展（解压加载用）
Source: "..\extension\*"; DestDir: "{app}\extension"; Flags: recursesubdirs ignoreversion
; Agent 插件与文档
Source: "..\agents\mcp-server.mjs"; DestDir: "{app}\agents"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\使用说明.docx"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\NovaDown 下载器"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\NovaDown 使用说明"; Filename: "{app}\使用说明.docx"
Name: "{group}\NovaDown（无界面服务 cli）"; Filename: "{app}\cli\NovaDown.Cli.exe"; Parameters: "serve"
Name: "{autodesktop}\NovaDown 下载器"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 NovaDown"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载只删安装目录；用户设置（%APPDATA%\NovaDown）与下载目录不受影响
