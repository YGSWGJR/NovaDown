// NovaDown 使用说明 — DOCX 生成脚本
// 架构：R1 封面（DS-1 深海）+ 目录节（罗马页码）+ 正文节（阿拉伯页码从 1 起）
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  Header, Footer, PageNumber, NumberFormat, AlignmentType, HeadingLevel,
  WidthType, BorderStyle, ShadingType, TableLayoutType, SectionType,
  PageBreak, TableOfContents, LevelFormat,
} = require("docx");
const fs = require("fs");
const path = require("path");

// ── 调色板：DS-1 Deep Sea ──
const P = {
  bg: "0B1C2C", accent: "529286",
  cover: { titleColor: "FFFFFF", subtitleColor: "B0B8C0", metaColor: "90989F", footerColor: "687078" },
  table: { headerBg: "529286", headerText: "FFFFFF", innerLine: "BECFCC", surface: "E8ECEB" },
  headingColor: "0B1C2C", body: "1F2A33", secondary: "5A6B78",
};

const NB = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };
const noBorders = { top: NB, bottom: NB, left: NB, right: NB };
const allNoBorders = { top: NB, bottom: NB, left: NB, right: NB, insideHorizontal: NB, insideVertical: NB };

// ── 封面标题布局函数（来自 design-system） ──
function splitTitleLines(title, charsPerLine) {
  if (title.length <= charsPerLine) return [title];
  const breakAfter = new Set([..."，。、；：！？", ..."的与和及之在于为", ..."-_—–·/", ..." \t"]);
  const lines = [];
  let remaining = title;
  while (remaining.length > charsPerLine) {
    let breakAt = -1;
    for (let i = charsPerLine; i >= Math.floor(charsPerLine * 0.6); i--) {
      if (i < remaining.length && breakAfter.has(remaining[i - 1])) { breakAt = i; break; }
    }
    if (breakAt === -1) {
      const limit = Math.min(remaining.length, Math.ceil(charsPerLine * 1.3));
      for (let i = charsPerLine + 1; i < limit; i++) {
        if (breakAfter.has(remaining[i - 1])) { breakAt = i; break; }
      }
    }
    if (breakAt === -1) {
      breakAt = charsPerLine;
      const prevChar = remaining[breakAt - 1], nextChar = remaining[breakAt];
      if (prevChar && nextChar && !breakAfter.has(prevChar) && !breakAfter.has(nextChar) &&
          /[\u4e00-\u9fff]/.test(prevChar) && /[\u4e00-\u9fff]/.test(nextChar)) breakAt -= 1;
    }
    lines.push(remaining.slice(0, breakAt).trim());
    remaining = remaining.slice(breakAt).trim();
  }
  if (remaining) lines.push(remaining);
  if (lines.length > 1 && lines[lines.length - 1].length <= 2) {
    const last = lines.pop();
    lines[lines.length - 1] += last;
  }
  return lines;
}

function calcTitleLayout(title, maxWidthTwips, preferredPt = 40, minPt = 24) {
  const charsPerLine = (pt) => Math.floor(maxWidthTwips / (pt * 20));
  let titlePt = preferredPt, lines;
  while (titlePt >= minPt) {
    const cpl = charsPerLine(titlePt);
    if (cpl < 2) { titlePt -= 2; continue; }
    lines = splitTitleLines(title, cpl);
    if (lines.length <= 3) break;
    titlePt -= 2;
  }
  if (!lines || lines.length > 3) {
    lines = splitTitleLines(title, charsPerLine(minPt));
    titlePt = minPt;
  }
  return { titlePt, titleLines: lines };
}

function calcCoverSpacing(params) {
  const {
    titleLineCount = 1, titlePt = 36, hasSubtitle = false, hasEnglishLabel = false,
    metaLineCount = 0, fixedHeight = 800, pageHeight = 16838, marginTop = 0, marginBottom = 0,
  } = params;
  const SAFETY = 1200;
  const usableHeight = pageHeight - marginTop - marginBottom - SAFETY;
  const titleHeight = titleLineCount * (titlePt * 23 + 200);
  const subtitleHeight = hasSubtitle ? (12 * 23 + 600) : 0;
  const englishLabelHeight = hasEnglishLabel ? (9 * 23 + 600) : 0;
  const metaHeight = metaLineCount * (10 * 23 + 100);
  const implicitParaHeight = 3 * 300;
  const contentHeight = titleHeight + subtitleHeight + englishLabelHeight + metaHeight + fixedHeight + implicitParaHeight;
  const safeRemaining = Math.max(usableHeight - contentHeight, 400);
  const FOOTER_MIN = 800;
  const rawTop = Math.floor(safeRemaining * 0.45);
  const rawBottom = Math.floor(safeRemaining * 0.45);
  const bottomSpacing = Math.max(rawBottom, FOOTER_MIN);
  const topSpacing = Math.max(rawTop - Math.max(0, FOOTER_MIN - rawBottom), 400);
  const midSpacing = Math.max(safeRemaining - topSpacing - bottomSpacing, 0);
  return { topSpacing, midSpacing, bottomSpacing };
}

