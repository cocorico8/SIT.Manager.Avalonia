﻿using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using SIT.Manager.Interfaces;
using SIT.Manager.Models.Config;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace SIT.Manager.ViewModels.Settings;

public partial class LauncherViewModel(IManagerConfigService configService,
                         ILocalizationService localizationService,
                         IModService modService,
                         IPickerDialogService pickerDialogService) : SettingsViewModelBase(configService, pickerDialogService)
{
    private readonly ILocalizationService _localizationService = localizationService;
    private readonly IModService _modService = modService;
    private LauncherConfig _launcherSettings => _configsService.Config.LauncherSettings;

    private readonly FluentAvaloniaTheme? faTheme = Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();

    [ObservableProperty]
    private bool _isTestModeEnabled = false;

    [ObservableProperty]
    private List<CultureInfo> _availableLocalizations = localizationService.GetAvailableLocalizations();

    [ObservableProperty]
    private CultureInfo _currentLocalization = localizationService.DefaultLocale;

    //TODO: Change this to work with new config!!
    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_launcherSettings.AccentColor))
        {
            if (faTheme != null && faTheme.CustomAccentColor != _launcherSettings.AccentColor)
            {
                faTheme.CustomAccentColor = _launcherSettings.AccentColor;
            }
        }
    }

    protected override void OnActivated()
    {
        base.OnActivated();

        CurrentLocalization = AvailableLocalizations.FirstOrDefault(x => x.Name == _launcherSettings.CurrentLanguageSelected, _localizationService.DefaultLocale);
        IsTestModeEnabled = _launcherSettings.EnableTestMode;
    }

    protected override void OnDeactivated()
    {
        base.OnDeactivated();
    }

    partial void OnCurrentLocalizationChanged(CultureInfo value)
    {
        if (value != null)
        {
            _localizationService.Translate(value);
        }
    }

    async partial void OnIsTestModeEnabledChanged(bool value)
    {
        // If test mode is enabled then we want to check that there's no mods we don't approve of currently installed.
        if (value)
        {
            List<string> installedMods = _configsService.Config.InstalledMods.Keys.ToList();
            int compatibleModCount = installedMods.Count(x => _modService.RecommendedModInstalls.Contains(x));
            int totalIncompatibleMods = installedMods.Count - compatibleModCount;
            if (totalIncompatibleMods > 0)
            {
                await new ContentDialog()
                {
                    Title = _localizationService.TranslateSource("SettingsPageViewModelEnableDevModeErrorTitle"),
                    Content = _localizationService.TranslateSource("SettingsPageViewModelEnableDevModeErrorDescription", totalIncompatibleMods.ToString()),
                    CloseButtonText = _localizationService.TranslateSource("SettingsPageViewModelEnableDevModeErrorButtonOk")
                }.ShowAsync();
            }
        }
        
        _launcherSettings.EnableTestMode = value;
    }
}
