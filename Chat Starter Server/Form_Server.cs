using System;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using ChatStarterCommon;
using ChatStarterServer.Properties;
using System.Threading;
using System.IO;

namespace ChatStarterServer
{
    public partial class Form_Server : Form
    {
        private enum Status { Initial, Listening, Error }
        private ChatServer _chatServer = null;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        bool _listeningBlinkState = false;

        // ip and port config
        private const string ConfigFilePath = "server_config.cfg";

        public Form_Server()
        {
            InitializeComponent();
            LoadIPAddressAndPortFromConfig();
            SetStatus(Status.Initial);
            GenerateIPAdress(chooseLocalIPAddress: true);

            _confirmationTimer = new System.Windows.Forms.Timer(); // Specify the namespace
            _confirmationTimer.Interval = 1000;
            _confirmationTimer.Tick += _confirmationTimer_Tick;
            _confirmationTimer.Start();
        }

        private void UpdateConfigFile(string ipAddress, string port)
        {
            try
            {
                // Update the config file with the new values
                using (StreamWriter sw = new StreamWriter(ConfigFilePath))
                {
                    sw.WriteLine("ip=" + ipAddress); // Update IP address
                    sw.WriteLine("port=" + port);    // Update port
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating config file: {ex.Message}");
            }
        }

        private void LoadIPAddressAndPortFromConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string[] lines = File.ReadAllLines(ConfigFilePath);

                    string configFileIp = null;
                    string configFilePort = null;

                    foreach (string line in lines)
                    {
                        if (line.StartsWith("ip="))
                        {
                            configFileIp = line.Substring(3);
                            // Extract IP address after 'ip='
                        }
                        else if (line.StartsWith("port="))
                        {
                            configFilePort = line.Substring(5);
                            // Extract port after 'port='
                        }
                    }

                    if (configFileIp != null && configFilePort != null)
                    {
                        // Confirmation dialog to overwrite old IP and port with the values from config
                        DialogResult dialogResult = MessageBox.Show("Do you want to overwrite the current IP and port with the values from the config file?",
                            "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        if (dialogResult == DialogResult.Yes)
                        {
                            textBox_ipAddress.Text = configFileIp;
                            textBox_port.Text = configFilePort;
                            UpdateConfigFile(configFileIp, configFilePort); // Update config with loaded values
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Config file not found! Creating a new one with default values.");

                    // Create a new config file with default values
                    using (StreamWriter sw = File.CreateText(ConfigFilePath))
                    {
                        sw.WriteLine("ip=127.0.0.1"); // Default IP address
                        sw.WriteLine("port=8080");    // Default port
                        textBox_ipAddress.Text = "127.0.0.1"; // Set default IP in the textbox
                        textBox_port.Text = "8080";          // Set default port in the textbox
                        UpdateConfigFile("127.0.0.1", "8080"); // Update config with default values
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading or creating config file: {ex.Message}");
            }
        }

        #region Common Methods
        private void SetStatus(Status status)
        {
            switch (status)
            {
                case Status.Initial:
                    Log("Server stopped.");
                    label_status.Text = "Stopped.";
                    label_status.ForeColor = Color.Gray;
                    label_status.Image = Resources.bullet_black;
                    label_localEndPoint.Text = string.Empty;
                    textBox_ipAddress.ReadOnly = false; /* Default true */
                    textBox_port.ReadOnly = false; /* Default true */
                    listBox_clients.Items.Clear();
                    button_start.Enabled = true;
                    button_stop.Enabled = false;
                    timer_listeningBlink.Stop();
                    textBox_ipAddress.Focus();
                    break;
                case Status.Listening:
                    label_status.Text = "Listening...";
                    label_status.ForeColor = Color.Green;
                    label_status.Image = Resources.bullet_green;
                    textBox_ipAddress.ReadOnly = true; /* Default false */
                    textBox_port.ReadOnly = true; /* Default false */
                    button_start.Enabled = false;
                    button_stop.Enabled = true;
                    timer_listeningBlink.Start();
                    break;
                case Status.Error:
                    SetStatus(Status.Initial);
                    label_status.Text = "Error!";
                    label_status.ForeColor = Color.Red;
                    label_status.Image = Resources.bullet_red;
                    break;
                default:
                    break;
            }
        }

        private void AcceptUserAndReceiveTextAsync()
        {
            Task.Factory.StartNew(() =>
            {
                Invoke((MethodInvoker)delegate()
                {
                    Log(string.Format("Server started at {0}:{1}.",
                        _chatServer.EndPoint.Address, _chatServer.EndPoint.Port));
                    label_localEndPoint.Text = string.Format("{0}:{1}",
                        _chatServer.EndPoint.Address, _chatServer.EndPoint.Port);
                    SetStatus(Status.Listening);
                });
                try
                {
                    while (true)
                    {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                        ChatUser user = _chatServer.AcceptUser();
                        Invoke((MethodInvoker)delegate()
                        {
                            listBox_clients.Items.Add(user);
                            _chatServer.BroadcastText(string.Format("{0} joined the chat.", user.Name));
                            Log(string.Format("{0} joined the chat.", user));
                        });
                        ReceiveTextAsync(user);
                    }
                }
                catch (Exception exception)
                {
                    Invoke((MethodInvoker)delegate()
                    {
                        MessageBox.Show(exception.Message, Application.ProductName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Log(string.Format("Error: {0}", exception.Message));
                        SetStatus(Status.Initial);
                    });
                }
            }, _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void ReceiveTextAsync(ChatUser user)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    while (true)
                    {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                        string message = _chatServer.ReceiveText(user.ID);
                        _chatServer.BroadcastText(string.Format("{0}: {1}", user.Name, message));
                        Invoke((MethodInvoker)delegate()
                        {
                            Log(string.Format("{0} sent a message: {1}", user, message));
                        });
                    }
                }
                catch
                {
                    Invoke((MethodInvoker)delegate()
                    {
                        Log(string.Format("{0} left the chat.", user));
                        listBox_clients.Items.Remove(user);
                    });
                }
            }, _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void Log(string text)
        {
            textBox_log.AppendText(string.Format("[{0}] ", DateTime.Now.ToString("HH:mm:ss")));
            textBox_log.AppendText(text);
            textBox_log.AppendText(Environment.NewLine);
        }

        private void SetError(Control control = null, string text = null)
        {
            if (control == null && text == null)
            {
                errorProvider_main.Clear();
                return;
            }
            errorProvider_main.SetError(control, text);
        }

        private void GenerateIPAdress(bool chooseLocalIPAddress = false)
        {
            if (chooseLocalIPAddress)
            {
                textBox_ipAddress.Text = Network.GetLocalIPAddress().ToString();
                return;
            }
            textBox_ipAddress.Text = Network.GenerateIPAddressString();
        }

        private void GeneratePort()
        {
            textBox_port.Text = Network.GeneratePort().ToString();
        }
        #endregion

        private void button_start_Click(object sender, EventArgs e)
        {
            bool inputHasErrors = false;
            IPAddress ipAddress;
            int port;

            SetError();
            if (!Network.TryParseIPAddress(textBox_ipAddress.Text, out ipAddress))
            {
                SetError(label_ipAddress, "Invalid IP Address");
                textBox_ipAddress.Focus();
                textBox_ipAddress.SelectAll();
                inputHasErrors = true;
            }
            if (!Network.TryParsePort(textBox_port.Text, out port))
            {
                SetError(label_port, "Invalid Port");
                textBox_ipAddress.Focus();
                textBox_port.SelectAll();
                inputHasErrors = true;
            }
            if (inputHasErrors)
            {
                return;
            }

            try
            {
                _chatServer = new ChatServer(ipAddress, port);
                _chatServer.Listen();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, Application.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log(string.Format("Error: {0}", exception.Message));
                SetStatus(Status.Initial);
            }

            AcceptUserAndReceiveTextAsync();
        }

        private void button_stop_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            SetStatus(Status.Initial);
            _chatServer.Stop(close: true);
        }

        private void timer_listeningBlink_Tick(object sender, EventArgs e)
        {
            _listeningBlinkState = !_listeningBlinkState;

            if (_listeningBlinkState)
            {
                label_status.Image = Resources.bullet_black;
                label_status.ForeColor = Color.Gray;
            }
            else
            {
                label_status.Image = Resources.bullet_green;
                label_status.ForeColor = Color.Green;
            }
        }

        private void label_ipAddress_Click(object sender, EventArgs e)
        {
            GenerateIPAdress();
        }

        private void label_port_Click(object sender, EventArgs e)
        {
            GeneratePort();
        }

        private void _confirmationTimer_Tick(object sender, EventArgs e)
        {
            _confirmationTimer.Stop(); // Stop the timer once it ticks

            // Show a confirmation dialog to overwrite old IP and port with the values from config
            DialogResult dialogResult = MessageBox.Show("Do you want to overwrite the current IP and port with the values from the config file?",
                "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (dialogResult == DialogResult.Yes)
            {
                // Load IP and port from config without self-checking
                LoadIPAddressAndPortFromConfig();
            }
        }
    }
}