// ── R1 封面（Pure Paragraph Left） ──
function buildCoverR1(config) {
  const C = config.palette;
  const padL = 1200, padR = 800;
  const availableWidth = 11906 - padL - padR - 300;
  const { titlePt, titleLines } = calcTitleLayout(config.title, availableWidth, 40, 24);
  const titleSize = titlePt * 2;
  const spacing = calcCoverSpacing({
    titleLineCount: titleLines.length, titlePt,
    hasSubtitle: !!config.subtitle, hasEnglishLabel: !!config.englishLabel,
    metaLineCount: (config.metaLines || []).length, fixedHeight: 400,
  });
  const accentLeft = { style: BorderStyle.SINGLE, size: 8, color: C.accent, space: 12 };
  const children = [];

  children.push(new Paragraph({ spacing: { before: spacing.topSpacing } }));

  if (config.englishLabel) {
    children.push(new Paragraph({
      indent: { left: padL, right: padR }, spacing: { after: 500 },
      border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: C.accent, space: 8 } },
      children: [new TextRun({ text: config.englishLabel.split("").join("  "),
        size: 18, color: C.accent, font: { ascii: "Calibri", eastAsia: "SimHei" }, characterSpacing: 40 })],
    }));
  }

  for (let i = 0; i < titleLines.length; i++) {
    children.push(new Paragraph({
      indent: { left: padL },
      spacing: { after: i < titleLines.length - 1 ? 100 : 300, line: Math.ceil(titlePt * 23), lineRule: "atLeast" },
      children: [new TextRun({ text: titleLines[i], size: titleSize, bold: true,
        color: C.titleColor, font: { eastAsia: "SimHei", ascii: "Arial" } })],
    }));
  }

  if (config.subtitle) {
    children.push(new Paragraph({
      indent: { left: padL }, spacing: { after: 800 },
      children: [new TextRun({ text: config.subtitle, size: 24, color: C.subtitleColor,
        font: { eastAsia: "Microsoft YaHei", ascii: "Arial" } })],
    }));
  }

  for (const line of (config.metaLines || [])) {
    children.push(new Paragraph({
      indent: { left: padL + 200 }, spacing: { after: 80 },
      border: { left: accentLeft },
      children: [new TextRun({ text: line, size: 24, color: C.metaColor,
        font: { eastAsia: "Microsoft YaHei", ascii: "Arial" } })],
    }));
  }

  children.push(new Paragraph({ spacing: { before: spacing.bottomSpacing } }));

  children.push(new Paragraph({
    indent: { left: padL, right: padR },
    border: { top: { style: BorderStyle.SINGLE, size: 2, color: C.accent, space: 8 } },
    spacing: { before: 200 },
    children: [
      new TextRun({ text: config.footerLeft || "", size: 16, color: C.footerColor, font: { ascii: "Arial", eastAsia: "Microsoft YaHei" } }),
      new TextRun({ text: "                                        " }),
      new TextRun({ text: config.footerRight || "", size: 16, color: C.footerColor, font: { ascii: "Arial", eastAsia: "Microsoft YaHei" } }),
    ],
  }));

  return [new Table({
    width: { size: 100, type: WidthType.PERCENTAGE },
    layout: TableLayoutType.FIXED,
    borders: allNoBorders,
    rows: [new TableRow({
      height: { value: 16838, rule: "exact" },
      children: [new TableCell({
        shading: { type: ShadingType.CLEAR, fill: C.bg }, borders: noBorders,
        children,
      })],
    })],
  })];
}

// ── 正文构件 ──
function h1(text) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_1,
    keepNext: true, // 标题与后段同页，避免孤悬页底
    spacing: { before: 360, after: 160, line: 312 },
    children: [new TextRun({ text, bold: true, size: 32, color: P.headingColor,
      font: { ascii: "Calibri", eastAsia: "SimHei" } })],
  });
}

