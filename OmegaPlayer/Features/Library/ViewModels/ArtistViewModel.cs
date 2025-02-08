﻿using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OmegaPlayer.Core.Interfaces;
using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Features.Library.Services;
using OmegaPlayer.Features.Playback.ViewModels;
using OmegaPlayer.Features.Playlists.Views;
using OmegaPlayer.Features.Shell.ViewModels;
using OmegaPlayer.Infrastructure.Data.Repositories;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace OmegaPlayer.Features.Library.ViewModels
{
    public partial class ArtistViewModel : SortableCollectionViewModel, ILoadMoreItems
    {
        private readonly ArtistDisplayService _artistsDisplayService;
        private readonly TrackQueueViewModel _trackQueueViewModel;
        private readonly PlaylistViewModel _playlistViewModel;
        private readonly AllTracksRepository _allTracksRepository;
        private readonly MainViewModel _mainViewModel;
        private List<ArtistDisplayModel> AllArtists { get; set; }

        [ObservableProperty]
        private ObservableCollection<ArtistDisplayModel> _artists = new();

        [ObservableProperty]
        private ObservableCollection<ArtistDisplayModel> _selectedArtists = new();

        [ObservableProperty]
        private bool _hasSelectedArtists;

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

        public ArtistViewModel(
            ArtistDisplayService artistsDisplayService,
            TrackQueueViewModel trackQueueViewModel,
            PlaylistViewModel playlistViewModel,
            MainViewModel mainViewModel,
            TrackSortService trackSortService,
            AllTracksRepository allTracksRepository,
            IMessenger messenger)
            : base(trackSortService, messenger)
        {
            _artistsDisplayService = artistsDisplayService;
            _trackQueueViewModel = trackQueueViewModel;
            _playlistViewModel = playlistViewModel;
            _allTracksRepository = allTracksRepository;
            _mainViewModel = mainViewModel;

            LoadInitialArtists();
        }

        protected override void ApplyCurrentSort()
        {
            // Clear existing artists
            Artists.Clear();
            _currentPage = 1;

            // Load first page with new sort settings
            LoadMoreItems().ConfigureAwait(false);
        }

        private async void LoadInitialArtists()
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
                // First load all artists if not already loaded
                if (AllArtists == null)
                {
                    await LoadAllArtistsAsync();
                }

                // Get the sorted list based on current sort settings
                var sortedArtists = GetSortedAllArtists();

                // Calculate the page range
                var startIndex = (_currentPage - 1) * _pageSize;
                var pageItems = sortedArtists
                    .Skip(startIndex)
                    .Take(_pageSize)
                    .ToList();

                var totalArtists = pageItems.Count;
                var current = 0;
                var newArtists = new List<ArtistDisplayModel>();

                foreach (var artist in pageItems)
                {
                    await Task.Run(async () =>
                    {
                        // Load artist photo
                        await _artistsDisplayService.LoadArtistPhotoAsync(artist);

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            newArtists.Add(artist);
                            current++;
                            LoadingProgress = (current * 100.0) / totalArtists;
                        });
                    });
                }

                // Add all processed artists to the collection
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var artist in newArtists)
                    {
                        Artists.Add(artist);
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
        private async Task LoadAllArtistsAsync()
        {
            AllArtists = await _artistsDisplayService.GetAllArtistsAsync();
        }

        private IEnumerable<ArtistDisplayModel> GetSortedAllArtists()
        {
            if (AllArtists == null) return new List<ArtistDisplayModel>();

            var sortedArtists = CurrentSortType switch
            {
                SortType.Duration => _trackSortService.SortItems(
                    AllArtists,
                    SortType.Duration,
                    CurrentSortDirection,
                    a => a.Name,
                    a => (int)a.TotalDuration.TotalSeconds),
                _ => _trackSortService.SortItems(
                    AllArtists,
                    SortType.Name,
                    CurrentSortDirection,
                    a => a.Name)
            };

            return sortedArtists;
        }


        [RelayCommand]
        public async Task OpenArtistDetails(ArtistDisplayModel artist)
        {
            if (artist == null) return;
            await _mainViewModel.NavigateToDetails(ContentType.Artist, artist);
        }

        [RelayCommand]
        public void SelectArtist(ArtistDisplayModel artist)
        {
            if (artist.IsSelected)
            {
                SelectedArtists.Add(artist);
            }
            else
            {
                SelectedArtists.Remove(artist);
            }
            HasSelectedArtists = SelectedArtists.Any();
        }

        [RelayCommand]
        public void ClearSelection()
        {
            foreach (var artist in Artists)
            {
                artist.IsSelected = false;
            }
            SelectedArtists.Clear();
            HasSelectedArtists = false;
        }

        [RelayCommand]
        public async Task PlayArtistFromHere(ArtistDisplayModel selectedArtist)
        {
            if (selectedArtist == null) return;

            var allArtistTracks = new List<TrackDisplayModel>();
            var startPlayingFromIndex = 0;
            var tracksAdded = 0;

            // Get sorted list of all artists
            var sortedArtists = GetSortedAllArtists();

            foreach (var artist in sortedArtists)
            {
                // Get tracks for this artist and sort them by album and Title
                var tracks = (await _artistsDisplayService.GetArtistTracksAsync(artist.ArtistID))
                    .OrderBy(t => t.AlbumTitle)
                    .ThenBy(t => t.Title)
                    .ToList();

                if (artist.ArtistID == selectedArtist.ArtistID)
                {
                    startPlayingFromIndex = tracksAdded;
                }

                allArtistTracks.AddRange(tracks);
                tracksAdded += tracks.Count;
            }

            if (!allArtistTracks.Any()) return;

            var startTrack = allArtistTracks[startPlayingFromIndex];
            _trackQueueViewModel.PlayThisTrack(startTrack, new ObservableCollection<TrackDisplayModel>(allArtistTracks));
        }

        [RelayCommand]
        public async Task PlayArtistTracks(ArtistDisplayModel artist)
        {
            if (artist == null) return;

            var tracks = await _artistsDisplayService.GetArtistTracksAsync(artist.ArtistID);
            if (tracks.Any())
            {
                _trackQueueViewModel.PlayThisTrack(tracks.First(), new ObservableCollection<TrackDisplayModel>(tracks));
            }
        }

        [RelayCommand]
        public async Task AddArtistTracksToNext(ArtistDisplayModel artist)
        {
            if (artist == null) return;

            var tracks = await _artistsDisplayService.GetArtistTracksAsync(artist.ArtistID);
            if (tracks.Any())
            {
                _trackQueueViewModel.AddToPlayNext(new ObservableCollection<TrackDisplayModel>(tracks));
            }
        }

        [RelayCommand]
        public async Task AddArtistTracksToQueue(ArtistDisplayModel artist)
        {
            if (artist == null) return;

            var tracks = await _artistsDisplayService.GetArtistTracksAsync(artist.ArtistID);
            if (tracks.Any())
            {
                _trackQueueViewModel.AddTrackToQueue(new ObservableCollection<TrackDisplayModel>(tracks));
            }
        }

        public async Task<List<TrackDisplayModel>> GetSelectedArtistTracks(int artistId)
        {
            var selectedArtists = SelectedArtists;
            if (selectedArtists.Count <= 1)
            {
                return await _artistsDisplayService.GetArtistTracksAsync(artistId);
            }

            var trackTasks = selectedArtists.Select(Artist =>
                _artistsDisplayService.GetArtistTracksAsync(Artist.ArtistID));

            var allTrackLists = await Task.WhenAll(trackTasks);
            return allTrackLists.SelectMany(tracks => tracks).ToList();
        }

        [RelayCommand]
        public async Task ShowPlaylistSelectionDialog(ArtistDisplayModel artist)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                if (mainWindow == null || !mainWindow.IsVisible) return;

                var selectedTracks = await GetSelectedArtistTracks(artist.ArtistID);

                var dialog = new PlaylistSelectionDialog();
                dialog.Initialize(_playlistViewModel, null, selectedTracks);
                await dialog.ShowDialog(mainWindow);

                ClearSelection();
            }
        }

        public async Task LoadHighResPhotosForVisibleArtistsAsync(IList<ArtistDisplayModel> visibleArtists)
        {
            foreach (var artist in visibleArtists)
            {
                if (artist.PhotoSize != "high")
                {
                    await _artistsDisplayService.LoadHighResArtistPhotoAsync(artist);
                    artist.PhotoSize = "high";
                }
            }
        }
    }

}