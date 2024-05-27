﻿using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SIT.Manager.Exceptions;
using SIT.Manager.Interfaces;
using SIT.Manager.Interfaces.ManagedProcesses;
using SIT.Manager.Models;
using SIT.Manager.Models.Aki;
using SIT.Manager.Models.Config;
using SIT.Manager.Models.Play;
using SIT.Manager.Native.Linux;
using SIT.Manager.Views.Play;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SIT.Manager.Services.ManagedProcesses;

public class TarkovClientService(IAkiServerRequestingService serverRequestingService,
                                 IBarNotificationService barNotificationService,
                                 ILocalizationService localizationService,
                                 ILogger<TarkovClientService> logger,
                                 IManagerConfigService configService) : ManagedProcess(barNotificationService, configService), ITarkovClientService
{
    private const string TARKOV_EXE = "EscapeFromTarkov.exe";

    private readonly IAkiServerRequestingService _serverRequestingService = serverRequestingService;
    private readonly ILocalizationService _localizationService = localizationService;
    private readonly ILogger<TarkovClientService> _logger = logger;

    public override string ExecutableDirectory => !string.IsNullOrEmpty(_configService.Config.SitEftInstallPath) ? _configService.Config.SitEftInstallPath : string.Empty;

    protected override string EXECUTABLE_NAME => TARKOV_EXE;

    private void ClearModCache()
    {
        string cachePath = _configService.Config.SitEftInstallPath;
        if (!string.IsNullOrEmpty(cachePath) && Directory.Exists(cachePath))
        {
            // Combine the installPath with the additional subpath.
            cachePath = Path.Combine(cachePath, "BepInEx", "cache");
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
            }
            Directory.CreateDirectory(cachePath);
            _barNotificationService.ShowInformational(_localizationService.TranslateSource("TarkovClientServiceCacheClearedTitle"), _localizationService.TranslateSource("TarkovClientServiceCacheClearedDescription"));
        }
        else
        {
            // Handle the case where InstallPath is not found or empty.
            _barNotificationService.ShowError(_localizationService.TranslateSource("TarkovClientServiceCacheClearedErrorTitle"), _localizationService.TranslateSource("TarkovClientServiceCacheClearedErrorDescription"));
        }
    }

    private static void MinimizeManager()
    {
        IApplicationLifetime? lifetime = App.Current.ApplicationLifetime;

        if (lifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null } desktopLifetime)
        {
            desktopLifetime.MainWindow.WindowState = WindowState.Minimized;
        }
    }

    private static void CloseManager()
    {
        IApplicationLifetime? lifetime = App.Current.ApplicationLifetime;
        if (lifetime != null && lifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private static string CreateLaunchArguments(TarkovLaunchConfig launchConfig, string token)
    {
        string jsonConfig = JsonSerializer.Serialize(launchConfig);

        // The json needs single quotes on Linux for some reason even though not valid json
        // but this seems to work fine on Windows too so might as well do it on both ¯\_(ツ)_/¯
        if (OperatingSystem.IsLinux())
        {
            jsonConfig = jsonConfig.Replace('\"', '\'');
        }

        Dictionary<string, string> argumentList = new()
        {
            { "-token", token },
            { "-config", jsonConfig }
        };

        string launchArguments = string.Join(' ', argumentList.Select(argument => $"{argument.Key}={argument.Value}"));
        if (OperatingSystem.IsLinux())
        {
            // We need to make sure that the json is contained in quotes on Linux otherwise you won't be able to connect to the server.
            launchArguments = string.Join(' ', argumentList.Select(argument => $"{argument.Key}=\"{argument.Value}\""));
        }
        return launchArguments;
    }

    public override void ClearCache()
    {
        ClearLocalCache();
        ClearModCache();
    }

    public void ClearLocalCache()
    {
        string eftCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "Battlestate Games", "EscapeFromTarkov");

        // Check if the directory exists.
        if (Directory.Exists(eftCachePath))
        {
            Directory.Delete(eftCachePath, true);
        }
        else
        {
            // Handle the case where the cache directory does not exist.
            _barNotificationService.ShowWarning(_localizationService.TranslateSource("TarkovClientServiceCacheClearedErrorTitle"), _localizationService.TranslateSource("TarkovClientServiceCacheClearedErrorEFTDescription", eftCachePath));
            return;
        }

        Directory.CreateDirectory(eftCachePath);
        _barNotificationService.ShowInformational(_localizationService.TranslateSource("TarkovClientServiceCacheClearedTitle"), _localizationService.TranslateSource("TarkovClientServiceCacheClearedEFTDescription"));
    }

    public async Task<bool> ConnectToServer(AkiServer server, AkiCharacter character)
    {
        string? ProfileID = null;
        List<AkiMiniProfile> miniProfiles = await _serverRequestingService.GetMiniProfilesAsync(server);
        if (miniProfiles.Select(x => x.Username == character.Username).Any())
        {
            _logger.LogDebug("Username {Username} was already found on server. Attempting to login...", character.Username);
            (string loginRespStr, AkiLoginStatus status) = await _serverRequestingService.LoginAsync(server, character);
            if (status == AkiLoginStatus.Success)
            {
                _logger.LogDebug("Login successful");
                ProfileID = loginRespStr;
            }
            else if (status == AkiLoginStatus.AccountNotFound)
            {
                throw new AccountNotFoundException();
            }
            else
            {
                _logger.LogDebug("Failed to login with error {status}", status);
                await new ContentDialog()
                {
                    Title = _localizationService.TranslateSource("DirectConnectViewModelLoginErrorTitle"),
                    Content = _localizationService.TranslateSource("DirectConnectViewModelLoginIncorrectPassword"),
                    CloseButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonOk")
                }.ShowAsync();
                return false;
            }
        }
        else
        {
            throw new AccountNotFoundException();
        }

        if (ProfileID != null)
        {
            character.ProfileID = ProfileID;
            _logger.LogDebug("{Username}'s ProfileID is {ProfileID}", character.Username, character.ProfileID);
        }

        // Launch game
        string launchArguments = CreateLaunchArguments(new TarkovLaunchConfig { BackendUrl = server.Address.AbsoluteUri.TrimEnd('/') }, character.ProfileID);
        try
        {
            Start(launchArguments);
            while (State == RunningState.Starting)
            {
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occured while launching Tarkov");
            await new ContentDialog()
            {
                Title = _localizationService.TranslateSource("ModsPageViewModelErrorTitle"),
                Content = ex.Message
            }.ShowAsync();
            return false;
        }

        if (_configService.Config.MinimizeAfterLaunch)
        {
            MinimizeManager();
        }

        if (_configService.Config.CloseAfterLaunch)
        {
            CloseManager();
        }
        return true;
    }

    public override void Start(string? arguments)
    {
        UpdateRunningState(RunningState.Starting);

        _process = new Process()
        {
            StartInfo = new ProcessStartInfo(ExecutableFilePath)
            {
                UseShellExecute = true,
                Arguments = arguments
            },
            EnableRaisingEvents = true,
        };
        if (OperatingSystem.IsLinux())
        {
            LinuxConfig linuxConfig = _configService.Config.LinuxConfig;

            // Check if either mangohud or gamemode is enabled.
            StringBuilder argumentsBuilder = new();
            if (linuxConfig.IsGameModeEnabled)
            {
                _process.StartInfo.FileName = "gamemoderun";
                if (linuxConfig.IsMangoHudEnabled)
                {
                    argumentsBuilder.Append("mangohud ");
                }
                argumentsBuilder.Append($"\"{linuxConfig.WineRunner}\" ");
            }
            else if (linuxConfig.IsMangoHudEnabled) // only mangohud is enabled
            {
                _process.StartInfo.FileName = "mangohud";
                argumentsBuilder.Append($"\"{linuxConfig.WineRunner}\" ");
            }
            else
            {
                _process.StartInfo.FileName = linuxConfig.WineRunner;
            }

            // force-gfx-jobs native is a workaround for the Unity bug that causes the game to crash on startup.
            // Taken from SPT Aki.Launcher.Base/Controllers/GameStarter.cs
            argumentsBuilder.Append($"\"{ExecutableFilePath}\" -force-gfx-jobs native ");
            if (!string.IsNullOrEmpty(arguments))
            {
                argumentsBuilder.Append(arguments);
            }
            _process.StartInfo.Arguments = argumentsBuilder.ToString();
            _process.StartInfo.UseShellExecute = false;

            _process.StartInfo.EnvironmentVariables["WINEPREFIX"] = linuxConfig.WinePrefix;
            _process.StartInfo.EnvironmentVariables["WINEESYNC"] = linuxConfig.IsEsyncEnabled ? "1" : "0";
            _process.StartInfo.EnvironmentVariables["WINEFSYNC"] = linuxConfig.IsFsyncEnabled ? "1" : "0";
            _process.StartInfo.EnvironmentVariables["WINE_FULLSCREEN_FSR"] = linuxConfig.IsWineFsrEnabled ? "1" : "0";
            _process.StartInfo.EnvironmentVariables["DXVK_NVAPIHACK"] = "0";
            _process.StartInfo.EnvironmentVariables["DXVK_ENABLE_NVAPI"] = linuxConfig.IsDXVK_NVAPIEnabled ? "1" : "0";
            _process.StartInfo.EnvironmentVariables["WINEARCH"] = "win64";
            _process.StartInfo.EnvironmentVariables["MANGOHUD"] = linuxConfig.IsMangoHudEnabled ? "1" : "0";
            _process.StartInfo.EnvironmentVariables["MANGOHUD_DLSYM"] = linuxConfig.IsMangoHudEnabled ? "1" : "0";
            _process.StartInfo.EnvironmentVariables["__GL_SHADER_DISK_CACHE"] = "1";
            _process.StartInfo.EnvironmentVariables["__GL_SHADER_DISK_CACHE_PATH"] = linuxConfig.WinePrefix;
            _process.StartInfo.EnvironmentVariables["DXVK_STATE_CACHE_PATH"] = linuxConfig.WinePrefix;
            // TODO: add the ability to add custom DLL overrides.
            string str = DllManager.GetDllOverride(linuxConfig);
            _process.StartInfo.EnvironmentVariables.Add("WINEDLLOVERRIDES", str);
        }
        else
        {
            _process.StartInfo.WorkingDirectory = ExecutableDirectory;
        }

        _process.Exited += ExitedEvent;
        _process.Start();

        if (_configService.Config.CloseAfterLaunch)
        {
            IApplicationLifetime? lifetime = App.Current.ApplicationLifetime;
            if (lifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                desktopLifetime.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        }
        UpdateRunningState(RunningState.Running);
    }

    public async Task<AkiCharacter?> CreateCharacter(AkiServer server, string username, string password, bool rememberLogin)
    {
        List<TarkovEdition> editions = [];
        AkiServerInfo? serverInfo = await _serverRequestingService.GetAkiServerInfoAsync(server);
        if (serverInfo != null)
        {
            foreach (string edition in serverInfo.Editions)
            {
                editions.Add(new TarkovEdition(edition, serverInfo.Descriptions[edition]));
            }
        }

        CreateCharacterDialogResult result = await new CreateCharacterDialogView(username, password, rememberLogin, [.. editions]).ShowAsync();
        if (result.DialogResult != ContentDialogResult.Primary)
        {
            return null;
        }

        AkiCharacter character = new(result.Username, result.Password)
        {
            Edition = result.TarkovEdition.Edition
        };

        _logger.LogInformation("Registering new character...");
        (string _, AkiLoginStatus status) = await _serverRequestingService.RegisterCharacterAsync(server, character);
        if (status != AkiLoginStatus.Success)
        {
            await new ContentDialog()
            {
                Title = _localizationService.TranslateSource("DirectConnectViewModelLoginErrorTitle"),
                Content = _localizationService.TranslateSource("DirectConnectViewModelLoginErrorDescription"),
                CloseButtonText = _localizationService.TranslateSource("DirectConnectViewModelButtonOk")
            }.ShowAsync();
            _logger.LogDebug("Register character failed with {status}", status);
            return null;
        }

        if (result.SaveLogin)
        {
            server.Characters.Add(character);
            int index = _configService.Config.BookmarkedServers.FindIndex(x => x.Address == server.Address);
            if (index != -1 && !_configService.Config.BookmarkedServers[index].Characters.Any(x => x.Username == character.Username))
            {
                _configService.Config.BookmarkedServers[index].Characters.Add(character);
            }
            _configService.UpdateConfig(_configService.Config);
        }

        return character;
    }

    public async Task<AkiCharacter?> CreateCharacter(AkiServer server)
    {
        return await CreateCharacter(server, string.Empty, string.Empty, false).ConfigureAwait(false);
    }
}
