using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace OllamaModelInstaller
{
    internal static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            EnableHighDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }

        private static void EnableHighDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (Windows 10 1703+)
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try { SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }
    }

    internal sealed class ParamRow
    {
        public string Name;
        public string Label;
        public string DefaultValue;
        public string Description;
        public bool Enabled;
        public CheckBox Check;
        public Control Value;
    }

    [DataContract]
    internal sealed class OllamaModelList
    {
        [DataMember(Name = "models")]
        public List<OllamaModelInfo> models { get; set; }
    }

    [DataContract]
    internal sealed class OllamaModelInfo
    {
        [DataMember(Name = "name")]
        public string name { get; set; }
        [DataMember(Name = "model")]
        public string model { get; set; }
        [DataMember(Name = "modified_at")]
        public string modified_at { get; set; }
        [DataMember(Name = "size")]
        public long size { get; set; }
        [DataMember(Name = "digest")]
        public string digest { get; set; }
        [DataMember(Name = "details")]
        public OllamaModelDetails details { get; set; }
    }

    [DataContract]
    internal sealed class OllamaModelDetails
    {
        [DataMember(Name = "format")]
        public string format { get; set; }
        [DataMember(Name = "family")]
        public string family { get; set; }
        [DataMember(Name = "parameter_size")]
        public string parameter_size { get; set; }
        [DataMember(Name = "quantization_level")]
        public string quantization_level { get; set; }
    }

    internal sealed class MainForm : Form
    {
        private readonly Color Accent = Color.FromArgb(74, 91, 219);
        private readonly Color Bg = Color.FromArgb(245, 247, 251);
        private readonly Color Card = Color.White;
        private readonly Color Muted = Color.FromArgb(92, 101, 116);
        private TextBox ggufBox;
        private TextBox mmprojBox;
        private TextBox modelNameBox;
        private TextBox modelfileBox;
        private TextBox systemBox;
        private TextBox templateBox;
        private TextBox licenseBox;
        private TextBox requiresBox;
        private TextBox rawBox;
        private RichTextBox previewBox;
        private RichTextBox logBox;
        private Label ollamaStatus;
        private Label fileInfoLabel;
        private Label mmprojInfoLabel;
        private Button importButton;
        private Button cancelButton;
        private TabControl tabs;
        private DataGridView customGrid;
        private DataGridView installedModelsGrid;
        private Button deleteModelButton;
        private Label installedModelsStatus;
        private ComboBox presetCombo;
        private Label presetDescription;
        private readonly List<ParamRow> parameters = new List<ParamRow>();
        private Process runningProcess;
        private System.Windows.Forms.Timer serviceTimer;
        private string ollamaPath;
        private bool internalPreviewUpdate;
        private bool applyingPreset;

        public MainForm(string[] args)
        {
            Text = "Ollama 模型安装器";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { Icon = SystemIcons.Application; }
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 680);
            Size = new Size(1080, 800);
            BackColor = Bg;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            FormClosing += OnFormClosing;

            BuildUi();
            DetectOllama();
            serviceTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            serviceTimer.Tick += delegate { UpdateOllamaServiceStatus(); };
            serviceTimer.Start();

            if (args != null && args.Length > 0 && File.Exists(args[0]) &&
                String.Equals(Path.GetExtension(args[0]), ".gguf", StringComparison.OrdinalIgnoreCase))
            {
                SetGguf(args[0]);
            }
            else
            {
                AutoDetectNearbyModel();
            }
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(18) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill };
            var title = new Label { Text = "Ollama 模型安装器", Font = new Font(Font.FontFamily, 20F, FontStyle.Bold), AutoSize = true, Location = new Point(2, 2), ForeColor = Color.FromArgb(31, 38, 54) };
            var subtitle = new Label { Text = "放入模型文件夹即可自动识别，确认 Modelfile 后一键导入", AutoSize = true, Location = new Point(4, 43), ForeColor = Muted };
            ollamaStatus = new Label { AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleRight };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(ollamaStatus);
            header.Resize += delegate { ollamaStatus.Location = new Point(Math.Max(0, header.ClientSize.Width - ollamaStatus.Width - 6), 18); };
            root.Controls.Add(header, 0, 0);

            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 8) };
            tabs.TabPages.Add(BuildBasicTab());
            tabs.TabPages.Add(BuildParametersTab());
            tabs.TabPages.Add(BuildAdvancedTab());
            tabs.TabPages.Add(BuildPreviewTab());
            tabs.TabPages.Add(BuildModelManagerTab());
            tabs.SelectedIndexChanged += delegate { if (tabs.SelectedIndex == 4) RefreshInstalledModels(); };
            root.Controls.Add(tabs, 0, 1);

            var footer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
            var hint = new Label { Text = "流程：自动识别模型 → 选择参数预设 → 预览/编辑 → 一键导入", AutoSize = true, Location = new Point(2, 23), ForeColor = Muted };
            var previewButton = MakeButton("生成并预览", false, 128);
            previewButton.Click += delegate { UpdatePreview(); tabs.SelectedIndex = 3; };
            var saveButton = MakeButton("仅保存", false, 96);
            saveButton.Click += delegate { SaveModelfileInteractive(); };
            cancelButton = MakeButton("取消导入", false, 100);
            cancelButton.Enabled = false;
            cancelButton.Click += delegate { CancelImport(); };
            importButton = MakeButton("一键导入 Ollama", true, 150);
            importButton.Click += delegate { StartImport(); };
            footer.Controls.Add(hint);
            footer.Controls.Add(previewButton);
            footer.Controls.Add(saveButton);
            footer.Controls.Add(cancelButton);
            footer.Controls.Add(importButton);
            footer.Resize += delegate
            {
                int x = footer.ClientSize.Width;
                importButton.Location = new Point(x - importButton.Width, 10);
                cancelButton.Location = new Point(importButton.Left - cancelButton.Width - 8, 10);
                saveButton.Location = new Point(cancelButton.Left - saveButton.Width - 8, 10);
                previewButton.Location = new Point(saveButton.Left - previewButton.Width - 8, 10);
            };
            root.Controls.Add(footer, 0, 2);
        }

        private TabPage BuildBasicTab()
        {
            var tab = NewTab("1  自动识别");
            var panel = NewFormPanel();
            panel.RowCount = 11;
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            AddSectionLabel(panel, "GGUF 模型文件（默认自动扫描 EXE 所在目录）", 0);
            var fileLine = NewLinePanel();
            ggufBox = NewTextBox();
            ggufBox.Dock = DockStyle.Fill;
            ggufBox.ReadOnly = true;
            var browse = MakeButton("选择文件…", false, 112);
            browse.Dock = DockStyle.Right;
            browse.Click += delegate { BrowseGguf(); };
            fileLine.Controls.Add(ggufBox);
            fileLine.Controls.Add(browse);
            panel.Controls.Add(fileLine, 0, 1);
            fileInfoLabel = new Label { Text = "支持 .gguf 文件", Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(3, 0, 0, 0) };
            panel.Controls.Add(fileInfoLabel, 0, 2);

            AddSectionLabel(panel, "视觉投影 mmproj（可选，支持多模态模型）", 3);
            var projectorLine = NewLinePanel();
            mmprojBox = NewTextBox();
            mmprojBox.Dock = DockStyle.Fill;
            mmprojBox.ReadOnly = true;
            var clearProjector = MakeButton("清除", false, 70);
            clearProjector.Dock = DockStyle.Right;
            clearProjector.Click += delegate { SetMmproj(""); };
            var browseProjector = MakeButton("选择 mmproj…", false, 126);
            browseProjector.Dock = DockStyle.Right;
            browseProjector.Click += delegate { BrowseMmproj(); };
            projectorLine.Controls.Add(mmprojBox);
            projectorLine.Controls.Add(clearProjector);
            projectorLine.Controls.Add(browseProjector);
            panel.Controls.Add(projectorLine, 0, 4);
            mmprojInfoLabel = new Label { Text = "同目录存在 mmproj 时会自动识别；没有视觉投影文件可留空。", Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(3, 0, 0, 0) };
            panel.Controls.Add(mmprojInfoLabel, 0, 5);

            AddSectionLabel(panel, "要导入到 Ollama 的模型名称（可修改）", 6);
            modelNameBox = NewTextBox();
            modelNameBox.Dock = DockStyle.Top;
            modelNameBox.TextChanged += AnyChanged;
            panel.Controls.Add(modelNameBox, 0, 7);

            AddSectionLabel(panel, "Modelfile 保存位置", 8);
            var mfLine = NewLinePanel();
            modelfileBox = NewTextBox();
            modelfileBox.Dock = DockStyle.Fill;
            modelfileBox.TextChanged += AnyChanged;
            var mfBrowse = MakeButton("选择位置…", false, 112);
            mfBrowse.Dock = DockStyle.Right;
            mfBrowse.Click += delegate { BrowseModelfile(); };
            mfLine.Controls.Add(modelfileBox);
            mfLine.Controls.Add(mfBrowse);
            panel.Controls.Add(mfLine, 0, 9);

            var note = new Label
            {
                Text = "mmproj 会作为第二个 FROM 文件写入，不会误用 ADAPTER。模型与 mmproj 必须彼此匹配，且 Ollama 需支持该视觉架构。",
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(800, 0),
                ForeColor = Muted,
                Padding = new Padding(3, 14, 0, 0)
            };
            panel.Controls.Add(note, 0, 10);
            tab.Controls.Add(panel);
            return tab;
        }

        private TabPage BuildParametersTab()
        {
            var tab = NewTab("2  参数预设");
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1 };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 43));

            var presetGroup = new GroupBox { Text = "参数预设", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 7) };
            presetCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Location = new Point(12, 28), Font = new Font(Font.FontFamily, 10F) };
            presetCombo.Items.AddRange(new object[] { "基础（推荐）", "中级", "高级", "自定义" });
            presetDescription = new Label { AutoSize = true, Location = new Point(220, 31), ForeColor = Muted, MaximumSize = new Size(650, 0) };
            presetGroup.Controls.Add(presetCombo);
            presetGroup.Controls.Add(presetDescription);
            outer.Controls.Add(presetGroup, 0, 0);

            var commonGroup = new GroupBox { Text = "常用运行参数（勾选后写入）", Dock = DockStyle.Fill, Padding = new Padding(12) };
            var common = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            common.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            common.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            AddParam(common, "num_ctx", "上下文长度", "4K", "可选 4K～256K，写入时自动转换为数字", true);
            AddParam(common, "temperature", "温度", "0.8", "越高越有创造性，越低越稳定", true);
            AddParam(common, "top_k", "Top K", "40", "限制候选 token 数量", false);
            AddParam(common, "top_p", "Top P", "0.9", "按累计概率筛选候选 token", false);
            AddParam(common, "min_p", "Min P", "0.0", "相对最高概率的最低候选阈值", false);
            AddParam(common, "repeat_penalty", "重复惩罚", "1.1", "提高可减少重复内容", false);
            AddParam(common, "repeat_last_n", "重复检查范围", "64", "向前检查多少 token", false);
            AddParam(common, "num_predict", "最大输出长度", "-1", "最大生成 token 数，-1 表示不限制", false);
            AddParam(common, "seed", "随机种子", "0", "固定后可提高相同输入的可复现性", false);
            AddParam(common, "stop", "停止词", "", "遇到此文本时停止；多个停止词请用自定义参数", false);
            commonGroup.Controls.Add(common);
            outer.Controls.Add(commonGroup, 0, 1);

            var customGroup = new GroupBox { Text = "自定义参数（说明将写成注释）", Dock = DockStyle.Fill, Padding = new Padding(10) };
            var customLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            customLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            customLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            customGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                EnableHeadersVisualStyles = false
            };
            customGrid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            customGrid.DefaultCellStyle.Padding = new Padding(4, 5, 4, 5);
            customGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            customGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            customGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 6, 4, 6);
            customGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 242, 248);
            customGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(44, 51, 67);
            customGrid.RowTemplate.Height = 36;
            customGrid.RowTemplate.MinimumHeight = 34;
            customGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "启用", FillWeight = 16, TrueValue = true, FalseValue = false });
            customGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "参数名", FillWeight = 27 });
            customGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "值", FillWeight = 25 });
            customGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "说明 / 注释", FillWeight = 50 });
            var deleteColumn = new DataGridViewButtonColumn
            {
                HeaderText = "操作",
                Text = "删除",
                UseColumnTextForButtonValue = true,
                FillWeight = 18,
                FlatStyle = FlatStyle.Flat
            };
            deleteColumn.DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 240);
            deleteColumn.DefaultCellStyle.ForeColor = Color.FromArgb(190, 58, 48);
            deleteColumn.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 226, 222);
            deleteColumn.DefaultCellStyle.SelectionForeColor = Color.FromArgb(168, 42, 34);
            customGrid.Columns.Add(deleteColumn);
            customGrid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 4) return;
                DataGridViewRow row = customGrid.Rows[e.RowIndex];
                if (!row.IsNewRow) customGrid.Rows.RemoveAt(e.RowIndex);
            };
            customGrid.CellValueChanged += delegate { MarkCustomPreset(); UpdatePreview(); };
            customGrid.RowsAdded += delegate { if (customGrid.Rows.Count > 1) MarkCustomPreset(); UpdatePreview(); };
            customGrid.RowsRemoved += delegate { MarkCustomPreset(); UpdatePreview(); };
            customGrid.CurrentCellDirtyStateChanged += delegate { if (customGrid.IsCurrentCellDirty) customGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            customLayout.Controls.Add(customGrid, 0, 0);
            var customHint = new Label { Text = "示例：stop  |  <|eot_id|>  |  对话结束标记。相同参数（如 stop）可添加多行。", Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(2, 8, 0, 0) };
            customLayout.Controls.Add(customHint, 0, 1);
            customGroup.Controls.Add(customLayout);
            outer.Controls.Add(customGroup, 0, 2);
            presetCombo.SelectedIndexChanged += delegate { ApplySelectedPreset(); };
            presetCombo.SelectedIndex = 0;
            tab.Controls.Add(outer);
            return tab;
        }

        private TabPage BuildAdvancedTab()
        {
            var tab = NewTab("3  高级内容");
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 245, Padding = new Padding(18) };
            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            systemBox = MakeMultilineGroup(top, "SYSTEM 系统提示词", 0, 0, "定义模型默认角色和行为");
            templateBox = MakeMultilineGroup(top, "TEMPLATE 提示模板", 1, 0, "可选；通常应沿用 GGUF 内置模板");
            licenseBox = MakeMultilineGroup(top, "LICENSE 许可证说明", 0, 1, "可选；支持多行文本");
            var reqGroup = new GroupBox { Text = "REQUIRES 最低 Ollama 版本", Dock = DockStyle.Fill, Padding = new Padding(10) };
            requiresBox = NewTextBox();
            requiresBox.Dock = DockStyle.Top;
            requiresBox.TextChanged += AnyChanged;
            var reqHint = new Label { Text = "可选，例如 0.14.0", Dock = DockStyle.Top, ForeColor = Muted, Padding = new Padding(1, 8, 0, 0) };
            reqGroup.Controls.Add(reqHint);
            reqGroup.Controls.Add(requiresBox);
            top.Controls.Add(reqGroup, 1, 1);
            split.Panel1.Controls.Add(top);

            var rawGroup = new GroupBox { Text = "附加 Modelfile 指令", Dock = DockStyle.Fill, Padding = new Padding(10) };
            rawBox = new TextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 10F), WordWrap = false };
            rawBox.TextChanged += AnyChanged;
            rawGroup.Controls.Add(rawBox);
            split.Panel2.Controls.Add(rawGroup);
            tab.Controls.Add(split);
            return tab;
        }

        private TabPage BuildPreviewTab()
        {
            var tab = NewTab("4  预览与导入");
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 330, Padding = new Padding(18) };
            var previewGroup = new GroupBox { Text = "最终 Modelfile（确认前可直接编辑；导入将使用这里的内容）", Dock = DockStyle.Fill, Padding = new Padding(10) };
            previewBox = NewCodeBox();
            previewBox.TextChanged += delegate { if (!internalPreviewUpdate) { /* manual edits are preserved until another setting changes */ } };
            previewGroup.Controls.Add(previewBox);
            split.Panel1.Controls.Add(previewGroup);
            var logGroup = new GroupBox { Text = "Ollama 执行日志", Dock = DockStyle.Fill, Padding = new Padding(10) };
            logBox = NewCodeBox();
            logBox.ReadOnly = true;
            logBox.BackColor = Color.FromArgb(24, 27, 35);
            logBox.ForeColor = Color.FromArgb(218, 223, 232);
            logGroup.Controls.Add(logBox);
            split.Panel2.Controls.Add(logGroup);
            tab.Controls.Add(split);
            return tab;
        }

        private TabPage BuildModelManagerTab()
        {
            var tab = NewTab("5  模型管理");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 3, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            var header = new Panel { Dock = DockStyle.Fill };
            var title = new Label { Text = "已安装的 Ollama 模型", AutoSize = true, Location = new Point(2, 2), Font = new Font(Font.FontFamily, 14F, FontStyle.Bold), ForeColor = Color.FromArgb(31, 38, 54) };
            installedModelsStatus = new Label { Text = "打开此页面后自动读取模型列表", AutoSize = true, Location = new Point(3, 38), ForeColor = Muted };
            var refreshButton = MakeButton("刷新列表", false, 104);
            refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refreshButton.Click += delegate { RefreshInstalledModels(); };
            header.Controls.Add(title);
            header.Controls.Add(installedModelsStatus);
            header.Controls.Add(refreshButton);
            header.Resize += delegate { refreshButton.Location = new Point(header.ClientSize.Width - refreshButton.Width, 9); };
            layout.Controls.Add(header, 0, 0);

            installedModelsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                EnableHeadersVisualStyles = false
            };
            installedModelsGrid.DefaultCellStyle.Padding = new Padding(5, 7, 5, 7);
            installedModelsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            installedModelsGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 7, 5, 7);
            installedModelsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 242, 248);
            installedModelsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(44, 51, 67);
            installedModelsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            installedModelsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "模型名称", FillWeight = 42 });
            installedModelsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "参数规模", FillWeight = 16 });
            installedModelsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "量化", FillWeight = 18 });
            installedModelsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "架构", FillWeight = 18 });
            installedModelsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "磁盘大小", FillWeight = 18 });
            installedModelsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "修改时间", FillWeight = 26 });
            installedModelsGrid.SelectionChanged += delegate { deleteModelButton.Enabled = installedModelsGrid.SelectedRows.Count == 1; };
            layout.Controls.Add(installedModelsGrid, 0, 1);

            var footer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
            var warning = new Label { Text = "删除操作不可撤销；共享文件仍被其他模型使用时不会重复删除。", AutoSize = true, Location = new Point(2, 20), ForeColor = Muted };
            deleteModelButton = MakeButton("删除选中模型", false, 142);
            deleteModelButton.Enabled = false;
            deleteModelButton.ForeColor = Color.FromArgb(190, 58, 48);
            deleteModelButton.Click += delegate { DeleteSelectedModel(); };
            footer.Controls.Add(warning);
            footer.Controls.Add(deleteModelButton);
            footer.Resize += delegate { deleteModelButton.Location = new Point(footer.ClientSize.Width - deleteModelButton.Width, 9); };
            layout.Controls.Add(footer, 0, 2);

            tab.Controls.Add(layout);
            return tab;
        }

        private void AddParam(TableLayoutPanel panel, string name, string label, string defaultValue, string description, bool enabled)
        {
            var row = new ParamRow { Name = name, Label = label, DefaultValue = defaultValue, Description = description, Enabled = enabled };
            var item = new Panel { Height = 68, Dock = DockStyle.Top, Margin = new Padding(4) };
            row.Check = new CheckBox { Text = label + "  (" + name + ")", Checked = enabled, AutoSize = true, Location = new Point(4, 4), Font = new Font(Font, FontStyle.Bold) };
            if (String.Equals(name, "num_ctx", StringComparison.OrdinalIgnoreCase))
            {
                var contextCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Microsoft YaHei UI", 10F) };
                contextCombo.Items.AddRange(new object[] { "4K", "8K", "16K", "32K", "64K", "128K", "256K" });
                contextCombo.SelectedItem = defaultValue;
                row.Value = contextCombo;
            }
            else
            {
                row.Value = NewTextBox();
                row.Value.Text = defaultValue;
            }
            row.Value.Location = new Point(4, 29);
            row.Value.Width = 135;
            row.Value.Enabled = enabled;
            var desc = new Label { Text = description, AutoSize = true, ForeColor = Muted, Location = new Point(148, 33), MaximumSize = new Size(260, 0) };
            row.Check.CheckedChanged += delegate { row.Value.Enabled = row.Check.Checked; MarkCustomPreset(); UpdatePreview(); };
            row.Value.TextChanged += delegate { MarkCustomPreset(); UpdatePreview(); };
            item.Controls.Add(row.Check);
            item.Controls.Add(row.Value);
            item.Controls.Add(desc);
            int index = parameters.Count;
            panel.RowCount = (index / 2) + 1;
            if (index % 2 == 0) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            panel.Controls.Add(item, index % 2, index / 2);
            parameters.Add(row);
        }

        private void ApplySelectedPreset()
        {
            if (presetCombo == null || presetCombo.SelectedIndex < 0 || presetCombo.SelectedIndex == 3) return;
            applyingPreset = true;
            try
            {
                string[] basic = { "num_ctx", "temperature" };
                string[] middle = { "num_ctx", "temperature", "top_k", "top_p", "repeat_penalty" };
                string[] advanced = { "num_ctx", "temperature", "top_k", "top_p", "min_p", "repeat_penalty", "repeat_last_n", "num_predict", "seed" };
                string[] enabledNames = presetCombo.SelectedIndex == 0 ? basic : (presetCombo.SelectedIndex == 1 ? middle : advanced);
                var enabledSet = new HashSet<string>(enabledNames, StringComparer.OrdinalIgnoreCase);
                foreach (ParamRow p in parameters)
                {
                    p.Check.Checked = enabledSet.Contains(p.Name);
                    p.Value.Text = p.DefaultValue;
                }

                ParamRow context = FindParameter("num_ctx");
                ParamRow temperature = FindParameter("temperature");
                if (presetCombo.SelectedIndex == 0)
                {
                    context.Value.Text = "4K";
                    temperature.Value.Text = "0.8";
                    presetDescription.Text = "少量稳妥参数，适合首次导入和大多数模型。";
                }
                else if (presetCombo.SelectedIndex == 1)
                {
                    context.Value.Text = "8K";
                    temperature.Value.Text = "0.7";
                    presetDescription.Text = "加入采样与重复控制，适合日常对话和内容创作。";
                }
                else
                {
                    context.Value.Text = "16K";
                    temperature.Value.Text = "0.7";
                    FindParameter("min_p").Value.Text = "0.05";
                    presetDescription.Text = "开放更多生成控制；请按模型能力和电脑内存调整上下文长度。";
                }
            }
            finally
            {
                applyingPreset = false;
            }
            UpdatePreview();
        }

        private ParamRow FindParameter(string name)
        {
            foreach (ParamRow p in parameters) if (String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            throw new InvalidOperationException("未找到参数：" + name);
        }

        private void MarkCustomPreset()
        {
            if (applyingPreset || presetCombo == null || presetCombo.SelectedIndex < 0 || presetCombo.SelectedIndex == 3) return;
            applyingPreset = true;
            presetCombo.SelectedIndex = 3;
            presetDescription.Text = "已修改预设，可继续自由勾选、编辑或添加参数。";
            applyingPreset = false;
        }

        private TextBox MakeMultilineGroup(TableLayoutPanel owner, string title, int col, int row, string hint)
        {
            var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(5) };
            var box = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            box.TextChanged += AnyChanged;
            var tip = new ToolTip();
            tip.SetToolTip(box, hint);
            group.Controls.Add(box);
            owner.Controls.Add(group, col, row);
            return box;
        }

        private void AnyChanged(object sender, EventArgs e) { UpdatePreview(); }

        private void UpdatePreview()
        {
            if (previewBox == null) return;
            string text = BuildModelfile();
            int start = previewBox.SelectionStart;
            internalPreviewUpdate = true;
            previewBox.Text = text;
            previewBox.SelectionStart = Math.Min(start, previewBox.TextLength);
            internalPreviewUpdate = false;
        }

        private string BuildModelfile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 由 Ollama 模型安装器生成");
            sb.AppendLine("# 生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            string gguf = ggufBox == null ? "" : ggufBox.Text.Trim();
            sb.AppendLine("FROM " + QuotePath(gguf.Length == 0 ? "<请选择 GGUF 文件>" : Path.GetFullPath(gguf)));
            string mmproj = mmprojBox == null ? "" : mmprojBox.Text.Trim();
            if (mmproj.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# 多模态视觉投影文件（mmproj）");
                sb.AppendLine("FROM " + QuotePath(Path.GetFullPath(mmproj)));
            }

            foreach (ParamRow p in parameters)
            {
                if (p.Check != null && p.Check.Checked && !String.IsNullOrWhiteSpace(p.Value.Text))
                {
                    sb.AppendLine();
                    sb.AppendLine("# " + p.Label + "：" + p.Description);
                    string parameterValue = p.Value.Text.Trim();
                    if (String.Equals(p.Name, "num_ctx", StringComparison.OrdinalIgnoreCase))
                        parameterValue = NormalizeContextLength(parameterValue);
                    sb.AppendLine("PARAMETER " + p.Name + " " + QuoteParameterValue(p.Name, parameterValue));
                }
            }

            if (customGrid != null)
            {
                foreach (DataGridViewRow row in customGrid.Rows)
                {
                    if (row.IsNewRow) continue;
                    bool enabled = row.Cells[0].Value != null && Convert.ToBoolean(row.Cells[0].Value);
                    string name = CellText(row, 1);
                    string value = CellText(row, 2);
                    string description = CellText(row, 3);
                    if (!enabled || name.Length == 0 || value.Length == 0) continue;
                    sb.AppendLine();
                    if (description.Length > 0) sb.AppendLine("# " + description.Replace("\r", " ").Replace("\n", " "));
                    sb.AppendLine("PARAMETER " + name + " " + QuoteParameterValue(name, value));
                }
            }

            AppendBlock(sb, "SYSTEM", systemBox == null ? "" : systemBox.Text);
            AppendBlock(sb, "TEMPLATE", templateBox == null ? "" : templateBox.Text);
            AppendBlock(sb, "LICENSE", licenseBox == null ? "" : licenseBox.Text);
            if (requiresBox != null && !String.IsNullOrWhiteSpace(requiresBox.Text))
            {
                sb.AppendLine();
                sb.AppendLine("REQUIRES " + requiresBox.Text.Trim());
            }
            if (rawBox != null && !String.IsNullOrWhiteSpace(rawBox.Text))
            {
                sb.AppendLine();
                sb.AppendLine("# 附加指令");
                sb.AppendLine(rawBox.Text.Trim());
            }
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        private static void AppendBlock(StringBuilder sb, string instruction, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            sb.AppendLine();
            string normalized = value.Replace("\r\n", "\n").Replace("\r", "\n");
            if (normalized.IndexOf('\n') >= 0)
            {
                sb.AppendLine(instruction + " \"\"\"");
                sb.AppendLine(normalized);
                sb.AppendLine("\"\"\"");
            }
            else
            {
                sb.AppendLine(instruction + " " + normalized);
            }
        }

        private static string QuotePath(string value)
        {
            if (value.IndexOfAny(new[] { ' ', '\t', '#' }) >= 0) return "\"" + value.Replace("\"", "\\\"") + "\"";
            return value;
        }

        private static string QuoteParameterValue(string name, string value)
        {
            if (String.Equals(name, "stop", StringComparison.OrdinalIgnoreCase) || value.IndexOfAny(new[] { ' ', '\t', '#' }) >= 0)
                return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            return value;
        }

        private static string NormalizeContextLength(string value)
        {
            string text = value.Trim().ToUpperInvariant().Replace(" ", "");
            long multiplier = 1;
            if (text.EndsWith("K", StringComparison.Ordinal))
            {
                multiplier = 1024;
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("M", StringComparison.Ordinal))
            {
                multiplier = 1024 * 1024;
                text = text.Substring(0, text.Length - 1);
            }
            long number;
            if (!Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number <= 0)
                return value;
            return (number * multiplier).ToString(CultureInfo.InvariantCulture);
        }

        private static string CellText(DataGridViewRow row, int index)
        {
            object value = row.Cells[index].Value;
            return value == null ? "" : Convert.ToString(value).Trim();
        }

        private void AutoDetectNearbyModel()
        {
            string folder = Application.StartupPath;
            string[] allFiles;
            try { allFiles = Directory.GetFiles(folder, "*.gguf", SearchOption.TopDirectoryOnly); }
            catch (Exception ex)
            {
                AppendLog("无法扫描程序所在文件夹：" + ex.Message, Color.LightCoral);
                UpdatePreview();
                return;
            }

            var candidates = new List<string>();
            foreach (string file in allFiles)
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                if (name.Contains("mmproj") || name.Contains("projector")) continue;
                candidates.Add(file);
            }

            if (candidates.Count == 0)
            {
                fileInfoLabel.Text = "未在 EXE 所在文件夹找到 GGUF；请把 EXE 放入模型文件夹，或手动选择。";
                AppendLog("未自动发现 GGUF 文件。程序目录：" + folder, Color.Khaki);
                UpdatePreview();
                return;
            }

            candidates.Sort(StringComparer.OrdinalIgnoreCase);
            string selected = candidates[0];
            foreach (string file in candidates)
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                if (name.Contains("-00001-of-") || name.Contains(".00001-of-")) { selected = file; break; }
                if (new FileInfo(file).Length > new FileInfo(selected).Length) selected = file;
            }
            SetGguf(selected);
            AppendLog("已从程序所在文件夹自动识别：" + Path.GetFileName(selected), Color.LightGreen);
            if (candidates.Count > 1)
                AppendLog("共发现 " + candidates.Count + " 个主模型 GGUF，已自动选择首分片或体积最大的文件；可在基础设置中更换。", Color.Khaki);
        }

        private void BrowseGguf()
        {
            using (var dialog = new OpenFileDialog { Filter = "GGUF 模型 (*.gguf)|*.gguf|所有文件 (*.*)|*.*", Title = "选择 GGUF 模型文件" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) SetGguf(dialog.FileName);
            }
        }

        private void BrowseMmproj()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "mmproj 视觉投影 (*.gguf)|*.gguf|所有文件 (*.*)|*.*",
                Title = "选择与主模型匹配的 mmproj GGUF 文件"
            })
            {
                if (!String.IsNullOrWhiteSpace(ggufBox.Text))
                {
                    try { dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(ggufBox.Text)); } catch { }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK) SetMmproj(dialog.FileName);
            }
        }

        private void AutoDetectMmproj(string modelPath)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
            string[] files;
            try { files = Directory.GetFiles(folder, "*.gguf", SearchOption.TopDirectoryOnly); }
            catch { SetMmproj(""); return; }

            string selected = "";
            foreach (string file in files)
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                if (!name.Contains("mmproj") && !name.Contains("projector")) continue;
                if (selected.Length == 0 || new FileInfo(file).Length > new FileInfo(selected).Length) selected = file;
            }
            SetMmproj(selected);
            if (selected.Length > 0)
                AppendLog("已自动识别视觉投影：" + Path.GetFileName(selected), Color.LightGreen);
        }

        private void SetMmproj(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                mmprojBox.Text = "";
                mmprojInfoLabel.Text = "未使用视觉投影文件；模型将按普通 GGUF 导入。";
            }
            else
            {
                path = Path.GetFullPath(path);
                mmprojBox.Text = path;
                var info = new FileInfo(path);
                mmprojInfoLabel.Text = "已启用多模态导入    文件大小：" + FormatBytes(info.Length) + "    文件：" + info.Name;
            }
            UpdatePreview();
        }

        private void SetGguf(string path)
        {
            path = Path.GetFullPath(path);
            ggufBox.Text = path;
            var info = new FileInfo(path);
            fileInfoLabel.Text = "文件大小：" + FormatBytes(info.Length) + "    修改时间：" + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            name = Regex.Replace(name, "-00001-of-[0-9]+$", "");
            name = Regex.Replace(name, "[^a-z0-9._-]+", "-").Trim('-', '.');
            modelNameBox.Text = String.IsNullOrEmpty(name) ? "local-model" : name;
            modelfileBox.Text = Path.Combine(Path.GetDirectoryName(path), "Modelfile");
            AutoDetectMmproj(path);
            UpdatePreview();
        }

        private void BrowseModelfile()
        {
            using (var dialog = new SaveFileDialog { Filter = "Modelfile|Modelfile|所有文件 (*.*)|*.*", FileName = "Modelfile", Title = "保存 Modelfile" })
            {
                if (!String.IsNullOrWhiteSpace(modelfileBox.Text))
                {
                    try { dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(modelfileBox.Text)); } catch { }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK) modelfileBox.Text = dialog.FileName;
            }
        }

        private void SaveModelfileInteractive()
        {
            string error;
            if (!ValidateInputs(out error, false)) { ShowError(error); return; }
            if (String.IsNullOrWhiteSpace(modelfileBox.Text)) BrowseModelfile();
            if (String.IsNullOrWhiteSpace(modelfileBox.Text)) return;
            try
            {
                WriteModelfile(modelfileBox.Text);
                AppendLog("已保存：" + Path.GetFullPath(modelfileBox.Text), Color.LightGreen);
                MessageBox.Show(this, "Modelfile 已保存。", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ShowError("保存失败：" + ex.Message); }
        }

        private bool ValidateInputs(out string error, bool importing)
        {
            error = "";
            string path = ggufBox.Text.Trim();
            if (path.Length == 0) { error = "请先选择 GGUF 模型文件。"; return false; }
            if (!File.Exists(path)) { error = "GGUF 文件不存在，请重新选择。"; return false; }
            if (!String.Equals(Path.GetExtension(path), ".gguf", StringComparison.OrdinalIgnoreCase)) { error = "请选择扩展名为 .gguf 的模型文件。"; return false; }
            string mmproj = mmprojBox.Text.Trim();
            if (mmproj.Length > 0)
            {
                if (!File.Exists(mmproj)) { error = "mmproj 视觉投影文件不存在，请重新选择或清除。"; return false; }
                if (!String.Equals(Path.GetExtension(mmproj), ".gguf", StringComparison.OrdinalIgnoreCase)) { error = "mmproj 必须是 GGUF 文件。"; return false; }
                if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(mmproj), StringComparison.OrdinalIgnoreCase)) { error = "主模型和 mmproj 不能是同一个文件。"; return false; }
            }
            if (importing)
            {
                string model = modelNameBox.Text.Trim();
                if (model.Length == 0) { error = "请输入 Ollama 模型名称。"; return false; }
                if (!Regex.IsMatch(model, "^[A-Za-z0-9][A-Za-z0-9._/-]*(?::[A-Za-z0-9][A-Za-z0-9._-]*)?$"))
                { error = "模型名称格式不正确。可使用字母、数字、点、下划线、短横线、斜杠和一个标签冒号。"; return false; }
                if (String.IsNullOrEmpty(ollamaPath) || !File.Exists(ollamaPath)) { error = "未找到 Ollama。请先安装或启动 Ollama，再重新打开本程序。"; return false; }
            }
            foreach (ParamRow p in parameters)
            {
                if (p.Check.Checked && String.IsNullOrWhiteSpace(p.Value.Text)) { error = p.Label + " 已启用，但没有填写值。"; return false; }
            }
            return true;
        }

        private void StartImport()
        {
            string error;
            if (!ValidateInputs(out error, true)) { ShowError(error); return; }
            if (!EnsureOllamaServiceRunning()) return;
            try
            {
                string mf = modelfileBox.Text.Trim();
                if (mf.Length == 0)
                {
                    mf = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ggufBox.Text)), "Modelfile");
                    modelfileBox.Text = mf;
                }
                WriteModelfile(mf);
                logBox.Clear();
                AppendLog("已生成 Modelfile：" + Path.GetFullPath(mf), Color.LightSkyBlue);
                AppendLog("开始导入模型 " + modelNameBox.Text.Trim() + " …", Color.White);

                var psi = new ProcessStartInfo
                {
                    FileName = ollamaPath,
                    Arguments = "create " + QuoteArg(modelNameBox.Text.Trim()) + " -f " + QuoteArg(Path.GetFullPath(mf)),
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(mf)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                runningProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                runningProcess.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) AppendLog(e.Data, Color.Gainsboro); };
                runningProcess.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) AppendLog(e.Data, Color.Khaki); };
                runningProcess.Exited += OnImportExited;
                SetBusy(true);
                tabs.SelectedIndex = 3;
                runningProcess.Start();
                runningProcess.BeginOutputReadLine();
                runningProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                SetBusy(false);
                runningProcess = null;
                ShowError("无法启动导入：" + ex.Message);
            }
        }

        private void OnImportExited(object sender, EventArgs e)
        {
            Process p = sender as Process;
            int code = -1;
            try { p.WaitForExit(); code = p.ExitCode; } catch { }
            BeginInvoke(new Action(delegate
            {
                SetBusy(false);
                runningProcess = null;
                if (code == 0)
                {
                    AppendLog("导入完成！现在可以运行：ollama run " + modelNameBox.Text.Trim(), Color.LightGreen);
                    MessageBox.Show(this, "模型已成功导入 Ollama。\n\n模型名称：" + modelNameBox.Text.Trim(), "导入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AppendLog("导入失败，退出代码：" + code + "。请查看上方日志。", Color.LightCoral);
                    MessageBox.Show(this, "导入失败，请查看“预览与日志”中的 Ollama 输出。", "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                try { p.Dispose(); } catch { }
            }));
        }

        private void CancelImport()
        {
            Process p = runningProcess;
            if (p == null) return;
            if (MessageBox.Show(this, "确定要取消当前导入吗？", "取消导入", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { if (!p.HasExited) p.Kill(); AppendLog("已请求取消导入。", Color.LightCoral); } catch (Exception ex) { ShowError("取消失败：" + ex.Message); }
        }

        private void SetBusy(bool busy)
        {
            importButton.Enabled = !busy && !String.IsNullOrEmpty(ollamaPath);
            cancelButton.Enabled = busy;
            UseWaitCursor = busy;
        }

        private void WriteModelfile(string path)
        {
            path = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string content = previewBox.Text;
            if (String.IsNullOrWhiteSpace(content)) content = BuildModelfile();
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private void DetectOllama()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe")
            };
            foreach (string candidate in candidates) if (File.Exists(candidate)) { ollamaPath = candidate; break; }
            if (String.IsNullOrEmpty(ollamaPath))
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in path.Split(Path.PathSeparator))
                {
                    try { string candidate = Path.Combine(dir.Trim(), "ollama.exe"); if (File.Exists(candidate)) { ollamaPath = candidate; break; } } catch { }
                }
            }
            if (!String.IsNullOrEmpty(ollamaPath))
            {
                importButton.Enabled = true;
                UpdateOllamaServiceStatus();
            }
            else
            {
                ollamaStatus.Text = "●  未安装 Ollama";
                ollamaStatus.ForeColor = Color.FromArgb(202, 78, 66);
                importButton.Enabled = false;
                RepositionOllamaStatus();
            }
        }

        private bool TryGetOllamaServerVersion(out string version)
        {
            version = "";
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:11434/api/version");
                request.Method = "GET";
                request.Timeout = 700;
                request.ReadWriteTimeout = 700;
                request.KeepAlive = false;
                request.Proxy = null;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    if (response.StatusCode != HttpStatusCode.OK) return false;
                    string json = reader.ReadToEnd();
                    Match match = Regex.Match(json, "\\\"version\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                    if (match.Success) version = match.Groups[1].Value;
                    return true;
                }
            }
            catch { return false; }
        }

        private void RefreshInstalledModels()
        {
            if (installedModelsGrid == null || installedModelsGrid.IsDisposed) return;
            installedModelsGrid.Rows.Clear();
            deleteModelButton.Enabled = false;

            string version;
            if (!TryGetOllamaServerVersion(out version))
            {
                installedModelsStatus.Text = "Ollama 服务未运行，启动服务后点击“刷新列表”。";
                installedModelsStatus.ForeColor = Color.FromArgb(214, 132, 32);
                return;
            }

            UseWaitCursor = true;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:11434/api/tags");
                request.Method = "GET";
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.KeepAlive = false;
                request.Proxy = null;
                OllamaModelList result;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                    result = (OllamaModelList)new DataContractJsonSerializer(typeof(OllamaModelList)).ReadObject(stream);

                long totalSize = 0;
                if (result != null && result.models != null)
                {
                    foreach (OllamaModelInfo model in result.models)
                    {
                        OllamaModelDetails details = model.details ?? new OllamaModelDetails();
                        string modified = model.modified_at ?? "";
                        DateTimeOffset date;
                        if (DateTimeOffset.TryParse(modified, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                            modified = date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                        int index = installedModelsGrid.Rows.Add(
                            String.IsNullOrWhiteSpace(model.name) ? model.model : model.name,
                            details.parameter_size ?? "—",
                            details.quantization_level ?? "—",
                            details.family ?? "—",
                            FormatBytes(model.size),
                            modified);
                        installedModelsGrid.Rows[index].Tag = model;
                        totalSize += model.size;
                    }
                }
                installedModelsGrid.ClearSelection();
                installedModelsStatus.Text = "共 " + installedModelsGrid.Rows.Count + " 个模型，占用约 " + FormatBytes(totalSize) + "    ·    Ollama v" + version;
                installedModelsStatus.ForeColor = Color.FromArgb(27, 142, 84);
            }
            catch (Exception ex)
            {
                installedModelsStatus.Text = "读取模型列表失败：" + GetWebErrorMessage(ex);
                installedModelsStatus.ForeColor = Color.FromArgb(202, 78, 66);
            }
            finally { UseWaitCursor = false; }
        }

        private void DeleteSelectedModel()
        {
            if (installedModelsGrid.SelectedRows.Count != 1) return;
            DataGridViewRow selectedRow = installedModelsGrid.SelectedRows[0];
            OllamaModelInfo model = selectedRow.Tag as OllamaModelInfo;
            if (model == null) return;
            string modelName = String.IsNullOrWhiteSpace(model.name) ? model.model : model.name;

            var answer = MessageBox.Show(this,
                "确定要永久删除这个 Ollama 模型吗？\n\n模型：" + modelName + "\n磁盘大小：" + FormatBytes(model.size) + "\n\n此操作不可撤销。",
                "确认删除模型", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            if (!EnsureOllamaServiceRunning()) return;

            UseWaitCursor = true;
            deleteModelButton.Enabled = false;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:11434/api/delete");
                request.Method = "DELETE";
                request.ContentType = "application/json";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                request.KeepAlive = false;
                request.Proxy = null;
                string json = "{\"model\":\"" + JsonEscape(modelName) + "\"}";
                byte[] body = Encoding.UTF8.GetBytes(json);
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Ollama 返回状态 " + (int)response.StatusCode);
                }
                AppendLog("已删除 Ollama 模型：" + modelName, Color.LightGreen);
                MessageBox.Show(this, "模型已删除：\n" + modelName, "删除成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshInstalledModels();
            }
            catch (Exception ex)
            {
                string message = GetWebErrorMessage(ex);
                AppendLog("删除模型失败：" + message, Color.LightCoral);
                MessageBox.Show(this, "删除失败：" + message, "删除模型", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { UseWaitCursor = false; }
        }

        private static string GetWebErrorMessage(Exception exception)
        {
            WebException web = exception as WebException;
            if (web != null && web.Response != null)
            {
                try
                {
                    using (var reader = new StreamReader(web.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        string body = reader.ReadToEnd();
                        if (!String.IsNullOrWhiteSpace(body)) return body;
                    }
                }
                catch { }
            }
            return exception.Message;
        }

        private static string JsonEscape(string value)
        {
            if (value == null) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private void UpdateOllamaServiceStatus()
        {
            if (ollamaStatus == null || ollamaStatus.IsDisposed) return;
            if (String.IsNullOrEmpty(ollamaPath))
            {
                ollamaStatus.Text = "●  未安装 Ollama";
                ollamaStatus.ForeColor = Color.FromArgb(202, 78, 66);
                if (runningProcess == null) importButton.Enabled = false;
                RepositionOllamaStatus();
                return;
            }

            string version;
            if (TryGetOllamaServerVersion(out version))
            {
                ollamaStatus.Text = "●  Ollama 正在运行" + (version.Length > 0 ? "  v" + version : "");
                ollamaStatus.ForeColor = Color.FromArgb(27, 142, 84);
            }
            else
            {
                ollamaStatus.Text = "●  Ollama 未运行（导入时可自动启动）";
                ollamaStatus.ForeColor = Color.FromArgb(214, 132, 32);
            }
            if (runningProcess == null) importButton.Enabled = true;
            RepositionOllamaStatus();
        }

        private void RepositionOllamaStatus()
        {
            if (ollamaStatus.Parent != null)
                ollamaStatus.Location = new Point(Math.Max(0, ollamaStatus.Parent.ClientSize.Width - ollamaStatus.Width - 6), 18);
        }

        private bool EnsureOllamaServiceRunning()
        {
            string version;
            if (TryGetOllamaServerVersion(out version)) return true;

            var answer = MessageBox.Show(this,
                "检测到 Ollama 后台服务没有运行。\n\n是否由本程序自动启动 Ollama，然后继续导入？",
                "Ollama 未运行", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                AppendLog("导入已暂停：Ollama 服务未运行。", Color.Khaki);
                return false;
            }

            AppendLog("正在启动 Ollama 服务…", Color.LightSkyBlue);
            UseWaitCursor = true;
            if (serviceTimer != null) serviceTimer.Stop();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ollamaPath,
                    Arguments = "serve",
                    WorkingDirectory = Path.GetDirectoryName(ollamaPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process service = Process.Start(psi);
                if (service != null) service.Dispose();

                DateTime deadline = DateTime.Now.AddSeconds(12);
                while (DateTime.Now < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(250);
                    if (TryGetOllamaServerVersion(out version))
                    {
                        AppendLog("Ollama 服务已启动" + (version.Length > 0 ? "，版本 " + version : "") + "。", Color.LightGreen);
                        UpdateOllamaServiceStatus();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("启动 Ollama 失败：" + ex.Message, Color.LightCoral);
            }
            finally
            {
                UseWaitCursor = false;
                if (serviceTimer != null) serviceTimer.Start();
            }

            UpdateOllamaServiceStatus();
            MessageBox.Show(this, "未能在 12 秒内启动 Ollama。请手动运行 Ollama 后重试。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        private void AppendLog(string text, Color color)
        {
            if (logBox == null || logBox.IsDisposed) return;
            if (logBox.InvokeRequired) { logBox.BeginInvoke(new Action<string, Color>(AppendLog), text, color); return; }
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionColor = color;
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && String.Equals(Path.GetExtension(files[0]), ".gguf", StringComparison.OrdinalIgnoreCase)) e.Effect = DragDropEffects.Copy;
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                string name = Path.GetFileName(files[0]).ToLowerInvariant();
                if (name.Contains("mmproj") || name.Contains("projector")) SetMmproj(files[0]);
                else SetGguf(files[0]);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (serviceTimer != null) serviceTimer.Stop();
            if (runningProcess != null)
            {
                var result = MessageBox.Show(this, "模型仍在导入。关闭窗口会终止导入，确定继续吗？", "正在导入", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes) { e.Cancel = true; return; }
                try { if (!runningProcess.HasExited) runningProcess.Kill(); } catch { }
            }
        }

        private static string QuoteArg(string value)
        {
            // ProcessStartInfo on Windows receives one command-line string. Backslashes are
            // preserved as-is; only quotes (and backslashes immediately before them) need care.
            if (value.Length == 0) return "\"\"";
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"')
                {
                    sb.Append('\\', slashes * 2 + 1);
                    sb.Append('"');
                    slashes = 0;
                    continue;
                }
                if (slashes > 0) { sb.Append('\\', slashes); slashes = 0; }
                sb.Append(ch);
            }
            if (slashes > 0) sb.Append('\\', slashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes; int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return size.ToString(unit == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + units[unit];
        }
        private void ShowError(string message) { MessageBox.Show(this, message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); }

        private TabPage NewTab(string text) { return new TabPage { Text = text, BackColor = Card, Padding = new Padding(0) }; }
        private TableLayoutPanel NewFormPanel() { return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(28, 24, 28, 20), BackColor = Card }; }
        private Panel NewLinePanel() { return new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 4) }; }
        private TextBox NewTextBox() { return new TextBox { BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 10F), Height = 30 }; }
        private RichTextBox NewCodeBox() { return new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10F), AcceptsTab = true, WordWrap = false, BackColor = Color.FromArgb(250, 251, 253) }; }
        private void AddSectionLabel(TableLayoutPanel panel, string text, int row) { panel.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(44, 51, 67), Padding = new Padding(2, 8, 0, 0) }, 0, row); }
        private Button MakeButton(string text, bool primary, int width)
        {
            var button = new Button { Text = text, Width = width, Height = 38, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = new Font(Font, FontStyle.Bold) };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(198, 204, 216);
            button.BackColor = primary ? Accent : Color.White;
            button.ForeColor = primary ? Color.White : Color.FromArgb(48, 56, 73);
            return button;
        }
    }
}