function h2(text) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_2,
    keepNext: true, // 标题与后段同页，避免孤悬页底
    spacing: { before: 240, after: 120, line: 312 },
    children: [new TextRun({ text, bold: true, size: 28, color: P.headingColor,
      font: { ascii: "Calibri", eastAsia: "SimHei" } })],
  });
}

function body(text, opts = {}) {
  return new Paragraph({
    alignment: AlignmentType.JUSTIFIED,
    indent: { firstLine: 420 },
    spacing: { line: 312, after: 80 },
    children: [new TextRun({ text, size: 24, color: P.body,
      font: { ascii: "Calibri", eastAsia: "Microsoft YaHei" } })],
    ...opts,
  });
}

// 提示框：左侧强调条 + 浅色底
function note(label, text) {
  return new Paragraph({
    alignment: AlignmentType.JUSTIFIED,
    spacing: { line: 312, before: 120, after: 160 },
    indent: { left: 200 },
    border: { left: { style: BorderStyle.SINGLE, size: 12, color: P.accent, space: 10 } },
    shading: { type: ShadingType.CLEAR, fill: P.table.surface },
    children: [
      new TextRun({ text: label + "  ", bold: true, size: 21, color: P.headingColor,
        font: { ascii: "Calibri", eastAsia: "SimHei" } }),
      new TextRun({ text, size: 21, color: P.body,
        font: { ascii: "Calibri", eastAsia: "Microsoft YaHei" } }),
    ],
  });
}

// 代码/路径块：等宽 + 浅底
function codeBlock(text) {
  return new Paragraph({
    spacing: { line: 312, before: 60, after: 120 },
    indent: { left: 300 },
    shading: { type: ShadingType.CLEAR, fill: "F2F5F7" },
    border: { left: { style: BorderStyle.SINGLE, size: 8, color: "9FB8C6", space: 10 } },
    children: [new TextRun({ text, size: 19, color: "24384A",
      font: { ascii: "Consolas", eastAsia: "Microsoft YaHei" } })],
  });
}

function listItem(text, reference) {
  return new Paragraph({
    numbering: { reference, level: 0 },
    alignment: AlignmentType.JUSTIFIED,
    spacing: { line: 312, after: 60 },
    children: [new TextRun({ text, size: 24, color: P.body,
      font: { ascii: "Calibri", eastAsia: "Microsoft YaHei" } })],
  });
}

function tableTitle(text) {
  return new Paragraph({
    keepNext: true,
    spacing: { before: 160, after: 80, line: 312 },
    children: [new TextRun({ text, bold: true, size: 21, color: P.secondary,
      font: { ascii: "Calibri", eastAsia: "SimHei" } })],
  });
}

// 横线风格表格（DS-1 表格色板）
function makeTable(headers, rows, widths) {
  const cellMargins = { top: 70, bottom: 70, left: 120, right: 120 };
  const headerRow = new TableRow({
    tableHeader: true, cantSplit: true,
    children: headers.map((text, i) => new TableCell({
      children: [new Paragraph({
        spacing: { line: 312 },
        children: [new TextRun({ text, bold: true, size: 21, color: P.table.headerText,
          font: { ascii: "Calibri", eastAsia: "SimHei" } })],
      })],
      shading: { type: ShadingType.CLEAR, fill: P.table.headerBg },
      margins: cellMargins,
      width: { size: widths[i], type: WidthType.PERCENTAGE },
    })),
  });
  const dataRows = rows.map((cells, r) => new TableRow({
    cantSplit: true,
    children: cells.map((text, i) => new TableCell({
      children: [new Paragraph({
        spacing: { line: 312 },
        children: [new TextRun({ text, size: 21, color: P.body,
          font: { ascii: "Calibri", eastAsia: "Microsoft YaHei" } })],
      })],
      shading: { type: ShadingType.CLEAR, fill: r % 2 === 0 ? "FFFFFF" : P.table.surface },
      margins: cellMargins,
      width: { size: widths[i], type: WidthType.PERCENTAGE },
    })),
  }));
  return new Table({
    width: { size: 100, type: WidthType.PERCENTAGE },
    borders: {
      top: { style: BorderStyle.SINGLE, size: 4, color: P.accent },
      bottom: { style: BorderStyle.SINGLE, size: 4, color: P.accent },
      left: NB, right: NB,
      insideHorizontal: { style: BorderStyle.SINGLE, size: 1, color: "D0D0D0" },
      insideVertical: NB,
    },
    rows: [headerRow, ...dataRows],
  });
}

