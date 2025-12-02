using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

using HtmlAgilityPack;

namespace AION.Chat
{
    public partial class Chat : Form
    {
        private Dictionary<string, RichTextBox> Channels { get; set; }
        private TriggerForm TriggerForm { get; set; } = new();
        private BanForm BanForm { get; set; } = new();
        private RichTextBox Selected { get; set; }

        private string ChatLogPath { get; set; }
        private StreamReader Reader { get; set; }
        private FileSystemWatcher ChatLogWatcher { get; set; }
        private FileStream FileStream { get; set; }
        private NotifyIcon Notify { get; set; } = new();
        private HtmlWeb Web { get; set; } = new();
        private ComponentResourceManager Resources { get; set; } = new(typeof(Properties.Resources));
        private bool IsSoundEnabled { get; set; } = true;
        private bool IsSoundPlaying { get; set; }

        public Chat()
        {
            InitializeComponent();
            Notify.Visible = true;
            Notify.Icon = Icon;
            Notify.BalloonTipClicked += (sender, e) => IsSoundEnabled = false;

            Channels = new()
            {
                { "All", CreateChannel() },
                { "LFG", CreateChannel() },
                { "PM", CreateChannel() },
            };
            Selected = Channels["All"];
        }

        private void ToggleChannel(RichTextBox channel)
        {
            foreach (RichTextBox c in Channels.Values)
                c.Hide();

            Selected = channel;
            Selected.Show();
        }

        private RichTextBox CreateChannel()
        {
            RichTextBox channel = new()
            {
                Margin = new Padding(4),
                Font = new Font("Consolas", 10, FontStyle.Regular),
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                ForeColor = Color.Silver,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = true,
                ReadOnly = true,
                Text = "",
            };
            ChatPanel.Controls.Add(channel);
            return channel;
        }

        private void SyncChatLog()
        {
            FileStream?.Dispose();
            Reader?.Dispose();
            ChatLogWatcher?.Dispose();

            if (!File.Exists(ChatLogPath))
                return;

            FileStream = new FileStream(ChatLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Reader = new StreamReader(FileStream, Encoding.Default);
            Reader.ReadToEnd();

            ChatLogWatcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(ChatLogPath),
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = "chat.log",
                EnableRaisingEvents = true
            };
            ChatLogWatcher.Changed += ChatLog_Changed;
        }

