﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using SIT.Manager.Interfaces;
using SIT.Manager.Models.Config;
using System.IO;
using System.Threading.Tasks;

namespace SIT.Manager.ViewModels.Settings;

public partial class EftViewModel : SettingsViewModelBase
{
    private readonly IBarNotificationService _barNotificationService;
    private readonly IInstallerService _installerService;
    private readonly ILocalizationService _localizationService;
    private readonly IVersionService _versionService;
    private SITConfig _sitConfig => _configsService.Config.SITSettings;

    [ObservableProperty]
    private string _bsgEftInstallPath;

    [ObservableProperty]
    private string _sitEftInstallPath;

    public IAsyncRelayCommand ChangeInstallLocationCommand { get; }

    public EftViewModel(IBarNotificationService barNotificationService,
        IInstallerService installerService,
        IManagerConfigService configService,
        ILocalizationService localizationService,
        IPickerDialogService pickerDialogService,
        IVersionService versionService) : base(configService, pickerDialogService)
    {
        _barNotificationService = barNotificationService;
        _installerService = installerService;
        _localizationService = localizationService;
        _versionService = versionService;

        BsgEftInstallPath = Path.GetDirectoryName(_installerService.GetEFTInstallPath()) ?? _localizationService.TranslateSource("EftViewModelBsgEftInstallPathMissing");
        SitEftInstallPath = _sitConfig.SitEFTInstallPath;

        ChangeInstallLocationCommand = new AsyncRelayCommand(ChangeInstallLocation);
    }

    private async Task ChangeInstallLocation()
    {
        string targetPath = await GetPathLocation("EscapeFromTarkov.exe");
        if (!string.IsNullOrEmpty(targetPath))
        {
            if (targetPath == BsgEftInstallPath)
            {
                // Using the same location as the current BSG install and we don't want this the same as the SIT install.
                await new ContentDialog()
                {
                    Title = _localizationService.TranslateSource("ConfigureSitViewModelLocationSelectionErrorTitle"),
                    Content = _localizationService.TranslateSource("ConfigureSitViewModelLocationSelectionErrorDescription"),
                    PrimaryButtonText = _localizationService.TranslateSource("ConfigureSitViewModelLocationSelectionErrorOk")
                }.ShowAsync();
                return;
            }

            SitEftInstallPath = targetPath;

            _sitConfig.SitEFTInstallPath = targetPath;
            _sitConfig.SitTarkovVersion = _versionService.GetEFTVersion(targetPath);
            _sitConfig.SitVersion = _versionService.GetSITVersion(targetPath);

            _barNotificationService.ShowInformational(_localizationService.TranslateSource("SettingsPageViewModelConfigTitle"), _localizationService.TranslateSource("SettingsPageViewModelConfigInformationEFTDescription", targetPath));
        }
        else
        {
            _barNotificationService.ShowError(_localizationService.TranslateSource("SettingsPageViewModelErrorTitle"), _localizationService.TranslateSource("SettingsPageViewModelConfigErrorEFTDescription"));
        }
    }

    protected override void OnActivated()
    {
        base.OnActivated();
    }
}