// FAQ：加粗问题行 + 答案段
function qa(question, answer) {
  return [
    new Paragraph({
      keepNext: true,
      spacing: { before: 160, after: 60, line: 312 },
      children: [new TextRun({ text: question, bold: true, size: 24, color: P.headingColor,
        font: { ascii: "Calibri", eastAsia: "SimHei" } })],
    }),
    body(answer),
  ];
}

// 页眉 / 页脚
function pageHeader() {
  return new Header({
    children: [new Paragraph({
      alignment: AlignmentType.CENTER,
      border: { bottom: { style: BorderStyle.SINGLE, size: 2, color: "C8D4DC", space: 4 } },
      children: [new TextRun({ text: "NovaDown 使用说明", size: 16, color: "8A98A4",
        font: { ascii: "Calibri", eastAsia: "Microsoft YaHei" } })],
    })],
  });
}

function pageFooter() {
  return new Footer({
    children: [new Paragraph({
      alignment: AlignmentType.CENTER,
      children: [new TextRun({ children: [PageNumber.CURRENT], size: 18, color: "8A98A4",
        font: { ascii: "Calibri" } })],
    })],
  });
}

// ══════════════════════ 正文内容 ══════════════════════
const bodyChildren = [];

// 一、认识 NovaDown
bodyChildren.push(h1("一、认识 NovaDown"));
bodyChildren.push(body("NovaDown 是一款运行在 Windows 上的多线程下载器，定位与 IDM（Internet Download Manager）类似：把大文件切成多个分段同时下载，充分利用带宽，并支持断点续传、限速与自动重试。软件由本机自研引擎驱动，不依赖第三方下载核心，免费使用、无广告、不上传任何数据。"));
bodyChildren.push(body("软件由三部分组成：桌面端（主程序，负责任务管理与下载引擎）、浏览器扩展（把浏览器里的下载自动转交过来）与 Agent 插件（让 AI 助手通过指令管理下载）。三者的连接枢纽是桌面端内置的本机控制接口，默认运行在 localhost:28123。"));
bodyChildren.push(tableTitle("表 1-1  核心功能一览"));
bodyChildren.push(makeTable(
  ["功能", "说明"],
  [
    ["多线程分段下载", "默认 8 个分段并发，小文件自动减少分段数，充分利用带宽"],
    ["断点续传", "每段进度独立保存，关机、断网、崩溃后重新下载同一链接自动续传"],
    ["出错自动重试", "网络抖动、服务器限流（429）、临时故障（5xx）自动退避重试，最多 5 次"],
    ["限速", "默认不限速；可设全局上限或单任务上限，设置实时生效"],
    ["浏览器接管", "浏览器中开始下载的文件按规则自动转交 NovaDown，失败自动回退"],
    ["Agent 插件", "支持 MCP 协议，AI 助手可直接添加、暂停、查询下载任务"],
  ],
  [26, 74]
));

