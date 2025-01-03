﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using System;
using Microsoft.Extensions.DependencyInjection;
using OmegaPlayer.Features.Library.Services;
using OmegaPlayer.Features.Home.ViewModels;
using OmegaPlayer.Features.Library.ViewModels;
using OmegaPlayer.Core.ViewModels;
using OmegaPlayer.Features.Playback.ViewModels;
using System.Threading.Tasks;
using OmegaPlayer.Core.Navigation.Services;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;

namespace OmegaPlayer.Features.Shell.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly DirectoryScannerService _directoryScannerService;
        private readonly DirectoriesService _directoryService;
        private readonly TrackSortService _trackSortService;
        private readonly INavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IMessenger _messenger;

        [ObservableProperty]
        private bool _isExpanded = true;

        [ObservableProperty]
        private ViewType _currentViewType = ViewType.Card;

        [ObservableProperty]
        private bool _showSortingControls;

        [ObservableProperty]
        private bool _showBackButton;

        [ObservableProperty]
        private SortType _selectedSortType = SortType.Name;

        [ObservableProperty]
        private SortDirection _sortDirection = SortDirection.Ascending;
        public ObservableCollection<string> AvailableSortTypes { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private bool _showViewTypeButtons = false;

        private ViewModelBase _currentPage;
        public ViewModelBase CurrentPage
        {
            get => _currentPage;
            set
            {
                SetProperty(ref _currentPage, value);
                // Show sorting controls only for views that display track listings
                ShowSortingControls = value is LibraryViewModel;
            }
        }

        public TrackControlViewModel TrackControlViewModel { get; }

        public MainViewModel(
            DirectoryScannerService directoryScannerService,
            DirectoriesService directoryService,
            TrackControlViewModel trackControlViewModel,
            TrackSortService trackSortService,
            IServiceProvider serviceProvider,
            INavigationService navigationService, 
            IMessenger messenger)
        {
            _directoryScannerService = directoryScannerService;
            _directoryService = directoryService;
            TrackControlViewModel = trackControlViewModel;
            _trackSortService = trackSortService;
            _serviceProvider = serviceProvider;
            _navigationService = navigationService; 
            _messenger = messenger;

            // Set initial page
            CurrentPage = _serviceProvider.GetRequiredService<HomeViewModel>();

            StartBackgroundScan();

            navigationService.NavigationRequested += async (s, e) => await NavigateToDetails(e.Type, e.Data);
        }

        [RelayCommand]
        private async Task Navigate(string destination)
        {
            //clear selected items in their respective views
            Type pageType = CurrentPage?.GetType();
            if (pageType != null)
            {
                var clearMethod = pageType.GetMethod("ClearSelection") ?? pageType.GetMethod("DeselectAllTracks");
                clearMethod?.Invoke(CurrentPage, null);
            }

            // Clear current view state in navigation service
            _navigationService.ClearCurrentView();
            ViewModelBase viewModel;
            ContentType contentType;
            switch (destination)
            {
                case "Home":
                    viewModel = _serviceProvider.GetRequiredService<HomeViewModel>();
                    contentType = ContentType.Home;
                    break;
                case "Library":
                    viewModel = _serviceProvider.GetRequiredService<LibraryViewModel>();
                    contentType = ContentType.Library;
                    if (CurrentPage is LibraryViewModel libraryVM)
                        await libraryVM.NavigateBack();
                    else
                        await ((LibraryViewModel)viewModel).Initialize(false);
                    break;
                case "Artists":
                    viewModel = _serviceProvider.GetRequiredService<ArtistViewModel>();
                    contentType = ContentType.Artist;
                    break;
                case "Albums":
                    viewModel = _serviceProvider.GetRequiredService<AlbumViewModel>();
                    contentType = ContentType.Album;
                    break;
                case "Playlists":
                    viewModel = _serviceProvider.GetRequiredService<PlaylistViewModel>();
                    contentType = ContentType.Playlist;
                    break;
                case "Genres":
                    viewModel = _serviceProvider.GetRequiredService<GenreViewModel>();
                    contentType = ContentType.Genre;
                    break;
                case "Folders":
                    viewModel = _serviceProvider.GetRequiredService<FolderViewModel>();
                    contentType = ContentType.Folder;
                    break;
                default:
                    throw new ArgumentException($"Unknown destination: {destination}");
            }
            CurrentPage = viewModel;
            UpdateAvailableSortTypes(contentType);
            ShowViewTypeButtons = CurrentPage is LibraryViewModel;
        }

        public async Task NavigateBackToLibrary()
        {
            await Navigate("Library");
        }

        [RelayCommand]
        private void ToggleNavigation()
        {
            IsExpanded = !IsExpanded;
        }

        public async Task NavigateToDetails(ContentType type, object data)
        {
            var detailsViewModel = _serviceProvider.GetRequiredService<LibraryViewModel>();
            await detailsViewModel.Initialize(true, type, data); // true since it's the details page
            CurrentPage = detailsViewModel;
            ShowViewTypeButtons = CurrentPage is LibraryViewModel;

            // use hardcoded library content type to have the same sort types as library or else will have default sort type
            UpdateAvailableSortTypes(ContentType.Library);
        }

        [RelayCommand]
        private void SetViewType(string viewType)
        {
            ViewType parsedViewType = Enum.Parse<ViewType>(viewType, true);
            CurrentViewType = parsedViewType;

            // Update the current page's view type
            if (CurrentPage is LibraryViewModel libraryVM)
            {
                libraryVM.CurrentViewType = parsedViewType;
            }
        }

        private void UpdateAvailableSortTypes(ContentType contentType)
        {
            AvailableSortTypes.Clear();

            var types = contentType switch
            {
                ContentType.Library => ["Name", "Artist", "Album", "Duration", "Genre", "Release Date"],
                ContentType.NowPlaying or ContentType.Home => Array.Empty<string>(),
                _ => ["Name", "Duration"]
            };

            foreach (var type in types)
            {
                AvailableSortTypes.Add(type);
            }

        }


        partial void OnSelectedSortTypeChanged(SortType value)
        {
            UpdateSorting();
        }

        partial void OnSortDirectionChanged(SortDirection value)
        {
            UpdateSorting();
        }

        private void UpdateSorting()
        {
            _messenger.Send(new SortUpdateMessage(SelectedSortType, SortDirection));
        }

        [RelayCommand]
        public void SetSortDirection(string direction)
        {
            SortDirection = direction.ToUpper() == "A-Z" ?
                SortDirection.Ascending :
                SortDirection.Descending;
        }

        [RelayCommand]
        public void SetSortType(string sortType)
        {
            SelectedSortType = sortType.ToLower() switch
            {
                "name" => SortType.Name,
                "artist" => SortType.Artist,
                "album" => SortType.Album,
                "duration" => SortType.Duration,
                "genre" => SortType.Genre,
                "releasedate" => SortType.ReleaseDate,
                _ => SortType.Name
            };
        }


        public async void StartBackgroundScan()
        {
            var directories = await _directoryService.GetAllDirectories();
            await Task.Run(() => _directoryScannerService.ScanDirectoriesAsync(directories));
        }
    }
}