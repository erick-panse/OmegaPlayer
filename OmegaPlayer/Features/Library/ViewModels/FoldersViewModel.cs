﻿using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OmegaPlayer.Core.Enums;
using OmegaPlayer.Core.Interfaces;
using OmegaPlayer.Core.Services;
using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Features.Library.Services;
using OmegaPlayer.Features.Playback.ViewModels;
using OmegaPlayer.Features.Playlists.Models;
using OmegaPlayer.Features.Playlists.Views;
using OmegaPlayer.Features.Profile.ViewModels;
using OmegaPlayer.Features.Shell.ViewModels;
using OmegaPlayer.Infrastructure.Data.Repositories;
using OmegaPlayer.Infrastructure.Services.Images;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagLib;

namespace OmegaPlayer.Features.Library.ViewModels
{
    public partial class FoldersViewModel : SortableCollectionViewModel, ILoadMoreItems
    {
        private readonly FolderDisplayService _folderDisplayService;
        private readonly TrackQueueViewModel _trackQueueViewModel;
        private readonly PlaylistsViewModel _playlistViewModel;
        private readonly AllTracksRepository _allTracksRepository;
        private readonly StandardImageService _standardImageService;
        private readonly MainViewModel _mainViewModel;
        private readonly ProfileManager _profileManager;

        private List<FolderDisplayModel> AllFolders { get; set; }

        // Add cancellation token source for load operations
        private CancellationTokenSource _cts = new CancellationTokenSource();

        [ObservableProperty]
        private ObservableCollection<FolderDisplayModel> _folders = new();

        [ObservableProperty]
        private ObservableCollection<FolderDisplayModel> _selectedFolders = new();

        [ObservableProperty]
        private bool _hasSelectedFolders;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private double _loadingProgress;

        private int _currentPage = 1;

        private const int _pageSize = 50;

        private bool _isInitialized = false;

        private AsyncRelayCommand _loadMoreItemsCommand;
        public System.Windows.Input.ICommand LoadMoreItemsCommand =>
            _loadMoreItemsCommand ??= new AsyncRelayCommand(LoadMoreItems);

        public FoldersViewModel(
            FolderDisplayService folderDisplayService,
            TrackQueueViewModel trackQueueViewModel,
            PlaylistsViewModel playlistViewModel,
            MainViewModel mainViewModel,
            TrackSortService trackSortService,
            AllTracksRepository allTracksRepository,
            StandardImageService standardImageService,
            ProfileManager profileManager,
            IErrorHandlingService errorHandlingService,
            IMessenger messenger)
            : base(trackSortService, messenger, errorHandlingService)
        {
            _folderDisplayService = folderDisplayService;
            _trackQueueViewModel = trackQueueViewModel;
            _playlistViewModel = playlistViewModel;
            _allTracksRepository = allTracksRepository;
            _standardImageService = standardImageService;
            _mainViewModel = mainViewModel;
            _profileManager = profileManager;

            LoadInitialFolders();

            // Update Content on profile switch
            _messenger.Register<ProfileUpdateMessage>(this, async (r, m) => await HandleProfileSwitch(m));
        }