// 二、快速上手
bodyChildren.push(h1("二、快速上手"));
bodyChildren.push(h2("2.1 启动软件"));
bodyChildren.push(body("桌面端主程序位于项目目录下的 NovaDown.App 中，双击即可运行，无需安装。启动后窗口底部的状态栏会显示浏览器接管服务的运行状态。常用文件位置如下："));
bodyChildren.push(tableTitle("表 2-1  文件位置速查"));
bodyChildren.push(makeTable(
  ["内容", "位置"],
  [
    ["桌面端主程序", "NovaDown\\NovaDown.App\\bin\\Debug\\net8.0-windows\\NovaDown.App.exe"],
    ["命令行版本", "NovaDown\\NovaDown.Cli\\bin\\Debug\\net8.0\\NovaDown.Cli.dll"],
    ["浏览器扩展", "NovaDown\\extension\\（在浏览器中加载此文件夹）"],
    ["Agent 插件", "NovaDown\\agents\\mcp-server.mjs"],
    ["设置文件", "%APPDATA%\\NovaDown\\settings.json"],
    ["默认下载目录", "用户目录\\Downloads\\NovaDown"],
  ],
  [30, 70]
));
bodyChildren.push(h2("2.2 添加第一个下载任务"));
bodyChildren.push(body("以一个普通的文件直链为例，完整流程只需三步：", { spacing: { line: 312, after: 60 } }));
bodyChildren.push(listItem("在网页或文档中复制文件的完整下载直链（以 http 或 https 开头）。", "list-quickstart"));
bodyChildren.push(listItem("把链接粘贴到窗口顶部的输入框，点击右侧的【添加任务】按钮。", "list-quickstart"));
bodyChildren.push(listItem("任务出现在列表中并自动开始，观察进度条与实时速度即可；完成后点击【打开所在目录】可定位文件。", "list-quickstart"));
bodyChildren.push(note("提示", "任务默认保存到【用户目录\\Downloads\\NovaDown】。想换位置，请在设置窗口中修改下载目录（见第六章），修改只对之后新建的任务生效。"));
bodyChildren.push(h2("2.3 界面导览"));
bodyChildren.push(body("主窗口自上而下分为四个区域，各元素作用如下："));
bodyChildren.push(tableTitle("表 2-2  界面元素说明"));
bodyChildren.push(makeTable(
  ["区域 / 元素", "作用"],
  [
    ["顶部输入框 + 添加任务", "粘贴下载直链，点击添加；输入为空时按钮为灰色"],
    ["右上方【⚙ 设置】", "打开设置窗口：下载目录、限速、接管规则、端口与令牌"],
    ["任务列表 · 进度列", "进度条叠加百分比文字，实时反映下载进度"],
    ["任务列表 · 速度列", "每秒刷新的瞬时速度；状态列会显示【重试中】等细节"],
    ["底部按钮组", "暂停 / 继续 / 打开所在目录，先在列表中选中一个任务再操作"],
    ["底部状态栏", "显示浏览器接管服务的地址与运行状态"],
  ],
  [34, 66]
));

// 三、日常使用
bodyChildren.push(h1("三、日常使用"));
bodyChildren.push(h2("3.1 暂停与继续"));
bodyChildren.push(body("在任务列表中选中正在下载的任务，点击底部【暂停】即可停止；【继续】会从已保存的进度接着下载，已下载的部分不会浪费。状态为【出错】的任务同样可以点【继续】重试。"));
bodyChildren.push(h2("3.2 断点续传"));
bodyChildren.push(body("每个任务的分段进度都实时记录在下载目录里，与文件同名、后缀为 .novadown.json 的进度文件中。无论是主动暂停、程序关闭、断电还是断网，之后重新下载同一个链接时会自动从上次的位置继续，无需任何手动操作。"));
bodyChildren.push(note("注意", "想让某个文件彻底重新下载，需要同时删除下载目录中的文件本体和同名 .novadown.json 进度文件。"));
bodyChildren.push(h2("3.3 限速"));
bodyChildren.push(body("默认不限速，跑满带宽。两种限速方式可选：一是全局限速，在设置窗口中选择档位（512 KB/s 到 10 MB/s），所有任务共享额度并即时生效，适合边下载边开视频会议的场景；二是单任务限速，命令行下载时附加 --limit 参数指定每秒字节数。想恢复全速，把全局限速拨回【不限速】即可。"));
bodyChildren.push(h2("3.4 出错自动重试"));
bodyChildren.push(body("下载过程中遇到网络波动、服务器临时故障（5xx）或站点限流（429），引擎会按 2、4、8、16、32 秒的间隔自动退避重试，最多 5 次；若服务器指明了等待时间（Retry-After），则按服务器要求等待。重试期间状态列显示【重试中】，已下载进度不受影响。重试耗尽后任务标记为【出错】，此时可点击【继续】手动再试。"));

