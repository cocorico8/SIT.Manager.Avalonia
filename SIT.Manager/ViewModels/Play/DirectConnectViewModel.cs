﻿using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SIT.Manager.Exceptions;
using SIT.Manager.Interfaces;
using SIT.Manager.Interfaces.ManagedProcesses;
using SIT.Manager.Models;
using SIT.Manager.Models.Aki;
using SIT.Manager.Models.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SIT.Manager.ViewModels.Play;

public partial class DirectConnectViewModel : ObservableRecipient
{
    private const string SIT_DLL_FILENAME = "StayInTarkov.dll";
    private const string EFT_EXE_FILENAME = "EscapeFromTarkov.exe";

    private readonly IAkiServerService _akiServerService;
    private readonly IAkiServerRequestingService _serverRequestingService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<DirectConnectViewModel> _logger;
    private readonly IManagerConfigService _configService;
    private readonly ITarkovClientService _tarkovClientService;
    private SITConfig _sitConfig => _configService.Config.SITSettings;

    [ObservableProperty]
    private string _lastServer = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _rememberMe = false;

    [ObservableProperty]
    private ManagerConfig _managerConfig;

    [ObservableProperty]
    private string _quickPlayText = string.Empty;

    [ObservableProperty]
    private bool _isLocalServerRunning = false;

    public IAsyncRelayCommand ConnectToServerCommand { get; }
    public IAsyncRelayCommand QuickPlayCommand { get; }

    public DirectConnectViewModel(
        IAkiServerRequestingService serverRequestingService,
        IManagerConfigService configService,
        ITarkovClientService tarkovClientService,
        IAkiServerService akiServerService,
        ILocalizationService localizationService,
        ILogger<DirectConnectViewModel> logger)
    {
        _serverRequestingService = serverRequestingService;
        _configService = configService;
        _managerConfig = _configService.Config;
        _tarkovClientService = tarkovClientService;
        _akiServerService = akiServerService;
        _localizationService = localizationService;
        _managerConfig = configService.Config;
        _logger = logger;

        AkiServer? lastAkiServer = _configService.Config.SITSettings.LastServer;
        if (lastAkiServer != null)
        {
            _lastServer = lastAkiServer.Address.AbsoluteUri;
            if (lastAkiServer.Characters.Count != 0)
            {
                AkiCharacter savedCharacter = lastAkiServer.Characters.First();
                _username = savedCharacter.Username;
                _password = savedCharacter.Password;
                _rememberMe = true;
            }
        }

        ConnectToServerCommand = new AsyncRelayCommand(async () => await ConnectToServer());
        QuickPlayCommand = new AsyncRelayCommand(async () => await ConnectToServer(true));
    }

    public Task ConnectToServer(string address, string username, string password)
    {
        if (!string.IsNullOrEmpty(address))
        {
            LastServer = address;
        }
        else
        {
            LastServer = "127.0.0.1";
        }

        if (!string.IsNullOrEmpty(username))
        {
            Username = username;
        }

        if (!string.IsNullOrEmpty(password))
        {
            Password = password;
        }

        var startServer = address == null;

        return ConnectToServer(startServer);
    }

