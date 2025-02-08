﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmegaPlayer.Core.Interfaces;
using OmegaPlayer.Features.Library.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using OmegaPlayer.Features.Library.Services;
using OmegaPlayer.Features.Playback.ViewModels;
using System.Collections.Generic;
using OmegaPlayer.Features.Shell.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia.Controls.ApplicationLifetimes;
using OmegaPlayer.Features.Playlists.Views;
using Avalonia;
using OmegaPlayer.Infrastructure.Data.Repositories;
using System;

namespace OmegaPlayer.Features.Library.ViewModels
{
    public partial class AlbumViewModel : SortableCollectionViewModel, ILoadMoreItems
    {
        private readonly AlbumDisplayService _albumsDisplayService;
        private readonly TrackQueueViewModel _trackQueueViewModel;
        private readonly PlaylistViewModel _playlistViewModel;
        private readonly AllTracksRepository _allTracksRepository;
        private readonly MainViewModel _mainViewModel;
        private List<AlbumDisplayModel> AllAlbums { get; set; }

        [ObservableProperty]
        private ObservableCollection<AlbumDisplayModel> _albums = new();

        [ObservableProperty]
        private ObservableCollection<AlbumDisplayModel> _selectedAlbums = new();

        [ObservableProperty]
        private bool _hasSelectedAlbums;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private double _loadingProgress;

        [ObservableProperty]
        private int _currentPage = 1;

        private const int _pageSize = 50;

        private AsyncRelayCommand _loadMoreItemsCommand;
        public System.Windows.Input.ICommand LoadMoreItemsCommand =>
            _loadMoreItemsCommand ??= new AsyncRelayCommand(LoadMoreItems);

        public AlbumViewModel(
            AlbumDisplayService albumsDisplayService,
            TrackQueueViewModel trackQueueViewModel,
            PlaylistViewModel playlistViewModel,
            MainViewModel mainViewModel,
            TrackSortService trackSortService,
            AllTracksRepository allTracksRepository,
            IMessenger messenger)
            : base(trackSortService, messenger)
        {
            _albumsDisplayService = albumsDisplayService;
            _trackQueueViewModel = trackQueueViewModel;
            _playlistViewModel = playlistViewModel;
            _allTracksRepository = allTracksRepository;
            _mainViewModel = mainViewModel;

            LoadInitialAlbums();
        }
        protected override void ApplyCurrentSort()
        {
            // Clear existing albums
            Albums.Clear();
            _currentPage = 1;

            // Load first page with new sort settings
            LoadMoreItems().ConfigureAwait(false);
        }


        private async void LoadInitialAlbums()
        {
            await LoadMoreItems();
        }

        private async Task LoadMoreItems()
        {
            if (IsLoading) return;

            IsLoading = true;
            LoadingProgress = 0;

            try
            {
                // First load all albums if not already loaded
                if (AllAlbums == null)
                {
                    await LoadAllAlbumsAsync();
                }

                // Get the sorted list based on current sort settings
                var sortedAlbums = GetSortedAllAlbums();

                // Calculate the page range
                var startIndex = (_currentPage - 1) * _pageSize;
                var pageItems = sortedAlbums
                    .Skip(startIndex)
                    .Take(_pageSize)
                    .ToList();

                var totalAlbums = pageItems.Count;
                var current = 0;
                var newAlbums = new List<AlbumDisplayModel>();

                foreach (var album in pageItems)
                {
                    await Task.Run(async () =>
                    {
                        await _albumsDisplayService.LoadAlbumCoverAsync(album);

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            newAlbums.Add(album);
                            current++;
                            LoadingProgress = (current * 100.0) / totalAlbums;
                        });
                    });
                }

