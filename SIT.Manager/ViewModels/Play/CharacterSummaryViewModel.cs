﻿using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SIT.Manager.Interfaces;
using SIT.Manager.Interfaces.ManagedProcesses;
using SIT.Manager.Models.Aki;
using SIT.Manager.Services.Caching;
using SIT.Manager.Views.Play;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SIT.Manager.ViewModels.Play;

public partial class CharacterSummaryViewModel : ObservableRecipient
{
    private readonly ILogger _logger;
    private readonly IManagerConfigService _configService;
    private readonly ILocalizationService _localizationService;
    private readonly ITarkovClientService _tarkovClientService;
    private readonly ICachingService _cachingService;
    private readonly IAkiServerRequestingService _akiServerRequestingService;

    private readonly AkiServer _connectedServer;
    private readonly AkiCharacter? character;

    [ObservableProperty]
    private AkiMiniProfile _profile;

    [ObservableProperty]
    private Bitmap? _sideImage;

    [ObservableProperty]
    private double _levelProgressPercentage = 0;

    [ObservableProperty]
    private int _nextLevel = 0;

    [ObservableProperty]
    private bool _requireLogin = true;

    public IAsyncRelayCommand PlayCommand { get; }

    public CharacterSummaryViewModel(AkiServer server,
        AkiMiniProfile profile,
        ILocalizationService localizationService,
        ILogger<CharacterSummaryViewModel> logger,
        IManagerConfigService configService,
        ITarkovClientService tarkovClientService,
        ICachingService cachingService,
        IAkiServerRequestingService akiServerRequestingService)
    {
        _logger = logger;
        _configService = configService;
        _localizationService = localizationService;
        _tarkovClientService = tarkovClientService;
        _cachingService = cachingService;
        _akiServerRequestingService = akiServerRequestingService;

        _connectedServer = server;
        Profile = profile;

        double requiredExperience = Profile.NextExperience - Profile.PreviousExperience;
        double currentExperienceProgress = profile.CurrentExperience - Profile.PreviousExperience;
        LevelProgressPercentage = currentExperienceProgress / requiredExperience * 100;

        NextLevel = Profile.CurrentLevel + 1;
        if (Profile.CurrentLevel == Profile.MaxLevel)
        {
            NextLevel = Profile.MaxLevel;
        }

        character = _connectedServer.Characters.FirstOrDefault(x => x.Username == Profile.Username);
        if (character != null)
        {
            int serverIndex = _configService.Config.BookmarkedServers.FindIndex(x => x.Address == character.ParentServer.Address);
            if (serverIndex != -1)
            {
                character = _configService.Config.BookmarkedServers[serverIndex].Characters.FirstOrDefault(x => x?.Username == character.Username, null);
                RequireLogin = string.IsNullOrEmpty(character?.Password);
            }
        }

        Task.Run(SetSideImage);

        PlayCommand = new AsyncRelayCommand(Play);
    }

    private async Task SetSideImage()
    {
        if (!string.IsNullOrEmpty(Profile.Side) && !Profile.Side.Equals("unknown", StringComparison.InvariantCultureIgnoreCase))
        {
            string cacheKey = $"side_{Profile.Side} icon";
            //TODO: Change this from bitmap to memorystream and load it into bitmap. This will allow us to save to disk
            CacheValue<Bitmap> cacheVal = await _cachingService.InMemory.GetOrComputeAsync<Bitmap>(cacheKey, async (key) =>
            {
                return new(await _akiServerRequestingService.GetAkiServerImage(_connectedServer, $"launcher/side_{Profile.Side.ToLower()}.png"));
            });
            if (cacheVal.HasValue && cacheVal.Value != null)
                SideImage = cacheVal.Value;
        }
        else
        {
            //TODO: Set a default. Not done because idk what to set it to
        }
    }

    private async Task Play()
    {
        AkiCharacter? character = _connectedServer.Characters.FirstOrDefault(x => x.Username == Profile.Username);
        bool rememberLogin = true;

        if (character == null)
        {
            LoginDialogView dialog = new(Profile.Username);
            (ContentDialogResult result, string password, rememberLogin) = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }
            character = new(_connectedServer, Profile.Username, password);
        }

        try
        {
            await _tarkovClientService.ConnectToServer(character);

            if (rememberLogin)
            {
                character.ParentServer.Characters.Add(character);
                int index = _configService.Config.BookmarkedServers.FindIndex(x => x.Address == character.ParentServer.Address);
                if (index != -1 && !_configService.Config.BookmarkedServers[index].Characters.Any(x => x.Username == character.Username))
                {
                    _configService.Config.BookmarkedServers[index].Characters.Add(character);
                }
                _configService.UpdateConfig(_configService.Config);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to connect to Tarkov server");
            ContentDialog errorDialog = new()
            {
                Title = _localizationService.TranslateSource("CharacterSummaryViewModelPlayErrorDialogTitle"),
                Content = _localizationService.TranslateSource("CharacterSummaryViewModelPlayErrorDialogContent"),
                PrimaryButtonText = _localizationService.TranslateSource("CharacterSummaryViewModelPlayErrorDialogPrimaryButtonText"),
            };
            await errorDialog.ShowAsync();
        }
    }
}