// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using NuGet.PackageManagement.VisualStudio;

namespace NuGet.Options
{
    public partial class GeneralOptionControl : UserControl
    {
        private readonly Configuration.ISettings _settings;
        private bool _initialized;

        private const string SettingsStorePath = @"UserSettings\NuGet";
        private const string ExperimentalFeaturesPropertyName = "ExperimentalFeatures";

        public GeneralOptionControl()
        {
            InitializeComponent();

            _settings = ServiceLocator.GetInstance<Configuration.ISettings>();
            Debug.Assert(_settings != null);
        }

        internal void OnActivated()
        {
            if (!_initialized)
            {
                try
                {
                    // not using the nuget.core version of PackageRestoreConsent
                    var packageRestoreConsent = new PackageManagement.VisualStudio.PackageRestoreConsent(_settings);

                    packageRestoreConsentCheckBox.Checked = packageRestoreConsent.IsGrantedInSettings;
                    packageRestoreAutomaticCheckBox.Checked = packageRestoreConsent.IsAutomatic;
                    packageRestoreAutomaticCheckBox.Enabled = packageRestoreConsentCheckBox.Checked;

                    var bindingRedirects = new BindingRedirectBehavior(_settings);
                    skipBindingRedirects.Checked = bindingRedirects.IsSkipped;

                    enableExperimentalFeaturesCheckBox.Checked = ExperimentalFeaturesEnabled;
                }
                catch(InvalidOperationException)
                {
                    MessageHelper.ShowErrorMessage(Resources.ShowError_ConfigInvalidOperation, Resources.ErrorDialogBoxTitle);
                }
                catch (UnauthorizedAccessException)
                {
                    MessageHelper.ShowErrorMessage(Resources.ShowError_ConfigUnauthorizedAccess, Resources.ErrorDialogBoxTitle);
                }
            }

            _initialized = true;
        }

        internal bool OnApply()
        {
            try
            {
                var packageRestoreConsent = new PackageManagement.VisualStudio.PackageRestoreConsent(_settings);
                packageRestoreConsent.IsGrantedInSettings = packageRestoreConsentCheckBox.Checked;
                packageRestoreConsent.IsAutomatic = packageRestoreAutomaticCheckBox.Checked;

                var bindingRedirects = new BindingRedirectBehavior(_settings);
                bindingRedirects.IsSkipped = skipBindingRedirects.Checked;

                ExperimentalFeaturesEnabled = enableExperimentalFeaturesCheckBox.Checked;
            }
            catch (InvalidOperationException)
            {
                MessageHelper.ShowErrorMessage(Resources.ShowError_ConfigInvalidOperation, Resources.ErrorDialogBoxTitle);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                MessageHelper.ShowErrorMessage(Resources.ShowError_ConfigUnauthorizedAccess, Resources.ErrorDialogBoxTitle);
                return false;
            }

            return true;
        }

        internal void OnClosed()
        {
            _initialized = false;
        }

        private void OnClearPackageCacheClick(object sender, EventArgs e)
        {
            //not implement now
        }

        private void OnBrowsePackageCacheClick(object sender, EventArgs e)
        {
            //not impement now
        }

        private void packageRestoreConsentCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            packageRestoreAutomaticCheckBox.Enabled = packageRestoreConsentCheckBox.Checked;
            if (!packageRestoreConsentCheckBox.Checked)
            {
                packageRestoreAutomaticCheckBox.Checked = false;
            }
        }

        private static bool ExperimentalFeaturesEnabled
        {
            get
            {
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var settingsStore = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);
                EnsureNuGetSettingsCollectionExists();
                return settingsStore.GetBoolean(SettingsStorePath, ExperimentalFeaturesPropertyName);
            }
            set
            {
                // This is stored as a Visual Studio settings so we can use it in a UIContext rule.
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var settingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
                EnsureNuGetSettingsCollectionExists(settingsStore);
                settingsStore.SetBoolean(SettingsStorePath, ExperimentalFeaturesPropertyName, value);
            }
        }

        private static void EnsureNuGetSettingsCollectionExists(WritableSettingsStore settingsStore = null)
        {
            if (settingsStore == null)
            {
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                settingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
            }

            if (!settingsStore.CollectionExists(SettingsStorePath))
            {
                settingsStore.CreateCollection(SettingsStorePath);
            }

            if (!settingsStore.PropertyExists(SettingsStorePath, ExperimentalFeaturesPropertyName))
            {
                settingsStore.SetBoolean(SettingsStorePath, ExperimentalFeaturesPropertyName, false);
            }
        }
    }
}
