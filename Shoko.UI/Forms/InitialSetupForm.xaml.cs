﻿using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Shoko.Server;
using Shoko.Server.Settings;

namespace Shoko.UI.Forms
{
    /// <summary>
    /// Interaction logic for InitialSetupForm.xaml
    /// </summary>
    public partial class InitialSetupForm : Window
    {
        private static BackgroundWorker workerTestLogin = new BackgroundWorker();

        public InitialSetupForm()
        {
            InitializeComponent();

            txtUsername.TextChanged += txtUsername_TextChanged;
            txtPassword.PasswordChanged += txtPassword_PasswordChanged;
            txtClientPort.TextChanged += txtClientPort_TextChanged;

            btnTestConnection.Click += btnTestConnection_Click;
            btnClose.Click += btnClose_Click;

            workerTestLogin.DoWork += workerTestLogin_DoWork;
            workerTestLogin.ProgressChanged += workerTestLogin_ProgressChanged;
            workerTestLogin.RunWorkerCompleted +=
                workerTestLogin_RunWorkerCompleted;
            workerTestLogin.WorkerReportsProgress = true;
            workerTestLogin.WorkerSupportsCancellation = true;

            Loaded += InitialSetupForm_Loaded;
        }

        void InitialSetupForm_Loaded(object sender, RoutedEventArgs e)
        {
            txtUsername.Focus();
        }

        void workerTestLogin_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            btnTestConnection.IsEnabled = true;
        }

        void workerTestLogin_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ServerState.Instance.AniDB_TestStatus = e.UserState.ToString();
        }

        void workerTestLogin_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                // TODO Will need ported to the new system
                workerTestLogin.ReportProgress(0, Commons.Properties.Resources.InitialSetup_Disposing);
                ShokoService.AnidbProcessor.ForceLogout();
                ShokoService.AnidbProcessor.CloseConnections();
                Thread.Sleep(1000);

                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Instance.Culture);

                workerTestLogin.ReportProgress(0, Commons.Properties.Resources.Server_Initializing);
                ShokoService.AnidbProcessor.Init(ServerSettings.Instance.AniDb.Username,
                    ServerSettings.Instance.AniDb.Password, ServerSettings.Instance.AniDb.ServerAddress,
                    ServerSettings.Instance.AniDb.ServerPort, ServerSettings.Instance.AniDb.ClientPort);

                workerTestLogin.ReportProgress(0, Commons.Properties.Resources.InitialSetup_Login);
                if (ShokoService.AnidbProcessor.Login())
                {
                    workerTestLogin.ReportProgress(0, Commons.Properties.Resources.InitialSetup_LoginPass1);
                    ShokoService.AnidbProcessor.ForceLogout();
                    workerTestLogin.ReportProgress(0, Commons.Properties.Resources.InitialSetup_LoginPass2);
                }
                else
                {
                    workerTestLogin.ReportProgress(0, Commons.Properties.Resources.InitialSetup_LoginFail);
                }
            }
            catch (Exception ex)
            {
                workerTestLogin.ReportProgress(0, ex.Message);
            }
        }

        void btnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            // Check if already running and cancel if needed
            if (workerTestLogin.IsBusy)
                workerTestLogin.CancelAsync();

            // If still running cancel action entirely
            if (workerTestLogin.IsBusy)
                return;

            if (txtUsername.Text.Trim().Length == 0)
            {
                MessageBox.Show(Commons.Properties.Resources.InitialSetup_EnterUsername,
                    Commons.Properties.Resources.Error,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                txtUsername.Focus();
                return;
            }

            if (txtPassword.Password.Trim().Length == 0)
            {
                MessageBox.Show(Commons.Properties.Resources.InitialSetup_EnterPassword,
                    Commons.Properties.Resources.Error,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                txtPassword.Focus();
                return;
            }

            if (txtClientPort.Text.Trim().Length == 0)
            {
                MessageBox.Show(Commons.Properties.Resources.InitialSetup_EnterPort,
                    Commons.Properties.Resources.Error,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                txtClientPort.Focus();
                return;
            }

            btnTestConnection.IsEnabled = false;
            workerTestLogin.RunWorkerAsync();
        }

        void txtClientPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                ServerSettings.Instance.AniDb.ClientPort = ushort.Parse(txtClientPort.Text.Trim());
            }
            catch
            {
            }
        }

        void txtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ServerSettings.Instance.AniDb.Password = txtPassword.Password;
        }

        void txtUsername_TextChanged(object sender, TextChangedEventArgs e)
        {
            ServerSettings.Instance.AniDb.Username = txtUsername.Text.Trim();
        }

        void btnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            MainWindow.AniDBLoginOpen = false;
            Close();
        }
    }
}