        public void ChangeLogFile()
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "Log files (*.log)|*.log|All files (*.*)|*.*",
                Title = "Select the AION chat.log file"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                ChatLogPath = dialog.FileName;
                SyncChatLog();
            }
        }

        private void ProcessLine(string line)
        {
            RichTextBox channel = null;
            Color color = Color.DarkGray;
            StringComparison ignore = StringComparison.CurrentCultureIgnoreCase;

            if (line.Contains("[3.LFG] [charname:", ignore))
            {
                channel = Channels["LFG"];
                color = Color.FromArgb(255, 177, 177);
            }
            else if (line.Contains("Whispers:", ignore) || line.Contains("You Whisper to [charname:", ignore))
            {
                channel = Channels["PM"];
                color = Color.FromArgb(160, 255, 160);
            }
            else if (line.Contains("You shout \"", ignore))
                color = Color.Sienna;

            // Parse and write log date
            ProcessDate(ref line);
            // Parse and edit links
            ProcessLinks(ref line);
            // Fix broken chars
            ProcessChars(ref line);
            // Print edited line
            Print(line + "\n", color, channel);
            // Notify
            Task.Run(() => Invoke(() => CheckNotify(line)));
        }

        private void ProcessChars(ref string line)
        {
            line = line.Replace("&#39;", "'");
        }

        private async Task CheckNotify(string line)
        {
            var triggers = TriggerForm.TriggerBox.Items.Cast<ListViewItem>();
            if (!triggers.Any() || !triggers.Where(b => b.Checked).Any(b => line.Contains(b.Text)))
                return;

            var bans = BanForm.BanBox.Items.Cast<ListViewItem>();
            if (bans.Where(b => b.Checked).Any(b => line.Contains(b.Text)))
                return;

            string trigger = TriggerForm.TriggerBox.Items.Cast<ListViewItem>()
                .Where(b => b.Checked).FirstOrDefault(b => line.Contains(b.Text)).Text;

            Notify.ShowBalloonTip(20 * 1000, trigger, line, ToolTipIcon.Info);

            await NotifySoundLoop();
        }

        private async Task NotifySoundLoop()
        {
            if (IsSoundPlaying || !IsSoundEnabled)
                return;

            IsSoundPlaying = true;
            for (int i = 0; i < 30 && IsSoundEnabled; i++)
            {
                System.Media.SystemSounds.Beep.Play();
                await Task.Delay(1000);
            }
            IsSoundPlaying = false;
        }

        private void ProcessLinks(ref string line)
        {
            Regex regexLink = new(@"(\[.*?:.*?])");
            Match link = regexLink.Match(line);

            while (link != null && link.Success)
            {
                string value = link.Value;
                line = line.Remove(link.Index, link.Length);

                if (value.Contains("item:"))
                {
                    // Get item id from the link
                    string item_id = value.Split(':')[1];
                    if (item_id.Contains(';'))
                        item_id = item_id[..item_id.IndexOf(';')];

                    // Get item name from aioncodex title
                    HtmlAgilityPack.HtmlDocument doc = Web.Load($"https://aioncodex.com/en/item/{item_id}");
                    string item_name = doc.DocumentNode.SelectSingleNode("html/head/title")
                        .InnerText.Replace(" - Aion Codex", "") ?? $"item:{item_id}";

                    value = "<" + item_name + ">";
                    line = line.Insert(link.Index, value);
                    link = regexLink.Match(line);
                    continue;
                }
                if (value.Contains("charname:"))
                {
                    // Get char name from the link
                    string character = value.Split(':')[1];
                    if (character.Contains(';'))
                        character = character[..character.IndexOf(';')];

                    value = character;
                    line = line.Insert(link.Index, value);
                    link = regexLink.Match(line);
                    continue;
                }
                break;
            }
        }

        public void ProcessDate(ref string line)
        {
            Regex time = new(@"([0-9:]{8})");
            Regex removeDate = new(@"(.*([0-9:]{8}).* : )");
            string date = "(" + time.Match(line).Value + ") ";

            line = removeDate.Replace(line, date);
        }

        public void Print(string line, Color color, RichTextBox channel = null)
        {
            // Write to the main channel
            Channels["All"].SelectionColor = color;
            Channels["All"].AppendText(line);
            Channels["All"].ScrollToCaret();

            // Write to the processed channel
            if (channel != null)
            {
                channel.SelectionColor = color;
                channel.AppendText(line);
                channel.ScrollToCaret();
            }
        }

        private void ButtonMute_Click(object sender, EventArgs e)
        {
            IsSoundEnabled = !IsSoundEnabled;
            ButtonMute.BackgroundImage = IsSoundEnabled
                ? (Image)Resources.GetObject("unmuted")
                : (Image)Resources.GetObject("muted");
        }

        private void ButtonClean_Click(object sender, EventArgs e) => Selected?.Clear();
        private void ButtonAll_Click(object sender, EventArgs e) => ToggleChannel(Channels["All"]);
        private void ButtonLFG_Click(object sender, EventArgs e) => ToggleChannel(Channels["LFG"]);
        private void ButtonPM_Click(object sender, EventArgs e) => ToggleChannel(Channels["PM"]);
        private void ButtonTrigger_Click(object sender, EventArgs e) => TriggerForm.Show();
        private void ButtonBan_Click(object sender, EventArgs e) => BanForm.Show();
        private void ButtonLog_Click(object sender, EventArgs e) => ChangeLogFile();
        
        private void ChatLog_Changed(object sender, FileSystemEventArgs e)
        {
            for (string line = Reader.ReadLine(); line != null; line = Reader.ReadLine())
            {
                if (!string.IsNullOrEmpty(line) && IsHandleCreated)
                    Invoke(() => ProcessLine(line));
            }
        }

        private void Chat_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.LogPath = ChatLogPath;

            Properties.Settings.Default.TriggersOn = [];
            Properties.Settings.Default.TriggersOn.AddRange(TriggerForm.TriggerBox.Items
                .Cast<ListViewItem>().Where(b => b.Checked).Select(b => b.Text).ToArray());

            Properties.Settings.Default.TriggersOff = [];
            Properties.Settings.Default.TriggersOff.AddRange(TriggerForm.TriggerBox.Items
                .Cast<ListViewItem>().Where(b => !b.Checked).Select(b => b.Text).ToArray());

            Properties.Settings.Default.BanOn = [];
            Properties.Settings.Default.BanOn.AddRange(BanForm.BanBox.Items
                .Cast<ListViewItem>().Where(b => b.Checked).Select(b => b.Text).ToArray());

            Properties.Settings.Default.BanOff = [];
            Properties.Settings.Default.BanOff.AddRange(BanForm.BanBox.Items
                .Cast<ListViewItem>().Where(b => !b.Checked).Select(b => b.Text).ToArray());

            Properties.Settings.Default.Save();
        }

        private void Chat_Load(object sender, EventArgs e)
        {
            ChatLogPath = Properties.Settings.Default.LogPath;

            Properties.Settings.Default.TriggersOn?.Cast<string>().ToList()
                .ForEach(item => TriggerForm.TriggerBox.Items.Add(new ListViewItem(item) { Checked = true }));

            Properties.Settings.Default.TriggersOff?.Cast<string>().ToList()
                .ForEach(item => TriggerForm.TriggerBox.Items.Add(new ListViewItem(item) { Checked = false }));

            Properties.Settings.Default.BanOn?.Cast<string>().ToList()
                .ForEach(item => BanForm.BanBox.Items.Add(new ListViewItem(item) { Checked = true }));

            Properties.Settings.Default.BanOff?.Cast<string>().ToList()
                .ForEach(item => BanForm.BanBox.Items.Add(new ListViewItem(item) { Checked = false }));

            SyncChatLog();
        }

        public new void Dispose()
        {
            foreach (RichTextBox chat in Channels.Values)
                chat?.Dispose();

            ChatLogWatcher?.Dispose();
            Reader?.Dispose();
            FileStream?.Dispose();

            base.Dispose();
        }
    }
}
