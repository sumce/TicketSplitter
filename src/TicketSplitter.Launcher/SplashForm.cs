namespace TicketSplitter.Launcher;

/// <summary>首次运行时释放内嵌文件需要几秒，给个提示免得看着像卡死。</summary>
internal sealed class SplashForm : Form
{
    public SplashForm()
    {
        Text = "作业票切分工具";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(360, 120);
        BackColor = Color.White;
        ShowInTaskbar = false;
        TopMost = true;

        var title = new Label
        {
            Text = "正在准备运行环境…",
            Font = new Font("Microsoft YaHei UI", 11F),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 56,
        };

        var hint = new Label
        {
            Text = "首次启动需要释放文件，之后会快很多",
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = Color.Gray,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Bottom,
            Height = 28,
        };

        var bar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 26,
            Dock = DockStyle.Bottom,
            Height = 6,
        };

        Controls.Add(title);
        Controls.Add(hint);
        Controls.Add(bar);
    }
}