                // Add all processed albums to the collection
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var album in newAlbums)
                    {
                        Albums.Add(album);
                    }
                });

                _currentPage++;
            }
            finally
            {
                await Task.Delay(500); // Small delay for smoother UI
                IsLoading = false;
            }
        }

        private async Task LoadAllAlbumsAsync()
        {
            AllAlbums = await _albumsDisplayService.GetAllAlbumsAsync();
        }
        private IEnumerable<AlbumDisplayModel> GetSortedAllAlbums()
        {
            if (AllAlbums == null) return new List<AlbumDisplayModel>();

            var sortedAlbums = CurrentSortType switch
            {
                SortType.Duration => _trackSortService.SortItems(
                    AllAlbums,
                    SortType.Duration,
                    CurrentSortDirection,
                    a => a.Title,
                    a => (int)a.TotalDuration.TotalSeconds),
                _ => _trackSortService.SortItems(
                    AllAlbums,
                    SortType.Name,
                    CurrentSortDirection,
                    a => a.Title)
            };

            return sortedAlbums;
        }


        [RelayCommand]
        public async Task OpenAlbumDetails(AlbumDisplayModel album)
        {
            if (album == null) return;
            await _mainViewModel.NavigateToDetails(ContentType.Album, album);
        }

        [RelayCommand]
        public void SelectAlbum(AlbumDisplayModel album)
        {
            if (album.IsSelected)
            {
                SelectedAlbums.Add(album);
            }
            else
            {
                SelectedAlbums.Remove(album);
            }
            HasSelectedAlbums = SelectedAlbums.Any();
        }


        [RelayCommand]
        public void ClearSelection()
        {
            foreach (var album in Albums)
            {
                album.IsSelected = false;
            }
            SelectedAlbums.Clear();
            HasSelectedAlbums = false;
        }

        [RelayCommand]
        public async Task PlayAlbumFromHere(AlbumDisplayModel selectedAlbum)
        {
            if (selectedAlbum == null) return;

            var allAlbumTracks = new List<TrackDisplayModel>();
            var startPlayingFromIndex = 0;
            var tracksAdded = 0;

            // Get sorted list of all albums
            var sortedAlbums = GetSortedAllAlbums();

            foreach (var album in sortedAlbums)
            {
                // Get tracks for this album and sort them by Title
                var tracks = (await _albumsDisplayService.GetAlbumTracksAsync(album.AlbumID))
                    .OrderBy(t => t.Title)
                    .ToList();

                if (album.AlbumID == selectedAlbum.AlbumID)
                {
                    startPlayingFromIndex = tracksAdded;
                }

                allAlbumTracks.AddRange(tracks);
                tracksAdded += tracks.Count;
            }

            if (!allAlbumTracks.Any()) return;

            var startTrack = allAlbumTracks[startPlayingFromIndex];
            _trackQueueViewModel.PlayThisTrack(startTrack, new ObservableCollection<TrackDisplayModel>(allAlbumTracks));
        }


        [RelayCommand]
        public async Task PlayAlbumTracks(AlbumDisplayModel album)
        {
            if (album == null) return;

            var tracks = await _albumsDisplayService.GetAlbumTracksAsync(album.AlbumID);
            if (tracks.Any())
            {
                _trackQueueViewModel.PlayThisTrack(tracks.First(), new ObservableCollection<TrackDisplayModel>(tracks));
            }
        }

        [RelayCommand]
        public async Task AddAlbumTracksToNext(AlbumDisplayModel album)
        {
            if (album == null) return;

            var tracks = await _albumsDisplayService.GetAlbumTracksAsync(album.AlbumID);
            if (tracks.Any())
            {
                _trackQueueViewModel.AddToPlayNext(new ObservableCollection<TrackDisplayModel>(tracks));
            }
        }

        [RelayCommand]
        public async Task AddAlbumTracksToQueue(AlbumDisplayModel album)
        {
            if (album == null) return;

            var tracks = await _albumsDisplayService.GetAlbumTracksAsync(album.AlbumID);
            if (tracks.Any())
            {
                _trackQueueViewModel.AddTrackToQueue(new ObservableCollection<TrackDisplayModel>(tracks));
            }
        }

        public async Task<List<TrackDisplayModel>> GetSelectedAlbumTracks(int albumId)
        {
            var selectedAlbums = SelectedAlbums;
            if (selectedAlbums.Count <= 1)
            {
                return await _albumsDisplayService.GetAlbumTracksAsync(albumId);
            }

            var trackTasks = selectedAlbums.Select(album =>
                _albumsDisplayService.GetAlbumTracksAsync(album.AlbumID));

            var allTrackLists = await Task.WhenAll(trackTasks);
            return allTrackLists.SelectMany(tracks => tracks).ToList();
        }

        [RelayCommand]
        public async Task ShowPlaylistSelectionDialog(AlbumDisplayModel album)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                if (mainWindow == null || !mainWindow.IsVisible) return;

                var selectedTracks = await GetSelectedAlbumTracks(album.AlbumID);

                var dialog = new PlaylistSelectionDialog();
                dialog.Initialize(_playlistViewModel, null, selectedTracks);
                await dialog.ShowDialog(mainWindow);

                ClearSelection();
            }
        }

        public async Task LoadHighResCoversForVisibleAlbumsAsync(IList<AlbumDisplayModel> visibleAlbums)
        {
            foreach (var album in visibleAlbums)
            {
                if (album.CoverSize != "high")
                {
                    await _albumsDisplayService.LoadHighResAlbumCoverAsync(album);
                    album.CoverSize = "high";
                }
            }
        }
    }
}