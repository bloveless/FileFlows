using FileFlows.Node;
using FileFlows.Node.Workers;
using FileFlows.Server.Workers;
using FileFlows.ServerShared.Models;

namespace FileFlows.WindowsNode
{
    public partial class Form1 : Form
    {
        public Form1(bool minimize)
        {
            InitializeComponent();
            this.txtServer.Text = AppSettings.Instance.ServerUrl ?? String.Empty;
            this.txtTempPath.Text = AppSettings.Instance.TempPath ?? String.Empty;
            this.numRunners.Value = AppSettings.Instance.Runners;
            this.chkEnabled.Checked = AppSettings.Instance.Enabled;


            ServerShared.Services.Service.ServiceBaseUrl = this.txtServer.Text;
            WorkerManager.StartWorkers(new FlowWorker()
            {
                IsEnabledCheck = () => AppSettings.Instance.Enabled
            });

            if (minimize)
            {
                this.Hide();
                this.WindowState = FormWindowState.Minimized;
                notifyIcon.Visible = true;
            }
            else
            {

                this.Show();
                this.WindowState = FormWindowState.Normal;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (IsHandleCreated == false && value)
            {
                value = false;
                CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if(disposing)
                WorkerManager.StopWorkers();
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }


        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                Hide();
                notifyIcon.Visible = true;
            }

        }

        void Open()
        {
            Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon.Visible = false;

        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e) => Open();
        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e) => Open();

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void EnableForm(bool enable = true)
        {
            if (this.txtServer.InvokeRequired)
            {
                Invoke(new Action(() => EnableForm(enable)));
                return;
            }
            this.txtServer.Enabled = enable;
            this.txtTempPath.Enabled = enable;
            this.btnBrowse.Enabled = enable;    
            this.numRunners.Enabled = enable;
            this.btnSave.Enabled = enable;
            this.chkEnabled.Enabled = enable;
        }

        private void SetUrl(string url)
        {
            if(this.txtServer.InvokeRequired)
            {
                Invoke(new Action(() => SetUrl(url)));
                return;
            }
            txtServer.Text = url;
        }

        private void ShowMessageBox(string text)
        {
            if (this.txtServer.InvokeRequired)
            {
                Invoke(new Action(() => ShowMessageBox(text)));
                return;
            }
            MessageBox.Show(text, "Failed to Connect");
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            string url = this.txtServer.Text;
            string path = this.txtTempPath.Text;
            int runners = (int)this.numRunners.Value;
            bool enabled = this.chkEnabled.Checked;
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(path))
                return;

            EnableForm(false);
            Task.Run(async () =>
            {
                List<RegisterModelMapping> mappings = new List<RegisterModelMapping>
                {
                    new RegisterModelMapping
                    { 
                        Server = "ffmpeg",
                        Local = Path.Combine(new FileInfo(Application.ExecutablePath).DirectoryName, "utils\\ffmpeg.exe")
                    }
                };

                var result = ConnectionTester.SaveConnection(url, path, runners, enabled, mappings);
                if (result.Item1)
                {
                    // can connect
                    SetUrl(result.Item2);
                    ServerShared.Services.Service.ServiceBaseUrl = result.Item2;
                    AppSettings.Save(new AppSettings
                    {
                        ServerUrl = result.Item2,
                        Enabled = enabled,
                        Runners = runners,
                        TempPath = path
                    });
                }
                else
                {
                    // cant connect
                    ShowMessageBox(result.Item2);
                }
                EnableForm(true);
            });
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            if(folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                this.txtTempPath.Text = folderBrowserDialog.SelectedPath;
            }
        }
    }
}