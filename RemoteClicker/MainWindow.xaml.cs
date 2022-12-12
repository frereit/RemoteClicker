using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Formatter;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WindowsInput;

namespace RemoteClicker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// The managed subscriber client.
        /// </summary>
        private IManagedMqttClient? managedMqttClientSubscriber;

        /// <summary>
        /// The session id
        /// </summary>
        private string sessionId;

        /// <summary>
        /// The session key
        /// </summary>
        private byte[] sessionKey;

        /// <summary>
        /// The input simulator instance
        /// </summary>
        private InputSimulator inputSimulator = new InputSimulator();

        private string sessionTopic
        {
            get
            {
                // FIXME: Hardcoded topic path
                return "remoteclicker/" + sessionId;
            }
        }

        private string sessionLink
        {
            get
            {
                return "https://frereit.github.io/RemoteClicker/?" + sessionId
                    + "#" + BitConverter.ToString(sessionKey).Replace("-", string.Empty);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            CreateSubscription();
        }

        private async void CreateSubscription()
        {
            var mqttFactory = new MqttFactory();
            var tlsOptions = new MqttClientTlsOptions
            {
                UseTls = true,
                IgnoreCertificateChainErrors = false,
                IgnoreCertificateRevocationErrors = false,
                AllowUntrustedCertificates = false
            };

            var options = new MqttClientOptions
            {
                ClientId = "ClientPublisher",
                ProtocolVersion = MqttProtocolVersion.V311,
                ChannelOptions = new MqttClientTcpOptions
                {
                    // FIXME: hardcoded broker
                    Server = "broker.hivemq.com",
                    Port = 8883,
                    TlsOptions = tlsOptions
                },
                CleanSession = true,
                KeepAlivePeriod = TimeSpan.FromSeconds(5),
            };

            this.managedMqttClientSubscriber = mqttFactory.CreateManagedMqttClient();

            this.managedMqttClientSubscriber = mqttFactory.CreateManagedMqttClient();
            this.managedMqttClientSubscriber.ConnectedAsync += this.OnSubscriberConnected;
            this.managedMqttClientSubscriber.DisconnectedAsync += this.OnSubscriberDisconnected;
            this.managedMqttClientSubscriber.ApplicationMessageReceivedAsync += this.OnSubscriberMessageReceived;

            await this.managedMqttClientSubscriber.StartAsync(
                new ManagedMqttClientOptions
                {
                    ClientOptions = options
                });
        }

        private void OnCleartextMessageReceived(string message)
        {
            if (message == "next")
            {
                nextSlide();
            } else if (message == "prev")
            {
                prevSlide();
            }
            Dispatcher.BeginInvoke((Action)(() =>
            {
                // FIXME: i18n, MVVM
                l_Status.Content = message;
            }));
        }

        private void nextSlide()
        {
            inputSimulator.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.NEXT);
        }

        private void prevSlide()
        {
            inputSimulator.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.PRIOR);
        }

        /// <summary>
        /// Decrypts any incoming messages
        /// </summary>
        private Task OnSubscriberMessageReceived(MqttApplicationMessageReceivedEventArgs args)
        {
            var payload = args.ApplicationMessage.Payload;
            var nonce = payload[..12];
            var ciphertext = payload[12..(payload.Length - 16)];
            var tag = payload[(payload.Length - 16)..];

            if (this.sessionKey == null)
            {
                return Task.CompletedTask;
            }

            using var aes = new AesGcm(this.sessionKey);
            var plaintextBytes = new byte[ciphertext.Length];
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            OnCleartextMessageReceived(Encoding.UTF8.GetString(plaintextBytes));
            return Task.CompletedTask;
        }


        /// <summary>
        /// Handles the subscriber connected event.
        /// </summary>
        private Task OnSubscriberConnected(MqttClientConnectedEventArgs args)
        {
            var status = "Connected to broker!";
            if (args.ConnectResult.ResultCode != 0)
            {
                // FIXME: Hardcoded broker name
                status = "Failed to connect to HiveMQ";
            }
            Dispatcher.BeginInvoke((Action)(() =>
            {
                // FIXME: i18n, MVVM
                l_Status.Content = status;
            }));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles the subscriber disconnected event
        /// </summary>
        private Task OnSubscriberDisconnected(MqttClientDisconnectedEventArgs args)
        {
            var status = "Disconnected!";
            if (args.Exception != null)
            {
                // FIXME: Hardcoded broker name
                status = "Disconnected from HiveMQ due to an exception: " + args.Exception.Message;
            }
            Dispatcher.BeginInvoke((Action)(() =>
            {
                l_Status.Content = status;
            }));
            return Task.CompletedTask;
        }

        private void refreshSession()
        {
            var sessionId = Guid.NewGuid().ToString("n").Substring(0, 16);
            using (var rng = RandomNumberGenerator.Create())
            {
                var sessionKey = new byte[16];
                rng.GetBytes(sessionKey);

                this.sessionId = sessionId;
                this.sessionKey = sessionKey;
            }

            Dispatcher.BeginInvoke((Action)(() =>
            {
                tb_SessionLink.Text = sessionLink;
            }));
        }

        private async void b_NewSession_Click(object sender, RoutedEventArgs e)
        {
            if (!this.managedMqttClientSubscriber.IsConnected)
            {
                MessageBox.Show("You are not connected!");
                return;
            }
            await this.managedMqttClientSubscriber.UnsubscribeAsync(sessionTopic);
            this.refreshSession();
            await this.managedMqttClientSubscriber.SubscribeAsync(sessionTopic);
            _ = Dispatcher.BeginInvoke((Action)(() =>
              {
                  l_Status.Content = "Listening!";
              }));
        }

        private void b_Copy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(sessionLink);
        }
    }
}