// 四、浏览器下载接管
bodyChildren.push(h1("四、浏览器下载接管"));
bodyChildren.push(body("安装浏览器扩展后，Chrome 或 Edge 里开始的下载会按规则自动转交给 NovaDown 的多线程引擎，并在失败时自动回退为浏览器普通下载。整个过程只需配置一次。"));
bodyChildren.push(h2("4.1 一次性安装（约两分钟）"));
bodyChildren.push(listItem("打开浏览器的扩展管理页（地址栏输入 chrome://extensions，Edge 为 edge://extensions），打开【开发者模式】，点击【加载已解压的扩展程序】，选择 NovaDown 项目下的 extension 文件夹。", "list-ext"));
bodyChildren.push(listItem("启动 NovaDown 桌面端，点击【⚙ 设置】，复制窗口中的访问令牌。", "list-ext"));
bodyChildren.push(listItem("点击浏览器工具栏中的 NovaDown 扩展图标，在弹窗中粘贴令牌，点击【保存并连接】。显示【已连接 NovaDown】即配置完成。", "list-ext"));
bodyChildren.push(note("提示", "若桌面端修改了 API 端口，需要在扩展弹窗中把端口改成相同的值并重新保存。"));
bodyChildren.push(h2("4.2 在桌面端配置接管规则"));
bodyChildren.push(body("扩展的行为完全由桌面端设置窗口控制，扩展端不单独保存规则。四项规则的含义如下："));
bodyChildren.push(tableTitle("表 4-1  接管规则"));
bodyChildren.push(makeTable(
  ["规则", "含义", "默认值"],
  [
    ["启用浏览器接管", "桌面端总开关，关闭后浏览器一律使用普通下载", "开启"],
    ["最小接管大小", "小于该体积的文件不接管，仍由浏览器直接下载", "1 MB"],
    ["接管扩展名", "命中列表中扩展名的文件无论大小一律接管，如 .zip、.exe、.mp4", "常见安装包与媒体格式"],
    ["出错回退浏览器下载", "NovaDown 下载报错时，扩展自动改用浏览器普通下载同一文件", "开启"],
  ],
  [24, 52, 24]
));
bodyChildren.push(h2("4.3 日常使用与状态标识"));
bodyChildren.push(body("配置完成后无需任何手动操作：在网页中点击下载，若文件符合规则，浏览器中原有的下载会被立即取消并转交 NovaDown，任务出现在桌面端列表中。扩展图标会出现临时角标：箭头表示已成功转交，感叹号表示已回退为浏览器下载，数秒后自动消失。点击扩展图标可随时查看与 NovaDown 的连接状态，并可用开关临时停用接管。"));
bodyChildren.push(h2("4.4 失败自动回退"));
bodyChildren.push(body("以下三种情况扩展会自动转为浏览器普通下载，不会造成下载丢失：NovaDown 未运行或未配对令牌；转交请求失败（例如端口变更后未同步）；NovaDown 下载过程中报错且规则中开启了出错回退。回退的链接在五分钟内不会被再次接管，避免死循环。"));

// 五、Agent 与无界面模式
bodyChildren.push(h1("五、Agent 与无界面模式"));
bodyChildren.push(h2("5.1 无界面服务模式"));
bodyChildren.push(body("不需要图形界面时（例如放在服务器或后台常驻），可用命令行以服务模式运行。它提供与桌面端完全相同的控制接口，供扩展和 Agent 插件调用："));
bodyChildren.push(codeBlock("NovaDown.Cli.exe serve    （或：dotnet NovaDown.Cli.dll serve）"));
bodyChildren.push(body("启动后命令行会打印接口地址与访问令牌，按 Ctrl+C 退出。"));
bodyChildren.push(h2("5.2 让 AI 助手管理下载（MCP 插件）"));
bodyChildren.push(body("项目附带符合 MCP 标准的 Agent 插件，支持 MCP 的 AI 客户端（如 ZCode、Claude）接入后可直接用自然语言操作下载：添加任务、查询进度、暂停与继续。在客户端的 MCP 配置中加入以下条目即可："));
bodyChildren.push(codeBlock('"mcpServers": { "novadown": { "command": "node", "args": ["...\\\\agents\\\\mcp-server.mjs"], "env": { "NOVADOWN_PORT": "28123", "NOVADOWN_TOKEN": "在设置中复制的令牌" } } }'));
bodyChildren.push(body("可用工具共五个：添加任务（支持附 Referer 应对防盗链站点）、列出任务、查询单个任务、暂停、继续。"));

