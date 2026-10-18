using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FeynmanPet
{
    internal enum AssistantUiState
    {
        Ready,
        Listening,
        Recognizing,
        Thinking,
        Speaking,
        Cancelled,
        Error
    }

    internal sealed class AssistantWindow : Window
    {
        private readonly string projectRoot;
        private readonly PetWindow pet;
        private readonly AssistantSettings settings;
        private readonly bool persistSettings;
        private readonly WindowsSpeechRecognitionService recognition;
        private readonly WindowsTtsService windowsTts;
        private readonly ESpeakTtsService eSpeakTts;
        private readonly CloudTtsService cloudTts;
        private readonly DeepSeekChatService chat;

        private readonly TextBlock statusText;
        private readonly TextBlock keyStatusText;
        private readonly TextBlock voiceStatusText;
        private readonly TextBox conversationBox;
        private readonly TextBox transcriptBox;
        private readonly ComboBox languageCombo;
        private readonly ComboBox answerLanguageCombo;
        private readonly ComboBox serviceCombo;
        private readonly ComboBox chineseVoiceCombo;
        private readonly ComboBox englishVoiceCombo;
        private readonly Slider rateSlider;
        private readonly CheckBox autoSpeakCheck;
        private readonly CheckBox showMicCheck;
        private readonly TextBox modelBox;
        private readonly TextBox promptBox;
        private readonly TextBox cloudVoiceIdBox;
        private readonly Button micButton;
        private readonly Button cancelRecordingButton;
        private readonly Button reRecordButton;
        private readonly Button sendButton;
        private readonly Button cancelRequestButton;
        private readonly Button stopSpeechButton;
        private readonly Button clearButton;
        private readonly Button testVoiceButton;

        private CancellationTokenSource chatCancellation;
        private CancellationTokenSource speechCancellation;
        private int operationVersion;
        private bool isSending;
        private bool isRecognizing;
        private bool isSpeaking;
        private bool closing;
        private AssistantUiState state;

        public AssistantWindow(string projectRoot, PetWindow pet, bool persistSettings)
        {
            this.projectRoot = projectRoot;
            this.pet = pet;
            this.persistSettings = persistSettings;
            settings = AssistantSettings.Load(projectRoot);
            recognition = new WindowsSpeechRecognitionService();
            windowsTts = new WindowsTtsService();
            eSpeakTts = new ESpeakTtsService();
            cloudTts = new CloudTtsService();
            chat = new DeepSeekChatService();

            Title = "费曼 AI 对话";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Width = 420;
            Height = Math.Min(540, Math.Max(460, SystemParameters.WorkArea.Height - 20));
            MinHeight = 430;
            MaxHeight = Math.Max(430, SystemParameters.WorkArea.Height - 20);
            Opacity = 0;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;
            Topmost = pet.Topmost;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            Border outer = new Border();
            outer.Margin = new Thickness(10);
            outer.CornerRadius = new CornerRadius(14);
            outer.Background = new SolidColorBrush(Color.FromRgb(244, 239, 228));
            outer.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 43, 41));
            outer.BorderThickness = new Thickness(1.5);
            DropShadowEffect windowShadow = new DropShadowEffect();
            windowShadow.BlurRadius = 18;
            windowShadow.ShadowDepth = 5;
            windowShadow.Opacity = 0.28;
            windowShadow.Color = Colors.Black;
            outer.Effect = windowShadow;

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            outer.Child = layout;

            Grid header = BuildHeader();
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            StackPanel content = new StackPanel();
            content.Margin = new Thickness(14, 10, 14, 14);
            ScrollViewer contentScroll = new ScrollViewer();
            contentScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            contentScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            contentScroll.Content = content;
            Grid.SetRow(contentScroll, 1);
            layout.Children.Add(contentScroll);

            statusText = new TextBlock();
            statusText.Text = "● 待机";
            statusText.FontSize = 12.5;
            statusText.FontWeight = FontWeights.SemiBold;
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(52, 94, 78));
            statusText.Margin = new Thickness(0, 0, 0, 7);
            content.Children.Add(statusText);

            conversationBox = new TextBox();
            conversationBox.Height = 155;
            conversationBox.IsReadOnly = true;
            conversationBox.AcceptsReturn = true;
            conversationBox.TextWrapping = TextWrapping.Wrap;
            conversationBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            conversationBox.Background = new SolidColorBrush(Color.FromRgb(255, 251, 241));
            conversationBox.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 177, 161));
            conversationBox.Padding = new Thickness(9);
            conversationBox.FontSize = 13;
            conversationBox.Text = "";
            content.Children.Add(conversationBox);

            TextBlock transcriptLabel = new TextBlock();
            transcriptLabel.Text = "语音识别文本（提交前可修改）";
            transcriptLabel.FontSize = 11.5;
            transcriptLabel.FontWeight = FontWeights.SemiBold;
            transcriptLabel.Foreground = new SolidColorBrush(Color.FromRgb(75, 67, 58));
            transcriptLabel.Margin = new Thickness(0, 10, 0, 4);
            content.Children.Add(transcriptLabel);

            transcriptBox = new TextBox();
            transcriptBox.Height = 70;
            transcriptBox.AcceptsReturn = true;
            transcriptBox.TextWrapping = TextWrapping.Wrap;
            transcriptBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            transcriptBox.Background = new SolidColorBrush(Color.FromRgb(255, 251, 241));
            transcriptBox.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 177, 161));
            transcriptBox.Padding = new Thickness(8);
            transcriptBox.FontSize = 13;
            content.Children.Add(transcriptBox);

            WrapPanel microphoneControls = new WrapPanel();
            microphoneControls.Margin = new Thickness(0, 8, 0, 0);
            micButton = CreateButton("开始说话", Color.FromRgb(52, 94, 78), Colors.White);
            micButton.Click += async delegate { await ToggleRecordingAsync(); };
            microphoneControls.Children.Add(micButton);
            cancelRecordingButton = CreateButton("取消录音", Color.FromRgb(166, 107, 72), Colors.White);
            cancelRecordingButton.Click += delegate { CancelRecording(); };
            microphoneControls.Children.Add(cancelRecordingButton);
            reRecordButton = CreateButton("重新录音", Color.FromRgb(88, 103, 113), Colors.White);
            reRecordButton.Click += async delegate { await RestartRecordingAsync(); };
            microphoneControls.Children.Add(reRecordButton);
            content.Children.Add(microphoneControls);

            WrapPanel answerControls = new WrapPanel();
            answerControls.Margin = new Thickness(0, 6, 0, 0);
            sendButton = CreateButton("提问", Color.FromRgb(44, 77, 104), Colors.White);
            sendButton.Click += async delegate { await SendQuestionAsync(); };
            answerControls.Children.Add(sendButton);
            cancelRequestButton = CreateButton("取消等待", Color.FromRgb(139, 103, 76), Colors.White);
            cancelRequestButton.Click += delegate { CancelCurrentRequest(); };
            answerControls.Children.Add(cancelRequestButton);
            stopSpeechButton = CreateButton("停止播报", Color.FromRgb(120, 72, 74), Colors.White);
            stopSpeechButton.Click += delegate { StopSpeech(); };
            answerControls.Children.Add(stopSpeechButton);
            clearButton = CreateButton("清除对话", Color.FromRgb(91, 95, 100), Colors.White);
            clearButton.Click += delegate { ClearConversation(); };
            answerControls.Children.Add(clearButton);
            content.Children.Add(answerControls);

            Expander settingsExpander = new Expander();
            settingsExpander.Header = "语音与 AI 设置";
            settingsExpander.Margin = new Thickness(0, 10, 0, 0);
            settingsExpander.Foreground = new SolidColorBrush(Color.FromRgb(54, 48, 43));
            settingsExpander.IsExpanded = false;
            StackPanel settingsPanel = new StackPanel();
            settingsPanel.Margin = new Thickness(2, 7, 2, 2);
            settingsExpander.Content = settingsPanel;

            Grid serviceGrid = new Grid();
            serviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            serviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            serviceGrid.Margin = new Thickness(0, 2, 0, 2);
            TextBlock serviceLabel = CreateFieldLabel("语音服务");
            serviceGrid.Children.Add(serviceLabel);
            serviceCombo = new ComboBox();
            ComboBoxItem windowsItem = new ComboBoxItem();
            windowsItem.Content = "Windows 本地语音";
            windowsItem.Tag = "windows";
            serviceCombo.Items.Add(windowsItem);
            ComboBoxItem espeakItem = new ComboBoxItem();
            espeakItem.Content = eSpeakTts.IsAvailable
                ? "eSpeak NG（本地男声）"
                : "eSpeak NG（未安装）";
            espeakItem.Tag = "espeak";
            espeakItem.IsEnabled = eSpeakTts.IsAvailable;
            serviceCombo.Items.Add(espeakItem);
            ComboBoxItem cloudItem = new ComboBoxItem();
            cloudItem.Content = "ElevenLabs 云端（预留，未启用）";
            cloudItem.Tag = "cloud";
            cloudItem.IsEnabled = false;
            serviceCombo.Items.Add(cloudItem);
            serviceCombo.SelectedIndex = 0;
            serviceCombo.SelectionChanged += delegate
            {
                if (IsLoaded)
                {
                    RefreshVoiceChoices();
                }
            };
            Grid.SetColumn(serviceCombo, 1);
            serviceGrid.Children.Add(serviceCombo);
            settingsPanel.Children.Add(serviceGrid);

            Grid voicePairGrid = new Grid();
            voicePairGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            voicePairGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(8) });
            voicePairGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            voicePairGrid.Margin = new Thickness(0, 4, 0, 2);

            StackPanel chineseVoicePanel = new StackPanel();
            chineseVoicePanel.Margin = new Thickness(0, 0, 0, 0);
            chineseVoicePanel.Children.Add(CreateFieldLabel("中文声音"));
            chineseVoiceCombo = new ComboBox();
            chineseVoiceCombo.Margin = new Thickness(0, 2, 0, 0);
            chineseVoicePanel.Children.Add(chineseVoiceCombo);
            Grid.SetColumn(chineseVoicePanel, 0);
            voicePairGrid.Children.Add(chineseVoicePanel);

            StackPanel englishVoicePanel = new StackPanel();
            englishVoiceCombo = new ComboBox();
            englishVoiceCombo.Margin = new Thickness(0, 2, 0, 0);
            englishVoicePanel.Children.Add(CreateFieldLabel("English 声音"));
            englishVoicePanel.Children.Add(englishVoiceCombo);
            Grid.SetColumn(englishVoicePanel, 2);
            voicePairGrid.Children.Add(englishVoicePanel);
            settingsPanel.Children.Add(voicePairGrid);

            Grid languageGrid = new Grid();
            languageGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            languageGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(8) });
            languageGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            languageGrid.Margin = new Thickness(0, 4, 0, 2);

            StackPanel recognitionLanguagePanel = new StackPanel();
            recognitionLanguagePanel.Children.Add(CreateFieldLabel("语音识别语言"));
            languageCombo = new ComboBox();
            languageCombo.Items.Add(MakeComboItem("自动（识别器自动选择）", "auto"));
            languageCombo.Items.Add(MakeComboItem("中文", "zh"));
            languageCombo.Items.Add(MakeComboItem("English", "en"));
            languageCombo.SelectedIndex = 0;
            languageCombo.Margin = new Thickness(0, 2, 0, 0);
            recognitionLanguagePanel.Children.Add(languageCombo);
            Grid.SetColumn(recognitionLanguagePanel, 0);
            languageGrid.Children.Add(recognitionLanguagePanel);

            StackPanel answerLanguagePanel = new StackPanel();
            answerLanguagePanel.Children.Add(CreateFieldLabel("回答语言"));
            answerLanguageCombo = new ComboBox();
            answerLanguageCombo.Items.Add(MakeComboItem("自动", "auto"));
            answerLanguageCombo.Items.Add(MakeComboItem("始终中文", "zh"));
            answerLanguageCombo.Items.Add(MakeComboItem("Always English", "en"));
            answerLanguageCombo.SelectedIndex = 0;
            answerLanguageCombo.Margin = new Thickness(0, 2, 0, 0);
            answerLanguagePanel.Children.Add(answerLanguageCombo);
            Grid.SetColumn(answerLanguagePanel, 2);
            languageGrid.Children.Add(answerLanguagePanel);
            settingsPanel.Children.Add(languageGrid);

            Grid rateGrid = new Grid();
            rateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            rateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rateGrid.Margin = new Thickness(0, 2, 0, 2);
            rateGrid.Children.Add(CreateFieldLabel("语速"));
            rateSlider = new Slider();
            rateSlider.Minimum = -4;
            rateSlider.Maximum = 4;
            rateSlider.TickFrequency = 1;
            rateSlider.IsSnapToTickEnabled = true;
            rateSlider.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(rateSlider, 1);
            rateGrid.Children.Add(rateSlider);
            settingsPanel.Children.Add(rateGrid);

            autoSpeakCheck = new CheckBox();
            autoSpeakCheck.Content = "自动朗读 AI 回答";
            autoSpeakCheck.Margin = new Thickness(0, 5, 0, 3);
            settingsPanel.Children.Add(autoSpeakCheck);

            showMicCheck = new CheckBox();
            showMicCheck.Content = "在桌宠旁显示麦克风按钮";
            showMicCheck.Margin = new Thickness(0, 2, 0, 4);
            settingsPanel.Children.Add(showMicCheck);

            settingsPanel.Children.Add(CreateFieldLabel("DeepSeek 模型"));
            modelBox = CreateSmallTextBox();
            modelBox.Margin = new Thickness(0, 2, 0, 5);
            settingsPanel.Children.Add(modelBox);

            settingsPanel.Children.Add(CreateFieldLabel("AI 角色提示词"));
            promptBox = CreateSmallTextBox();
            promptBox.Height = 64;
            promptBox.AcceptsReturn = true;
            promptBox.TextWrapping = TextWrapping.Wrap;
            promptBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            promptBox.Margin = new Thickness(0, 2, 0, 5);
            settingsPanel.Children.Add(promptBox);

            settingsPanel.Children.Add(CreateFieldLabel("云端声音 ID（预留）"));
            cloudVoiceIdBox = CreateSmallTextBox();
            cloudVoiceIdBox.IsEnabled = false;
            cloudVoiceIdBox.Margin = new Thickness(0, 2, 0, 5);
            settingsPanel.Children.Add(cloudVoiceIdBox);

            keyStatusText = new TextBlock();
            keyStatusText.FontSize = 11;
            keyStatusText.TextWrapping = TextWrapping.Wrap;
            keyStatusText.Margin = new Thickness(0, 4, 0, 2);
            settingsPanel.Children.Add(keyStatusText);

            voiceStatusText = new TextBlock();
            voiceStatusText.FontSize = 10.5;
            voiceStatusText.TextWrapping = TextWrapping.Wrap;
            voiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(88, 82, 74));
            voiceStatusText.Margin = new Thickness(0, 2, 0, 5);
            settingsPanel.Children.Add(voiceStatusText);

            WrapPanel settingsActions = new WrapPanel();
            testVoiceButton = CreateButton("测试中英声音", Color.FromRgb(80, 103, 92), Colors.White);
            testVoiceButton.Click += async delegate { await TestVoicesAsync(); };
            settingsActions.Children.Add(testVoiceButton);
            Button saveSettingsButton = CreateButton(
                "保存设置",
                Color.FromRgb(52, 77, 96),
                Colors.White);
            saveSettingsButton.Click += delegate { SaveSettingsFromUi(); };
            settingsActions.Children.Add(saveSettingsButton);
            Button soundSettingsButton = CreateButton(
                "Windows 声音设置",
                Color.FromRgb(91, 95, 100),
                Colors.White);
            soundSettingsButton.Click += delegate { OpenWindowsSoundSettings(); };
            settingsActions.Children.Add(soundSettingsButton);
            settingsPanel.Children.Add(settingsActions);
            content.Children.Add(settingsExpander);

            Content = outer;
            Loaded += delegate
            {
                PopulateSettings();
                RefreshCapabilityLabels();
                SetUiState(AssistantUiState.Ready, "待机");
                if (!chat.IsConfigured)
                {
                    AppendSystemMessage(chat.ConfigurationHint);
                }
            };
            ContentRendered += delegate
            {
                PositionNearPet();
                Opacity = 1;
            };
            recognition.RecognitionFinished += RecognitionFinishedAutomatically;
            recognition.RecognitionFailed += RecognitionFailedAutomatically;
            Closing += AssistantWindowClosing;
        }

        private Grid BuildHeader()
        {
            Grid header = new Grid();
            header.Background = new SolidColorBrush(Color.FromRgb(41, 52, 60));
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try
                    {
                        DragMove();
                    }
                    catch
                    {
                    }
                }
            };
            TextBlock title = new TextBlock();
            title.Text = "费曼 AI 对话";
            title.Foreground = Brushes.White;
            title.FontSize = 16;
            title.FontWeight = FontWeights.SemiBold;
            title.VerticalAlignment = VerticalAlignment.Center;
            title.Margin = new Thickness(15, 0, 0, 0);
            header.Children.Add(title);

            TextBlock subtitle = new TextBlock();
            subtitle.Text = "双语语音 · DeepSeek · 本地 TTS";
            subtitle.Foreground = new SolidColorBrush(Color.FromRgb(191, 205, 204));
            subtitle.FontSize = 10.5;
            subtitle.HorizontalAlignment = HorizontalAlignment.Right;
            subtitle.VerticalAlignment = VerticalAlignment.Center;
            subtitle.Margin = new Thickness(0, 0, 46, 0);
            header.Children.Add(subtitle);

            Button close = new Button();
            close.Content = "×";
            close.Width = 30;
            close.Height = 28;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Center;
            close.Margin = new Thickness(0, 0, 10, 0);
            close.Foreground = Brushes.White;
            close.Background = Brushes.Transparent;
            close.BorderThickness = new Thickness(0);
            close.FontSize = 20;
            close.Click += delegate { Close(); };
            header.Children.Add(close);
            return header;
        }

        private static TextBox CreateSmallTextBox()
        {
            TextBox box = new TextBox();
            box.Background = new SolidColorBrush(Color.FromRgb(255, 251, 241));
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 177, 161));
            box.Padding = new Thickness(6, 4, 6, 4);
            box.FontSize = 12;
            return box;
        }

        private static TextBlock CreateFieldLabel(string text)
        {
            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontSize = 11.5;
            label.Foreground = new SolidColorBrush(Color.FromRgb(78, 70, 61));
            label.VerticalAlignment = VerticalAlignment.Center;
            return label;
        }

        private static Button CreateButton(string text, Color background, Color foreground)
        {
            Button button = new Button();
            button.Content = text;
            button.Margin = new Thickness(0, 0, 5, 5);
            button.Padding = new Thickness(10, 5, 10, 6);
            button.Background = new SolidColorBrush(background);
            button.Foreground = new SolidColorBrush(foreground);
            button.BorderThickness = new Thickness(0);
            button.FontSize = 11.5;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static ComboBoxItem MakeComboItem(string text, string tag)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = text;
            item.Tag = tag;
            return item;
        }

        public void OpenAndStartRecording()
        {
            if (!IsVisible)
            {
                Show();
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(delegate
                    {
                        PositionNearPet();
                        StartRecordingAndReport();
                    }));
            }
            else
            {
                Activate();
                if (!recognition.IsListening)
                {
                    StartRecordingAndReport();
                }
            }
        }

        public void OpenOnly()
        {
            if (!IsVisible)
            {
                Show();
            }
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(PositionNearPet));
            Activate();
        }

        public void SetTopmost(bool topmost)
        {
            Topmost = topmost;
        }

        public void SuspendForHide()
        {
            Interlocked.Increment(ref operationVersion);
            if (chatCancellation != null)
            {
                chatCancellation.Cancel();
            }
            if (speechCancellation != null)
            {
                speechCancellation.Cancel();
            }
            recognition.Cancel();
            isSending = false;
            isRecognizing = false;
            isSpeaking = false;
            windowsTts.StopSpeaking();
            eSpeakTts.StopSpeaking();
            cloudTts.StopSpeaking();
            SetUiState(AssistantUiState.Cancelled, "窗口隐藏，任务已取消");
        }

        public async void ToggleRecordingFromShortcut()
        {
            if (!IsVisible)
            {
                OpenAndStartRecording();
                return;
            }
            Activate();
            await ToggleRecordingAsync();
        }

        private async Task ToggleRecordingAsync()
        {
            if (recognition.IsListening)
            {
                await StopRecordingAndRecognizeAsync();
            }
            else
            {
                StartRecordingAndReport();
            }
        }

        private void StartRecordingAndReport()
        {
            if (isSending || isRecognizing)
            {
                return;
            }
            StopSpeech();
            SpeechRecognitionStartResult result = recognition.Start(settings.RecognitionLanguage);
            if (!result.Success)
            {
                transcriptBox.Text = "";
                SetUiState(AssistantUiState.Error, "无法开始录音");
                AppendSystemMessage(result.Message);
                return;
            }
            if (!String.IsNullOrEmpty(result.Message))
            {
                AppendSystemMessage(result.Message);
            }
            SetUiState(
                AssistantUiState.Listening,
                String.IsNullOrEmpty(result.CultureName)
                    ? "正在聆听"
                    : "正在聆听（" + result.CultureName + "）");
            pet.SetMicButtonMode(MicButtonMode.Recording);
        }

        private async Task StopRecordingAndRecognizeAsync()
        {
            isRecognizing = true;
            SetUiState(AssistantUiState.Recognizing, "正在识别");
            pet.SetMicButtonMode(MicButtonMode.Busy);
            try
            {
                string recognized = await recognition.StopAsync(CancellationToken.None);
                isRecognizing = false;
                if (String.IsNullOrWhiteSpace(recognized))
                {
                    transcriptBox.Text = "";
                    SetUiState(
                        AssistantUiState.Error,
                        "没有检测到语音，请检查麦克风后重新录音。");
                    return;
                }
                transcriptBox.Text = recognized;
                transcriptBox.Focus();
                transcriptBox.CaretIndex = transcriptBox.Text.Length;
                SetUiState(AssistantUiState.Ready, "识别完成，可编辑后提问");
                pet.ShowAssistantBubble("你：" + recognized, 5200);
            }
            catch (OperationCanceledException)
            {
                isRecognizing = false;
                SetUiState(AssistantUiState.Cancelled, "录音已取消");
            }
            catch (Exception exception)
            {
                isRecognizing = false;
                SetUiState(AssistantUiState.Error, "语音识别失败");
                AppendSystemMessage(exception.Message);
            }
            finally
            {
                isRecognizing = false;
                pet.SetMicButtonMode(MicButtonMode.Idle);
                UpdateControls();
            }
        }

        private void CancelRecording()
        {
            if (!recognition.IsListening && !isRecognizing)
            {
                return;
            }
            recognition.Cancel();
            isRecognizing = false;
            transcriptBox.Text = "";
            SetUiState(AssistantUiState.Cancelled, "录音已取消");
            pet.SetMicButtonMode(MicButtonMode.Idle);
        }

        private void RecognitionFinishedAutomatically(
            object sender,
            SpeechRecognitionResultEventArgs e)
        {
            if (closing)
            {
                return;
            }
            Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(delegate
                {
                    if (closing || isRecognizing)
                    {
                        return;
                    }
                    if (String.IsNullOrWhiteSpace(e.Text))
                    {
                        SetUiState(
                            AssistantUiState.Error,
                            "没有检测到语音，请检查麦克风后重新录音。");
                        pet.SetMicButtonMode(MicButtonMode.Idle);
                        return;
                    }
                    transcriptBox.Text = e.Text;
                    SetUiState(AssistantUiState.Ready, "识别完成，可编辑后提问");
                    pet.ShowAssistantBubble("你：" + e.Text, 5200);
                }));
        }

        private void RecognitionFailedAutomatically(
            object sender,
            SpeechRecognitionErrorEventArgs e)
        {
            if (closing)
            {
                return;
            }
            Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(delegate
                {
                    if (closing || isRecognizing)
                    {
                        return;
                    }
                    SetUiState(AssistantUiState.Error, "语音识别失败");
                    AppendSystemMessage(e.Message);
                    pet.SetMicButtonMode(MicButtonMode.Idle);
                }));
        }

        private async Task RestartRecordingAsync()
        {
            CancelCurrentRequest();
            StopSpeech();
            if (recognition.IsListening || isRecognizing)
            {
                recognition.Cancel();
            }
            isRecognizing = false;
            transcriptBox.Text = "";
            StartRecordingAndReport();
            await Task.FromResult(true);
        }

        private async Task SendQuestionAsync()
        {
            if (isSending)
            {
                return;
            }
            ApplyLanguageAndVoiceSettingsFromUi();
            string question = (transcriptBox.Text ?? "").Trim();
            if (question.Length == 0)
            {
                SetUiState(AssistantUiState.Error, "请先录音识别，或输入问题");
                AppendSystemMessage("识别文本为空，无法提交。");
                return;
            }
            if (!chat.IsConfigured)
            {
                SetUiState(AssistantUiState.Error, "缺少 DeepSeek API Key");
                AppendSystemMessage(chat.ConfigurationHint);
                return;
            }

            if (recognition.IsListening || isRecognizing)
            {
                recognition.Cancel();
                isRecognizing = false;
            }
            StopSpeech();
            CancelCurrentRequest();
            int sequence = Interlocked.Increment(ref operationVersion);
            CancellationTokenSource cancellation = new CancellationTokenSource();
            chatCancellation = cancellation;
            isSending = true;
            AppendConversation("你", question);
            SetUiState(AssistantUiState.Thinking, "正在思考");
            UpdateControls();
            try
            {
                string answerLanguage = LanguageTools.Resolve(
                    settings.OutputLanguage,
                    question);
                RuntimeLog.Write(
                    "AnswerLanguage=" + answerLanguage +
                    " configured=" + settings.OutputLanguage);
                string answer = await chat.AskAsync(
                    question,
                    settings,
                    answerLanguage,
                    cancellation.Token);
                if (sequence != operationVersion || closing)
                {
                    return;
                }
                AppendConversation("费曼", answer);
                if (settings.AutoSpeak)
                {
                    await SpeakTextAsync(answer, sequence);
                    if (sequence == operationVersion && !closing)
                    {
                        pet.ShowAssistantBubble("费曼：" + answer, 7000);
                    }
                }
                else if (sequence == operationVersion)
                {
                    SetUiState(AssistantUiState.Ready, "回答完成");
                    pet.ShowAssistantBubble("费曼：" + answer, 7000);
                }
            }
            catch (OperationCanceledException)
            {
                if (sequence == operationVersion)
                {
                    SetUiState(AssistantUiState.Cancelled, "已取消等待");
                    AppendSystemMessage("已取消当前回答。");
                }
            }
            catch (ChatServiceException exception)
            {
                if (sequence == operationVersion)
                {
                    SetUiState(AssistantUiState.Error, "AI 请求失败");
                    AppendSystemMessage(exception.Message);
                }
            }
            catch (Exception exception)
            {
                if (sequence == operationVersion)
                {
                    SetUiState(AssistantUiState.Error, "AI 请求失败");
                    AppendSystemMessage("请求失败: " + exception.Message);
                }
            }
            finally
            {
                if (sequence == operationVersion)
                {
                    isSending = false;
                    chatCancellation = null;
                    UpdateControls();
                }
            }
        }

        private async Task SpeakTextAsync(string text, int sequence)
        {
            TtsPreparedText prepared = TtsTextPreprocessor.Prepare(text, "auto");
            RuntimeLog.Write(
                "TTS text prepared. language=" + prepared.Language +
                " length=" + prepared.Text.Length +
                " removedCode=" + prepared.RemovedCode);
            if (prepared.WasEmpty)
            {
                SetUiState(AssistantUiState.Ready, "回答中没有适合朗读的文本");
                return;
            }
            IVoiceOutputService voice = GetSelectedVoiceService();
            if (!voice.IsAvailable)
            {
                SetUiState(AssistantUiState.Error, "语音服务不可用");
                AppendSystemMessage(voice.AvailabilityMessage);
                return;
            }
            CancellationTokenSource cancellation = new CancellationTokenSource();
            speechCancellation = cancellation;
            isSpeaking = true;
            SetUiState(AssistantUiState.Speaking, "正在朗读");
            UpdateControls();
            try
            {
                await voice.SpeakAsync(
                    prepared.Text,
                    prepared.Language,
                    settings.TtsRate,
                    prepared.Language == "zh"
                        ? settings.ChineseVoiceName
                        : settings.EnglishVoiceName,
                    cancellation.Token);
                if (sequence == operationVersion && !closing)
                {
                    SetUiState(AssistantUiState.Ready, "播报完成，等待下一问");
                }
            }
            catch (OperationCanceledException)
            {
                if (sequence == operationVersion && !closing)
                {
                    SetUiState(AssistantUiState.Cancelled, "播报已停止");
                }
            }
            catch (Exception exception)
            {
                if (sequence == operationVersion && !closing)
                {
                    SetUiState(
                        AssistantUiState.Error,
                        "播报失败：请检查 Windows 默认扬声器");
                    AppendSystemMessage(
                        "语音没有通过默认播放设备启动。请打开“设置 → 系统 → 声音”，" +
                        "确认扬声器已设为默认、没有被其他应用独占，然后重新测试。\n" +
                        exception.Message);
                }
            }
            finally
            {
                isSpeaking = false;
                speechCancellation = null;
                UpdateControls();
            }
        }

        private async Task TestVoicesAsync()
        {
            StopSpeech();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            speechCancellation = cancellation;
            isSpeaking = true;
            SetUiState(AssistantUiState.Speaking, "测试中英语音");
            UpdateControls();
            try
            {
                IVoiceOutputService provider = GetSelectedVoiceService();
                await provider.SpeakAsync(
                    "你好，保持好奇。",
                    "zh",
                    (int)Math.Round(rateSlider.Value),
                    SelectedVoiceName(chineseVoiceCombo),
                    cancellation.Token);
                await provider.SpeakAsync(
                    "Curiosity is the beginning of understanding.",
                    "en",
                    (int)Math.Round(rateSlider.Value),
                    SelectedVoiceName(englishVoiceCombo),
                    cancellation.Token);
                SetUiState(AssistantUiState.Ready, "声音测试完成");
            }
            catch (OperationCanceledException)
            {
                SetUiState(AssistantUiState.Cancelled, "声音测试已停止");
            }
            catch (Exception exception)
            {
                SetUiState(AssistantUiState.Error, "声音测试失败");
                AppendSystemMessage(exception.Message);
            }
            finally
            {
                isSpeaking = false;
                speechCancellation = null;
                UpdateControls();
            }
        }

        private void StopSpeech()
        {
            if (speechCancellation != null)
            {
                speechCancellation.Cancel();
            }
            windowsTts.StopSpeaking();
            eSpeakTts.StopSpeaking();
            cloudTts.StopSpeaking();
            isSpeaking = false;
            if (!closing && state == AssistantUiState.Speaking)
            {
                SetUiState(AssistantUiState.Cancelled, "播报已停止");
            }
            UpdateControls();
        }

        private void CancelCurrentRequest()
        {
            Interlocked.Increment(ref operationVersion);
            if (chatCancellation != null)
            {
                chatCancellation.Cancel();
            }
            if (speechCancellation != null)
            {
                speechCancellation.Cancel();
            }
            windowsTts.StopSpeaking();
            eSpeakTts.StopSpeaking();
            cloudTts.StopSpeaking();
            isSending = false;
            isSpeaking = false;
            if (!closing)
            {
                SetUiState(AssistantUiState.Cancelled, "任务已取消");
            }
            UpdateControls();
        }

        private void ClearConversation()
        {
            CancelCurrentRequest();
            chat.ClearHistory();
            conversationBox.Text = "对话记录已清除。";
            transcriptBox.Text = "";
            SetUiState(AssistantUiState.Ready, "待机");
        }

        private void SaveSettingsFromUi()
        {
            ComboBoxItem languageItem = languageCombo.SelectedItem as ComboBoxItem;
            settings.RecognitionLanguage = languageItem == null
                ? "auto"
                : (string)languageItem.Tag;
            ComboBoxItem answerLanguageItem =
                answerLanguageCombo.SelectedItem as ComboBoxItem;
            settings.OutputLanguage = answerLanguageItem == null
                ? "auto"
                : (string)answerLanguageItem.Tag;
            ComboBoxItem serviceItem = serviceCombo.SelectedItem as ComboBoxItem;
            settings.TtsService = serviceItem == null
                ? "windows"
                : (string)serviceItem.Tag;
            settings.ChineseVoiceName = SelectedVoiceName(chineseVoiceCombo);
            settings.EnglishVoiceName = SelectedVoiceName(englishVoiceCombo);
            settings.TtsRate = (int)Math.Round(rateSlider.Value);
            settings.AutoSpeak = autoSpeakCheck.IsChecked == true;
            settings.ShowMicButton = showMicCheck.IsChecked == true;
            settings.DeepSeekModel = String.IsNullOrWhiteSpace(modelBox.Text)
                ? "deepseek-chat"
                : modelBox.Text.Trim();
            settings.SystemPrompt = String.IsNullOrWhiteSpace(promptBox.Text)
                ? AssistantSettings.DefaultPrompt
                : promptBox.Text.Trim();
            settings.CloudVoiceId = cloudVoiceIdBox.Text.Trim();
            if (persistSettings)
            {
                settings.Save(projectRoot);
            }
            pet.ApplyAssistantSettings(settings);
            RefreshCapabilityLabels();
            SetUiState(AssistantUiState.Ready, "设置已保存");
        }

        private void ApplyLanguageAndVoiceSettingsFromUi()
        {
            ComboBoxItem recognitionItem =
                languageCombo.SelectedItem as ComboBoxItem;
            settings.RecognitionLanguage = recognitionItem == null
                ? "auto"
                : (string)recognitionItem.Tag;
            ComboBoxItem answerItem =
                answerLanguageCombo.SelectedItem as ComboBoxItem;
            settings.OutputLanguage = answerItem == null
                ? "auto"
                : (string)answerItem.Tag;
            ComboBoxItem serviceItem = serviceCombo.SelectedItem as ComboBoxItem;
            settings.TtsService = serviceItem == null
                ? "windows"
                : (string)serviceItem.Tag;
            settings.ChineseVoiceName = SelectedVoiceName(chineseVoiceCombo);
            settings.EnglishVoiceName = SelectedVoiceName(englishVoiceCombo);
        }

        private void OpenWindowsSoundSettings()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("ms-settings:sound");
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception exception)
            {
                AppendSystemMessage("无法打开 Windows 声音设置: " + exception.Message);
            }
        }

        private void PopulateSettings()
        {
            SelectComboByTag(languageCombo, settings.RecognitionLanguage);
            SelectComboByTag(answerLanguageCombo, settings.OutputLanguage);
            string serviceTag =
                settings.TtsService == "espeak" && eSpeakTts.IsAvailable
                    ? "espeak"
                    : "windows";
            SelectComboByTag(serviceCombo, serviceTag);
            RefreshVoiceChoices();
            rateSlider.Value = Math.Max(-4, Math.Min(4, settings.TtsRate));
            autoSpeakCheck.IsChecked = settings.AutoSpeak;
            showMicCheck.IsChecked = settings.ShowMicButton;
            modelBox.Text = settings.DeepSeekModel;
            promptBox.Text = settings.SystemPrompt;
            cloudVoiceIdBox.Text = settings.CloudVoiceId;
        }

        private void RefreshVoiceChoices()
        {
            chineseVoiceCombo.Items.Clear();
            englishVoiceCombo.Items.Clear();
            IVoiceOutputService provider = GetSelectedVoiceService();
            foreach (VoiceDescriptor voice in provider.GetVoices())
            {
                ComboBoxItem item = MakeComboItem(
                    voice.Name + " · " + voice.CultureName + " · " + voice.Gender,
                    voice.Name);
                if (voice.CultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    chineseVoiceCombo.Items.Add(item);
                }
                else if (voice.CultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                {
                    englishVoiceCombo.Items.Add(item);
                }
            }
            if (chineseVoiceCombo.Items.Count == 0)
            {
                chineseVoiceCombo.Items.Add(MakeComboItem("无可用中文声音", ""));
            }
            if (englishVoiceCombo.Items.Count == 0)
            {
                englishVoiceCombo.Items.Add(MakeComboItem("No English voice", ""));
            }
            SelectComboByTag(chineseVoiceCombo, settings.ChineseVoiceName ?? "");
            SelectComboByTag(englishVoiceCombo, settings.EnglishVoiceName ?? "");
        }

        private static string SelectedVoiceName(ComboBox combo)
        {
            ComboBoxItem item = combo.SelectedItem as ComboBoxItem;
            return item == null ? "" : (string)item.Tag;
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            foreach (object item in combo.Items)
            {
                ComboBoxItem comboItem = item as ComboBoxItem;
                if (comboItem != null &&
                    String.Equals((string)comboItem.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = comboItem;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private void RefreshCapabilityLabels()
        {
            keyStatusText.Text = chat.ConfigurationHint;
            keyStatusText.Foreground = chat.IsConfigured
                ? new SolidColorBrush(Color.FromRgb(45, 113, 74))
                : new SolidColorBrush(Color.FromRgb(154, 69, 54));

            StringBuilder capabilities = new StringBuilder();
            capabilities.Append("识别器: ");
            IList<RecognizerDescriptor> recognizers = recognition.GetInstalledRecognizers();
            if (recognizers.Count == 0)
            {
                capabilities.Append("未安装 Windows 语音识别语言包。");
            }
            else
            {
                for (int index = 0; index < recognizers.Count; index++)
                {
                    if (index > 0)
                    {
                        capabilities.Append(", ");
                    }
                    capabilities.Append(recognizers[index].CultureName);
                }
            }
            capabilities.AppendLine();
            capabilities.Append("识别语言状态: 中文=");
            capabilities.Append(recognition.SupportsLanguage("zh") ? "可用" : "未安装");
            capabilities.Append("，English=");
            capabilities.Append(recognition.SupportsLanguage("en") ? "可用" : "未安装");
            capabilities.AppendLine();
            capabilities.Append(windowsTts.AvailabilityMessage);
            capabilities.AppendLine();
            capabilities.Append(eSpeakTts.AvailabilityMessage);
            capabilities.AppendLine();
            capabilities.Append(cloudTts.AvailabilityMessage);
            voiceStatusText.Text = capabilities.ToString();
        }

        private IVoiceOutputService GetSelectedVoiceService()
        {
            ComboBoxItem item = serviceCombo.SelectedItem as ComboBoxItem;
            string provider = item == null ? "windows" : (string)item.Tag;
            if (provider == "espeak" && eSpeakTts.IsAvailable)
            {
                return eSpeakTts;
            }
            if (provider == "cloud")
            {
                return cloudTts;
            }
            return windowsTts;
        }

        private void SetUiState(AssistantUiState newState, string message)
        {
            state = newState;
            string label = StateLabel(newState);
            string detail = String.Equals(message, label, StringComparison.Ordinal)
                ? ""
                : message;
            statusText.Text = "● " + label +
                (String.IsNullOrEmpty(detail) ? "" : "  " + detail);
            if (newState == AssistantUiState.Error)
            {
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(166, 64, 53));
            }
            else if (newState == AssistantUiState.Cancelled)
            {
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(154, 108, 51));
            }
            else if (newState == AssistantUiState.Listening ||
                newState == AssistantUiState.Speaking)
            {
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(51, 94, 110));
            }
            else
            {
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(52, 94, 78));
            }
            pet.ApplyAssistantUiState(newState, message);
            pet.SetMicButtonMode(
                newState == AssistantUiState.Listening
                    ? MicButtonMode.Recording
                    : (newState == AssistantUiState.Recognizing ||
                        newState == AssistantUiState.Thinking ||
                        newState == AssistantUiState.Speaking
                        ? MicButtonMode.Busy
                        : MicButtonMode.Idle));
            UpdateControls();
        }

        private static string StateLabel(AssistantUiState value)
        {
            if (value == AssistantUiState.Listening) return "正在聆听";
            if (value == AssistantUiState.Recognizing) return "正在识别";
            if (value == AssistantUiState.Thinking) return "正在思考";
            if (value == AssistantUiState.Speaking) return "正在说话";
            if (value == AssistantUiState.Cancelled) return "已取消";
            if (value == AssistantUiState.Error) return "出错";
            return "待机";
        }

        private void UpdateControls()
        {
            micButton.Content = recognition.IsListening ? "结束并识别" : "开始说话";
            micButton.IsEnabled = !isRecognizing && !isSending;
            cancelRecordingButton.IsEnabled = recognition.IsListening || isRecognizing;
            reRecordButton.IsEnabled = !isSending;
            sendButton.IsEnabled = !isSending && !isRecognizing;
            cancelRequestButton.IsEnabled = isSending || isSpeaking;
            stopSpeechButton.IsEnabled = isSpeaking;
            clearButton.IsEnabled = !isSending;
            testVoiceButton.IsEnabled = !isSending && !isSpeaking;
        }

        private void AppendConversation(string speaker, string text)
        {
            StringBuilder updated = new StringBuilder(conversationBox.Text);
            if (updated.Length > 0)
            {
                updated.AppendLine();
            }
            updated.Append(speaker);
            updated.AppendLine("：");
            updated.AppendLine(text);
            conversationBox.Text = updated.ToString();
            conversationBox.ScrollToEnd();
        }

        private void AppendSystemMessage(string text)
        {
            string current = conversationBox.Text;
            conversationBox.Text = current +
                (current.Length > 0 ? Environment.NewLine : "") +
                "系统：" + Environment.NewLine + text;
            conversationBox.ScrollToEnd();
        }

        private void PositionNearPet()
        {
            UpdateLayout();
            double height = Math.Max(ActualHeight, 1);
            double width = Math.Max(ActualWidth, 1);
            Rect work = SystemParameters.WorkArea;
            double left = pet.Left - width - 10;
            if (left < work.Left)
            {
                left = pet.Left + pet.Width + 10;
            }
            double top = pet.Top + pet.Height - Math.Min(height, 560);
            Left = Math.Max(work.Left, Math.Min(work.Right - width, left));
            Top = Math.Max(work.Top, Math.Min(work.Bottom - height, top));
        }

        private void AssistantWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            closing = true;
            Interlocked.Increment(ref operationVersion);
            if (chatCancellation != null)
            {
                chatCancellation.Cancel();
            }
            if (speechCancellation != null)
            {
                speechCancellation.Cancel();
            }
            recognition.Dispose();
            windowsTts.Dispose();
            eSpeakTts.Dispose();
            cloudTts.Dispose();
            chat.Dispose();
            if (persistSettings)
            {
                settings.Save(projectRoot);
            }
            pet.OnAssistantClosed();
        }
    }
}
