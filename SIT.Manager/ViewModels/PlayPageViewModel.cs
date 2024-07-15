﻿using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using SIT.Manager.Interfaces;
using SIT.Manager.Models.Aki;
using SIT.Manager.Models.Play;
using SIT.Manager.Services.Caching;
using SIT.Manager.Views.Play;
using System;

namespace SIT.Manager.ViewModels;

public partial class PlayPageViewModel : ObservableRecipient,
                                         IRecipient<ServerConnectMessage>,
                                         IRecipient<ConnectedServerRequestMessage>,
                                         IRecipient<ServerDisconnectMessage>
{
    private const string SELECTED_TAB_INDEX_CACHE_KEY = "LastSelectedPlayPageTabIndex";

    private readonly ICachingService _cachingService;

    private AkiServer? _connectedServer;

    [ObservableProperty]
    private UserControl _playControl;

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public PlayPageViewModel(ICachingService cachingService)
    {
        _cachingService = cachingService;

        CacheValue<int> indexValue = _cachingService.OnDisk.GetOrCompute(SELECTED_TAB_INDEX_CACHE_KEY, (key) =>
        {
            return SelectedTabIndex;
        });
        SelectedTabIndex = indexValue.Value;

        PlayControl = new ServerSelectionView();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (_cachingService.OnDisk.Exists(SELECTED_TAB_INDEX_CACHE_KEY))
        {
            _cachingService.OnDisk.TryRemove(SELECTED_TAB_INDEX_CACHE_KEY);
        }
        _cachingService.OnDisk.TryAdd(SELECTED_TAB_INDEX_CACHE_KEY, value);
    }

    public void Receive(ServerConnectMessage message)
    {
        _connectedServer = message.Value;
        PlayControl = new CharacterSelectionView();
    }

    public void Receive(ConnectedServerRequestMessage message)
    {
        if (_connectedServer != null)
        {
            message.Reply(_connectedServer);
        }
        else
        {
            throw new Exception("_connectedServer is null when it shouldn't be");
        }
    }

    public void Receive(ServerDisconnectMessage message)
    {
        _connectedServer = null;
        PlayControl = new ServerSelectionView();
    }
}