        private async Task HandleProfileSwitch(ProfileUpdateMessage message)
        {
            await _errorHandlingService.SafeExecuteAsync(
                async () =>
                {
                    // Cancel any ongoing loading operation
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = new CancellationTokenSource();

                    // Reset state
                    _isInitialized = false;
                    AllFolders = null;

                    // Clear collections on UI thread to prevent cross-thread exceptions
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Folders.Clear();
                        SelectedFolders.Clear();
                        HasSelectedFolders = false;
                    });

                    // Reset pagination
                    _currentPage = 1;

                    // Trigger reload
                    await LoadMoreItems();
                },
                "Handling profile switch for folders view",
                ErrorSeverity.NonCritical,
                false);
        }

        protected override void ApplyCurrentSort()
        {
            if (!_isInitialized) return;

            // Clear existing folders
            Folders.Clear();
            _currentPage = 1;

            // Load first page with new sort settings
            LoadMoreItems().ConfigureAwait(false);
        }

        /// <summary>
        /// Notifies the image loading system about folder visibility changes
        /// </summary>
        public async Task NotifyFolderVisible(FolderDisplayModel folder, bool isVisible)
        {
            if (folder == null) return;

            var tracks = await _folderDisplayService.GetFolderTracksAsync(folder.FolderPath);

            var firstTrack = tracks.FirstOrDefault();
            if (_standardImageService != null && firstTrack != null)
            {
                await _standardImageService.NotifyImageVisible(firstTrack.CoverPath, isVisible);
            }
        }

        private async void LoadInitialFolders()
        {
            await Task.Delay(100);

            if (!_isInitialized)
            {
                _isInitialized = true;
                await LoadMoreItems();
            }
        }

        public override void OnSortSettingsReceived(SortType sortType, SortDirection direction, bool isUserInitiated = false)
        {
            base.OnSortSettingsReceived(sortType, direction, false); // Never auto-apply sort

            if (!_isInitialized)
            {
                LoadInitialFolders();
            }
            else if (isUserInitiated)
            {
                // Only apply sort if user initiated the change
                ApplyCurrentSort();
            }
        }

        private async Task LoadMoreItems()
        {
            if (IsLoading) return;

            // Get the cancellation token for this load operation
            var cancellationToken = _cts.Token;

            IsLoading = true;
            LoadingProgress = 0;

            try
            {
                // First load all folders if not already loaded
                if (AllFolders == null)
                {
                    // Check if cancelled
                    cancellationToken.ThrowIfCancellationRequested();

                    AllFolders = await _folderDisplayService.GetAllFoldersAsync();

                    // Check again after potentially long operation
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // Get the sorted list based on current sort settings
                var sortedFolders = GetSortedAllFolders();

                // Calculate the page range
                var startIndex = (_currentPage - 1) * _pageSize;
                var pageItems = sortedFolders
                    .Skip(startIndex)
                    .Take(_pageSize)
                    .ToList();

                var totalFolders = pageItems.Count;
                var current = 0;
                var newFolders = new List<FolderDisplayModel>();

                foreach (var folder in pageItems)
                {
                    // Check if cancelled before processing each folder
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var tracks = await _folderDisplayService.GetFolderTracksAsync(folder.FolderPath);
                        var firstTrack = tracks.FirstOrDefault();
                        if (firstTrack != null)
                        {
                            await _folderDisplayService.LoadFolderCoverAsync(folder, firstTrack.CoverPath, "medium", true);
                        }

                        // Check again after loading cover
                        cancellationToken.ThrowIfCancellationRequested();

                        newFolders.Add(folder);
                        current++;
                        LoadingProgress = (current * 100.0) / totalFolders;
                    }
                    catch (OperationCanceledException)
                    {
                        // Rethrow to be caught by the outer handler
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading folder: {ex.Message}");
                    }
                }

                // Check if cancelled before updating UI
                cancellationToken.ThrowIfCancellationRequested();

                // Add all processed folders to the collection on UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var folder in newFolders)
                    {
                        Folders.Add(folder);
                    }
                });

                _currentPage++;
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled, just exit quietly
            }
            catch (Exception ex)
            {
                _errorHandlingService.LogError(
                    ErrorSeverity.NonCritical,
                    "Loading more folders",
                    ex.Message,
                    ex,
                    false);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private IEnumerable<FolderDisplayModel> GetSortedAllFolders()
        {
            if (AllFolders == null) return new List<FolderDisplayModel>();

            var sortedFolders = CurrentSortType switch
            {
                SortType.Duration => _trackSortService.SortItems(
                    AllFolders,
                    SortType.Duration,
                    CurrentSortDirection,
                    f => f.FolderName,
                    f => (int)f.TotalDuration.TotalSeconds),
                _ => _trackSortService.SortItems(
                    AllFolders,
                    SortType.Name,
                    CurrentSortDirection,
                    f => f.FolderName)
            };

            return sortedFolders;
        }

        [RelayCommand]
        public async Task OpenFolderDetails(FolderDisplayModel folder)
        {
            if (folder == null) return;
            await _mainViewModel.NavigateToDetails(ContentType.Folder, folder);
        }

        [RelayCommand]
        public void SelectFolder(FolderDisplayModel folder)
        {
            if (folder == null) return;

            if (folder.IsSelected)
            {
                SelectedFolders.Add(folder);
            }
            else
            {
                SelectedFolders.Remove(folder);
            }
            HasSelectedFolders = SelectedFolders.Count > 0;
        }

        [RelayCommand]
        public void SelectAll()
        {
            _errorHandlingService.SafeExecute(
                () =>
                {
                    SelectedFolders.Clear();
                    foreach (var folder in Folders)
                    {
                        folder.IsSelected = true;
                        SelectedFolders.Add(folder);
                    }
                    HasSelectedFolders = SelectedFolders.Count > 0;
                },
                "Selecting all tracks",
                ErrorSeverity.NonCritical,
                false);
        }

        [RelayCommand]
        public void ClearSelection()
        {
            _errorHandlingService.SafeExecute(
                () =>
                {
                    foreach (var folder in Folders)
                    {
                        folder.IsSelected = false;
                    }
                    SelectedFolders.Clear();
                    HasSelectedFolders = SelectedFolders.Count > 0;
                },
                "Clearing folder selection",
                ErrorSeverity.NonCritical,
                false);
        }

        [RelayCommand]
        public async Task PlayFolderFromHere(FolderDisplayModel selectedFolder)
        {
            await _errorHandlingService.SafeExecuteAsync(
                async () =>
                {
                    if (selectedFolder == null) return;

                    var allFolderTracks = new List<TrackDisplayModel>();
                    var startPlayingFromIndex = 0;
                    var tracksAdded = 0;

                    // Get sorted list of all folders
                    var sortedFolders = GetSortedAllFolders();

                    foreach (var folder in sortedFolders)
                    {
                        // Get tracks for this folder and sort them by Title
                        var tracks = (await _folderDisplayService.GetFolderTracksAsync(folder.FolderPath))
                            .OrderBy(t => t.Title)
                            .ToList();

                        if (folder.FolderPath == selectedFolder.FolderPath)
                        {
                            startPlayingFromIndex = tracksAdded;
                        }

                        allFolderTracks.AddRange(tracks);
                        tracksAdded += tracks.Count;
                    }

                    if (allFolderTracks.Count < 1) return;

                    var startTrack = allFolderTracks[startPlayingFromIndex];
                    _trackQueueViewModel.PlayThisTrack(startTrack, new ObservableCollection<TrackDisplayModel>(allFolderTracks));
                },
                "Playing tracks from selected folder",
                ErrorSeverity.Playback,
                true);
        }


        [RelayCommand]
        public async Task PlayFolderTracks(FolderDisplayModel folder)
        {
            if (folder == null) return;

            var tracks = await _folderDisplayService.GetFolderTracksAsync(folder.FolderPath);
            if (tracks.Count > 0)
            {
                _trackQueueViewModel.PlayThisTrack(tracks.First(), new ObservableCollection<TrackDisplayModel>(tracks));
            }
        }

        [RelayCommand]
        public async Task AddFolderTracksToNext(FolderDisplayModel folder)
        {
            var tracks = await GetTracksToAdd(folder);

            if (tracks != null && tracks.Count > 0)
            {
                _trackQueueViewModel.AddToPlayNext(new ObservableCollection<TrackDisplayModel>(tracks));
            }

            ClearSelection();
        }

        [RelayCommand]
        public async Task AddFolderTracksToQueue(FolderDisplayModel folder)
        {
            var tracks = await GetTracksToAdd(folder);

            if (tracks != null && tracks.Count > 0)
            {
                _trackQueueViewModel.AddTrackToQueue(new ObservableCollection<TrackDisplayModel>(tracks));
            }

            ClearSelection();
        }

        /// <summary>
        /// Helper that returns the tracks to be added in Play next and Add to Queue methods
        /// </summary>
        public async Task<List<TrackDisplayModel>> GetTracksToAdd(FolderDisplayModel folder)
        {
            var foldersList = SelectedFolders.Count > 0
                ? SelectedFolders
                : new ObservableCollection<FolderDisplayModel>();

            if (foldersList.Count < 1 && folder != null)
            {
                foldersList.Add(folder);
            }

            var tracks = new List<TrackDisplayModel>();

            foreach (var folderToAdd in foldersList)
            {
                var folderTracks = await _folderDisplayService.GetFolderTracksAsync(folderToAdd.FolderPath);

                if (folderTracks.Count > 0)
                    tracks.AddRange(folderTracks);
            }

            return tracks;
        }

        public async Task<List<TrackDisplayModel>> GetSelectedFolderTracks(string folderPath)
        {
            return await _errorHandlingService.SafeExecuteAsync(
                async () =>
                {
                    var selectedFolder = SelectedFolders;
                    if (selectedFolder.Count <= 1)
                    {
                        return await _folderDisplayService.GetFolderTracksAsync(folderPath);
                    }

                    var trackTasks = selectedFolder.Select(folder =>
                        _folderDisplayService.GetFolderTracksAsync(folder.FolderPath));

                    var allTrackLists = await Task.WhenAll(trackTasks);
                    return allTrackLists.SelectMany(tracks => tracks).ToList();
                },
                "Getting selected folder tracks",
                new List<TrackDisplayModel>(),
                ErrorSeverity.NonCritical,
                false);
        }

        [RelayCommand]
        public async Task ShowPlaylistSelectionDialog(FolderDisplayModel folder)
        {
            await _errorHandlingService.SafeExecuteAsync(
                async () =>
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        var mainWindow = desktop.MainWindow;
                        if (mainWindow == null || !mainWindow.IsVisible) return;

                        var selectedTracks = await GetSelectedFolderTracks(folder.FolderPath);

                        var dialog = new PlaylistSelectionDialog();
                        dialog.Initialize(_playlistViewModel, null, selectedTracks);
                        await dialog.ShowDialog(mainWindow);

                        ClearSelection();
                    }
                },
                "Showing playlist selection dialog for folder tracks",
                ErrorSeverity.NonCritical,
                true);
        }
    }
}