    private async Task ConnectToServer(bool launchServer = false)
    {
        if (string.IsNullOrEmpty(_sitConfig.SitVersion) && string.IsNullOrEmpty(_sitConfig.SitTarkovVersion))
        {
            await new ContentDialog()
            {
                Title = _localizationService.TranslateSource("DirectConnectViewModelInstallNotFoundTitle"),
                Content = _localizationService.TranslateSource("DirectConnectViewModelInstallNotFoundMessage"),
                PrimaryButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonOk"),
            }.ShowAsync();
            return;
        }

        AkiServer? lastDirectServer = _configService.Config.SITSettings.LastServer;
        if (lastDirectServer == null)
        {
            lastDirectServer = new AkiServer(GetUriFromAddress(LastServer)!);
            lastDirectServer.Characters.Add(new AkiCharacter());
        }

        lastDirectServer.Characters.Clear();
        AkiCharacter lastDirectCharacter = new()
        {
            Username = Username,
            Password = Password
        };
        lastDirectServer.Characters.Add(lastDirectCharacter);

        Uri? serverAddress = GetUriFromAddress(LastServer);

        List<ValidationRule> validationRules = GenerateValidationRules(serverAddress, launchServer);
        foreach (ValidationRule rule in validationRules)
        {
            if (rule?.Check != null && !rule.Check())
            {
                await new ContentDialog()
                {
                    Title = rule?.Name,
                    Content = rule?.ErrorMessage,
                    CloseButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonOk")
                }.ShowAsync();
                return;
            }
        }

        if (launchServer)
        {
            bool aborted = await LaunchServer();
            if (aborted)
            {
                // TODO log aborted :)
                return;
            }
        }

        if (serverAddress != null)
        {
            AkiServer server = await _serverRequestingService.GetAkiServerAsync(serverAddress, false);
            AkiCharacter character = new(Username, Password);

            try
            {
                await _tarkovClientService.ConnectToServer(server, character);
            }
            catch (AccountNotFoundException)
            {
                ContentDialogResult createAccountResponse = await new ContentDialog()
                {
                    Title = _localizationService.TranslateSource("DirectConnectViewModelAccountNotFound"),
                    Content = _localizationService.TranslateSource("DirectConnectViewModelAccountNotFoundDescription"),
                    PrimaryButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonYes"),
                    CloseButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonNo")
                }.ShowAsync();
                if (createAccountResponse == ContentDialogResult.Primary)
                {
                    AkiCharacter? newCharacter = await _tarkovClientService.CreateCharacter(server, Username, Password, RememberMe);
                    if (newCharacter != null)
                    {
                        await ConnectToServer(false);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                await new ContentDialog()
                {
                    Title = _localizationService.TranslateSource("DirectConnectViewModelLoginErrorTitle"),
                    Content = _localizationService.TranslateSource("DirectConnectViewModelLoginErrorDescription", ex.Message),
                    CloseButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonOk")
                }.ShowAsync();
            }
        }
    }

    private Uri? GetUriFromAddress(string addressString)
    {
        try
        {
            UriBuilder addressBuilder = new(addressString);
            addressBuilder.Port = addressBuilder.Port == 80 ? 6969 : addressBuilder.Port;
            return addressBuilder.Uri;
        }
        catch (UriFormatException)
        {
            return null;
        }
        catch (Exception ex)
        {
            // Something BAAAAD has happened here
            _logger.LogError(ex, "No idea what happened but we didn't manager to get the server uri");
            return null;
        }
    }

    private List<ValidationRule> GenerateValidationRules(Uri? serverAddress, bool launchServer)
    {
        List<ValidationRule> validationRules =
        [
            //Address
            new()
            {
                Name = _localizationService.TranslateSource("DirectConnectViewModelServerAddressTitle"),
                ErrorMessage = _localizationService.TranslateSource("DirectConnectViewModelServerAddressDescription"),
                Check = () => { return serverAddress != null; }
            },
            //Install path
            new()
            {
                Name = _localizationService.TranslateSource("DirectConnectViewModelInstallPathTitle"),
                ErrorMessage = _localizationService.TranslateSource("DirectConnectViewModelInstallPathDescription"),
                Check = () => { return !string.IsNullOrEmpty(_sitConfig.SitEFTInstallPath); }
            },
            //SIT check
            new()
            {
                Name = _localizationService.TranslateSource("DirectConnectViewModelSITInstallationTitle"),
                ErrorMessage = _localizationService.TranslateSource("DirectConnectViewModelSITInstallationDescription", SIT_DLL_FILENAME),
                Check = () => { return File.Exists(Path.Combine(_sitConfig.SitEFTInstallPath, "BepInEx", "plugins", SIT_DLL_FILENAME)); }
            },
            //EFT Check
            new()
            {
                Name = _localizationService.TranslateSource("DirectConnectViewModelEFTInstallationTitle"),
                ErrorMessage = _localizationService.TranslateSource("DirectConnectViewModelEFTInstallationDescription", EFT_EXE_FILENAME),
                Check = () => { return File.Exists(Path.Combine(_sitConfig.SitEFTInstallPath, EFT_EXE_FILENAME)); }
            },
            //Field Check
            new()
            {
                Name = _localizationService.TranslateSource("DirectConnectViewModelInputValidationTitle"),
                ErrorMessage = _localizationService.TranslateSource("DirectConnectViewModelInputValidationDescription"),
                Check = () => { return LastServer.Length > 0 && Username.Length > 0 && Password.Length > 0; }
            }
        ];

        if (launchServer)
        {
            validationRules.AddRange(
            [
                //Unhandled Instance
                new ValidationRule()
                {
                    Name = _localizationService.TranslateSource("DirectConnectViewModelUnhandledAkiInstanceTitle"),
                    ErrorMessage = _localizationService.TranslateSource("DirectConnectViewModelUnhandledAkiInstanceDescription"),
                    Check = () => { return !_akiServerService.IsUnhandledInstanceRunning(); }
                },
                //Missing executable
                new ValidationRule()
                {
                    Name = _localizationService.TranslateSource("DirectConnectViewModelMissingAKIInstallationTitle"),
                    ErrorMessage = _localizationService.TranslateSource("DirectConnectViewModelMissingAKIInstallationDescription"),
                    Check = () => { return File.Exists(_akiServerService.ExecutableFilePath); }
                }
            ]);
        }

        return validationRules;
    }

    private async Task<bool> LaunchServer()
    {
        _akiServerService.Start();

        bool aborted = false;
        RunningState serverState;
        while ((serverState = _akiServerService.State) == RunningState.Starting)
        {
            QuickPlayText = _localizationService.TranslateSource("DirectConnectViewModelWaitingForServerTitle");

            if (serverState == RunningState.Running)
            {
                // We're done the server is running now
                break;
            }
            else if (serverState != RunningState.Starting)
            {
                // We have a state that is not right so need to alert the user and abort
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    new ContentDialog()
                    {
                        Title = _localizationService.TranslateSource("DirectConnectViewModelServerErrorTitle"),
                        Content = _localizationService.TranslateSource("DirectConnectViewModelServerErrorDescription"),
                        CloseButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonOk")
                    }.ShowAsync();
                });
                aborted = true;
                break;
            }

            await Task.Delay(1000);
        }

        QuickPlayText = _localizationService.TranslateSource("DirectConnectViewModelQuickPlayText");
        IsLocalServerRunning = _akiServerService.State == RunningState.Running || _akiServerService.State == RunningState.Starting;
        return aborted;
    }

    protected override void OnActivated()
    {
        base.OnActivated();

        QuickPlayText = _localizationService.TranslateSource("DirectConnectViewModelQuickPlayText");
        IsLocalServerRunning = _akiServerService.State == RunningState.Running || _akiServerService.State == RunningState.Starting;
    }
}