// 六、设置项参考
bodyChildren.push(h1("六、设置项参考"));
bodyChildren.push(body("设置窗口中各项目的默认值与作用如下，修改后点击【保存】立即生效并写入配置文件："));
bodyChildren.push(tableTitle("表 6-1  设置项一览"));
bodyChildren.push(makeTable(
  ["设置项", "默认值", "说明"],
  [
    ["下载目录", "Downloads\\NovaDown", "新任务的保存位置；点击【浏览】可更换磁盘与文件夹"],
    ["全局速度上限", "不限速", "所有任务共享的带宽上限，档位 512 KB/s 至 10 MB/s，实时生效"],
    ["单任务分段数", "8", "每个任务的最大并发分段数，范围 1 至 32"],
    ["新任务自动开始", "开启", "关闭后任务加入列表时保持暂停，需手动点【继续】"],
    ["API 端口", "28123", "本机控制接口端口；修改后扩展弹窗需同步修改"],
    ["访问令牌", "自动生成", "扩展与 Agent 插件的连接凭证，可点击复制，请勿外传"],
    ["启用浏览器接管", "开启", "浏览器接管的桌面端总开关"],
    ["最小接管大小", "1 MB", "小于该值的文件不接管"],
    ["接管扩展名", "常见格式", "逗号分隔的扩展名列表，命中的文件一律接管"],
    ["出错回退浏览器下载", "开启", "NovaDown 报错时扩展自动改用浏览器下载"],
  ],
  [26, 24, 50]
));

// 七、常见问题
bodyChildren.push(h1("七、常见问题"));
bodyChildren.push(...qa("添加任务时提示【服务器未返回文件大小】怎么办？",
  "该网站不提供文件大小信息，当前版本无法计算分段，属于暂不支持的情形。多数软件官网、网盘直链与代码仓库都正常；遇到此类链接时，建议换用官方直链下载。"));
bodyChildren.push(...qa("状态一直显示【重试中】，需要我做什么？",
  "不需要任何操作。这是引擎在应对网络波动或站点限流，会自动等待并重试。若五次重试后仍失败，状态会变为【出错】并显示原因，此时可点击【继续】手动再试，或稍后重试。"));
bodyChildren.push(...qa("浏览器里的下载没有被接管？",
  "依次检查：桌面端是否已启动；扩展弹窗是否显示【已连接】；文件是否小于设置的最小接管大小且扩展名不在接管列表中；桌面端总开关是否开启。新改过端口的话，扩展弹窗中的端口也要同步修改。"));
bodyChildren.push(...qa("提示【禁止下载本机（localhost）地址】或【私有/保留地址】？",
  "这是内置的安全防护：下载引擎只允许公网 http/https 链接，拒绝本机与内网地址，防止恶意链接探测内网。局域网设备（如 NAS）的下载暂不支持。"));
bodyChildren.push(...qa("如何让某个文件彻底重新下载？",
  "先在任务列表确认旧任务不在下载中，然后到下载目录删除该文件与同名 .novadown.json 进度文件，再重新添加链接即可。"));
bodyChildren.push(...qa("启动时提示 API 启动失败或端口被占用？",
  "说明 28123 端口被其他程序占用。在设置窗口把 API 端口改为其他值（1024 至 65535）并保存；若已安装扩展，请在扩展弹窗中把端口同步改为新值。"));
bodyChildren.push(...qa("下载速度比预期慢？",
  "先确认设置中未开启全局限速；再检查任务的实际分段数（小文件会自动减少分段）；部分网站本身限速时，多分段也无法突破站点上限，这属于服务器侧限制。"));

// 八、安全与隐私
bodyChildren.push(h1("八、安全与隐私"));
bodyChildren.push(body("NovaDown 在设计上做了以下安全约束：下载引擎仅接受 http 与 https 公网链接，主动拒绝本机、环回、内网与保留地址，并跟踪每一次重定向逐跳校验，防止恶意链接借跳转探测内网；控制接口只监听本机回环地址，外部设备无法访问；访问令牌等同于本机控制权，请勿透露给他人或粘贴到不可信的页面；所有下载记录、设置与进度文件均保存在本机，软件不含任何数据上报。"));

// 九、已知限制
bodyChildren.push(h1("九、已知限制"));
bodyChildren.push(body("以下能力在当前版本（v0.2）尚未支持或存在边界：不提供文件大小信息的链接无法下载；BT 种子与磁力链暂不支持；网页视频嗅探尚未实现；断点续传要求服务器支持 Range 分段，个别站点只支持整段下载时会自动退化为单线程且续传从头开始；浏览器接管要求桌面端（或 serve 模式）处于运行状态。以上均已列入后续版本计划。"));

