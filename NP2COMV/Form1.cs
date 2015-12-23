using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using log4net;
using log4net.Config;
using log4net.Layout;

namespace NP2COMV
{
    public partial class Form1 : Form
    {
        private readonly string servicePath = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).FullName;
        private static readonly List<Connection> ConnectionList = new List<Connection>();
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Program));

        public Form1()
        {
            InitializeComponent();
            XmlConfigurator.ConfigureAndWatch(new FileInfo(servicePath + "log4net.config"));
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            configFilesComboBox.Items.AddRange(Directory.GetFiles(servicePath, "*.n2c"));
            namedPipeComboBox.Items.AddRange(Directory.GetFiles(@"\\.\pipe\"));
            serialPortComboBox.Items.AddRange(SerialPort.GetPortNames());
            parityComboBox.Items.AddRange(Enum.GetNames(typeof(Parity)));
            stopBitsComboBox.Items.AddRange(Enum.GetNames(typeof(StopBits)));
            if (namedPipeComboBox.Items.Count > 0) namedPipeComboBox.SelectedIndex = 0;
            if (serialPortComboBox.Items.Count > 0) serialPortComboBox.SelectedIndex = 0;
            if (configFilesComboBox.Items.Count > 0) configFilesComboBox.SelectedIndex = 0;
            baudRateComboBox.SelectedIndex = 12;
            parityComboBox.SelectedIndex = 0;
            dataBitsComboBox.SelectedIndex = 3;
            stopBitsComboBox.SelectedIndex = 1;

            var rtbAppender = new RichTextBoxAppender { RichTextBox = richTextBox1 };
            BasicConfigurator.Configure(rtbAppender);
            rtbAppender.Layout = new PatternLayout("%-5p %d{HH:mm:ss} - %m%n");

            /*
            ConnectionList.Clear();
            ConnectionList.AddRange(Directory.GetFiles(servicePath, "*.n2c").Select(Settings.Load).Select(c => new Connection(c)));
            Logger.Debug("Loaded (" + ConnectionList.Count + ") connection files");
            ConnectionList.ForEach(c => c.Start());
            */

            if (configFilesComboBox.Items.Count > 0)
                loadConfiguration(configFilesComboBox.Text);

        }

        protected Connection Connection { get; set; }

        /* set action of service toggle button (cross-thread) */
        private void setStartStop(bool state) {
            toggleServiceButton.Text = state ? "Stop" : "Start";
        }

        /* start/stop service */
        private void toggleServiceButton_Click(object sender, EventArgs e)
        {
            if (Connection != null && Connection.IsStarted)
            {
                Connection.Stop();
            }
            else
            {
                var namedPipe = Regex.Match((string)namedPipeComboBox.Text, @"\\\\(?<machine>[^\\]+)\\pipe\\(?<pipe>\w+)");
                Parity parity;
                StopBits stopbits;
                Enum.TryParse((string)parityComboBox.Text, out parity);
                Enum.TryParse((string)stopBitsComboBox.Text, out stopbits);
                Connection = new Connection(new Settings
                {
                    BaudRate = int.Parse((string)baudRateComboBox.Text),
                    ComPort = (string)serialPortComboBox.Text,
                    Parity = parity,
                    StopBits = stopbits,
                    DataBits = int.Parse((string)dataBitsComboBox.Text),
                    MachineName = namedPipe.Groups["machine"].Value,
                    NamedPipe = namedPipe.Groups["pipe"].Value,
                }, this, setStartStop);
                Connection.Start();
            }
        }

        /* write configuration to file (*.n2c) */
        private void writeConfigButton_Click(object sender, EventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "NP2COM (*.n2c)|*.n2c",
                AddExtension = true,
                DefaultExt = "n2c",
            };

            saveConfiguration(sfd);
        }

        /* read configuration from file (*.n2c) */
        private void loadConfigButton_Click(object sender, EventArgs e)
        {
            loadConfiguration(configFilesComboBox.Text);
        }

        private void saveConfiguration(SaveFileDialog sfd)
        {
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                var namedPipe = Regex.Match((string)namedPipeComboBox.SelectedItem,
                                            @"\\\\(?<machine>[^\\]+)\\pipe\\(?<pipe>\w+)");
                Parity parity;
                StopBits stopbits;
                Enum.TryParse((string)parityComboBox.SelectedItem, out parity);
                Enum.TryParse((string)stopBitsComboBox.SelectedItem, out stopbits);
                new Settings()
                {
                    BaudRate = int.Parse((string)baudRateComboBox.SelectedItem),
                    ComPort = (string)serialPortComboBox.SelectedItem,
                    Parity = parity,
                    StopBits = stopbits,
                    DataBits = int.Parse((string)dataBitsComboBox.SelectedItem),
                    MachineName = namedPipe.Groups["machine"].Value,
                    NamedPipe = namedPipe.Groups["pipe"].Value,
                }.Save(sfd.FileName);
            }
        }

        private void loadConfiguration(String fileName)
        {
            Settings settings = Settings.Load(fileName);

            parityComboBox.Text = settings.Parity.ToString();
            stopBitsComboBox.Text = settings.StopBits.ToString();
            dataBitsComboBox.Text = settings.DataBits.ToString();
            baudRateComboBox.Text = settings.BaudRate.ToString();
            namedPipeComboBox.Text = "\\\\" + settings.MachineName + "\\pipe\\" + settings.NamedPipe.ToString();
            serialPortComboBox.Text = settings.ComPort.ToString();
        }

    }
}