// 十、文件位置速查
bodyChildren.push(h1("十、问题反馈与信息速查"));
bodyChildren.push(body("排查问题时，以下文件与位置最常被用到：进度异常先看下载目录中的 .novadown.json 进度文件；连接异常先看桌面端底部状态栏与设置窗口中的端口、令牌；版本信息可在本机控制接口的 health 接口查询。"));
bodyChildren.push(tableTitle("表 10-1  常用信息速查"));
bodyChildren.push(makeTable(
  ["项目", "值"],
  [
    ["软件版本", "v0.2（2026 年 11 月 11 日）"],
    ["控制接口", "http://localhost:28123/api（令牌鉴权）"],
    ["健康检查", "GET /api/health"],
    ["设置文件", "%APPDATA%\\NovaDown\\settings.json"],
    ["进度文件", "下载目录内与文件同名的 .novadown.json"],
    ["命令行帮助", "NovaDown.Cli.exe（不带参数运行显示用法）"],
  ],
  [30, 70]
));

// ══════════════════════ 文档组装 ══════════════════════
const pgSize = { width: 11906, height: 16838 };
const pgMargin = { top: 1440, bottom: 1440, left: 1701, right: 1417 };

const doc = new Document({
  styles: {
    default: {
      document: {
        run: { font: { ascii: "Calibri", eastAsia: "Microsoft YaHei" }, size: 24, color: P.body },
        paragraph: { spacing: { line: 312 } },
      },
      heading1: {
        run: { font: { ascii: "Calibri", eastAsia: "SimHei" }, size: 32, bold: true, color: P.headingColor },
        paragraph: { spacing: { before: 360, after: 160, line: 312 }, outlineLevel: 0 },
      },
      heading2: {
        run: { font: { ascii: "Calibri", eastAsia: "SimHei" }, size: 28, bold: true, color: P.headingColor },
        paragraph: { spacing: { before: 240, after: 120, line: 312 }, outlineLevel: 1 },
      },
    },
  },
  numbering: {
    config: [
      {
        reference: "list-quickstart",
        levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } }],
      },
      {
        reference: "list-ext",
        levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } }],
      },
    ],
  },
  sections: [
    // 第 1 节：封面（无页码、无页眉页脚）
    {
      properties: { page: { size: pgSize, margin: { top: 0, bottom: 0, left: 0, right: 0 } } },
      children: buildCoverR1({
        title: "NovaDown 使用说明",
        subtitle: "自研多线程下载器 · 版本 v0.2",
        englishLabel: "NOVADOWN USER GUIDE",
        metaLines: [
          "适用系统：Windows 10 / 11",
          "文档版本：v0.2（2026 年 11 月）",
          "适用范围：桌面端 · 浏览器扩展 · Agent 插件",
        ],
        footerLeft: "NovaDown",
        footerRight: "2026",
        palette: { bg: P.bg, accent: P.accent, ...P.cover },
      }),
    },
    // 第 2 节：目录（罗马页码）
    {
      properties: {
        type: SectionType.NEXT_PAGE,
        page: { size: pgSize, margin: pgMargin, pageNumbers: { start: 1, formatType: NumberFormat.UPPER_ROMAN } },
      },
      headers: { default: pageHeader() },
      footers: { default: pageFooter() },
      children: [
        new Paragraph({
          alignment: AlignmentType.CENTER,
          spacing: { before: 480, after: 360 },
          children: [new TextRun({ text: "目  录", bold: true, size: 32,
            font: { eastAsia: "SimHei", ascii: "Calibri" }, color: P.headingColor })],
        }),
        new TableOfContents("Table of Contents", { hyperlink: true, headingStyleRange: "1-2" }),
        new Paragraph({
          spacing: { before: 200 },
          children: [new TextRun({
            text: "注：本目录由域代码生成。如编辑文档后页码变化，请在目录上点击右键并选择\u201c更新域\u201d以刷新页码。",
            italics: true, size: 18, color: "888888",
            font: { ascii: "Calibri", eastAsia: "Microsoft YaHei" } })],
        }),
        // 说明：正文位于独立分节（NEXT_PAGE），分节符自动换页，此处不再加 PageBreak 以免产生空白页
      ],
    },
    // 第 3 节：正文（阿拉伯页码从 1 起）
    {
      properties: {
        type: SectionType.NEXT_PAGE,
        page: { size: pgSize, margin: pgMargin, pageNumbers: { start: 1, formatType: NumberFormat.DECIMAL } },
      },
      headers: { default: pageHeader() },
      footers: { default: pageFooter() },
      children: bodyChildren,
    },
  ],
});

const OUT = path.join(__dirname, "..", "使用说明.docx");
Packer.toBuffer(doc).then((buf) => {
  fs.writeFileSync(OUT, buf);
  console.log("生成完成: " + OUT);
});
