﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using NAudio.Wave;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Timers;
using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Features.Library.Services;
using OmegaPlayer.Infrastructure.Data.Repositories;
using OmegaPlayer.Core.ViewModels;
using System.Threading.Tasks;
using OmegaPlayer.Core.Navigation.Services;
using Avalonia;
using Avalonia.Controls;
using OmegaPlayer.Features.Playback.Views;
using Avalonia.Controls.ApplicationLifetimes;
using OmegaPlayer.Infrastructure.Services;
using OmegaPlayer.Features.Playback.Services;
using OmegaPlayer.Core.Services;
using OmegaPlayer.Features.Shell.Views;
using OmegaPlayer.Core.Interfaces;
using OmegaPlayer.Core.Enums;

namespace OmegaPlayer.Features.Playback.ViewModels
{
    public partial class TrackControlViewModel : ViewModelBase
    {

        private readonly TrackDisplayService _trackDService;
        private readonly TrackQueueViewModel _trackQueueViewModel;
        private readonly AllTracksRepository _allTracksRepository;
        private readonly ArtistDisplayService _artistDisplayService;
        private readonly AlbumDisplayService _albumDisplayService;
        private readonly AudioMonitorService _audioMonitorService;
        private readonly StateManagerService _stateManager;
        private readonly TrackStatsService _trackStatsService;
        private readonly LocalizationService _localizationService;
        private readonly INavigationService _navigationService;
        private readonly IMessenger _messenger;
        private readonly IErrorHandlingService _errorHandlingService;

        public List<TrackDisplayModel> AllTracks { get; set; }

        // Instance used in sleeptimerdialog
        public static TrackControlViewModel Instance { get; private set; }
        public bool IsSleepTimerActive => SleepTimerManager.Instance.IsTimerActive;
        public DateTime? SleepTimerEndTime => SleepTimerManager.Instance.EndTime;


        private IWavePlayer _waveOut;
        private AudioFileReader _audioFileReader;
        [ObservableProperty]
        private float _trackVolume;

        public TrackControlViewModel(
            TrackDisplayService trackDService,
            TrackQueueViewModel trackQueueViewModel,
            AllTracksRepository allTracksRepository,
            ArtistDisplayService artistDisplayService,
            AlbumDisplayService albumDisplayService,
            AudioMonitorService audioMonitorService,
            StateManagerService stateManagerService,
            TrackStatsService trackStatsService,
            LocalizationService localizationService,
            INavigationService navigationService,
            IMessenger messenger,
            IErrorHandlingService errorHandlingService)
        {
            _trackDService = trackDService;
            _trackQueueViewModel = trackQueueViewModel;
            _allTracksRepository = allTracksRepository;
            _artistDisplayService = artistDisplayService;
            _albumDisplayService = albumDisplayService;
            _audioMonitorService = audioMonitorService;
            _stateManager = stateManagerService;
            _trackStatsService = trackStatsService;
            _localizationService = localizationService;
            _navigationService = navigationService;
            _messenger = messenger;
            _errorHandlingService = errorHandlingService;

            Instance = this;

            AllTracks = _allTracksRepository.AllTracks;
            LoadTrackQueue();
            InitializeWaveOut(); // Ensure _waveOut is initialized

            _timer = new Timer(250); // Initialize but do not start the timer
            _timer.Elapsed += TimerElapsed;

            UpdateMainIcons();

            messenger.Register<TrackQueueUpdateMessage>(this, async (r, m) =>
            {
                if (m.CurrentTrack != null)
                {
                    CurrentlyPlayingTrack = m.CurrentTrack;
                    if (!m.IsShuffleOperation)
                    {
                        PlaySelectedTracks(CurrentlyPlayingTrack);
                    }
                    else
                    {
                        await UpdateTrackInfo();
                    }
                }
            });

            SleepTimerManager.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SleepTimerManager.IsTimerActive))
                {
                    OnPropertyChanged(nameof(IsSleepTimerActive));
                    UpdateSleepIcon();

                    if (!SleepTimerManager.Instance.IsTimerActive && SleepTimerManager.Instance.TimerExpiredNaturally && !_finishLastSongOnSleep)
                    {
                        PauseTrack();
                    }
                }
            };

            _messenger.Register<AudioPauseMessage>(this, (r, m) =>
            {
                if (m.ShouldPause)
                {
                    PauseTrack();
                }
                else if (m.ShouldResume && IsPlaying != PlaybackState.Playing)
                {
                    PlayTrack();
                }
            });

            // Subscribe to volume changes
            this.PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == nameof(TrackVolume))
                {
                    await _stateManager.SaveVolumeState(TrackVolume);
                }
            };
        }

        private async Task LoadTrackQueue()
        {
            try
            {
                await _trackQueueViewModel.LoadLastPlayedQueue();
                await UpdateFromQueueState();

                // Set the last played track and start playing
                var currentTrack = _trackQueueViewModel.CurrentTrack;
                if (currentTrack != null)
                {
                    // Update the track information and play the current track
                    await UpdateTrackInfo();
                }
            }
            catch
            {
                ShowMessageBox("Error when trying to fetch all tracks");
            }
        }

        [ObservableProperty]
        public PlaybackState _isPlaying = PlaybackState.Stopped;

        [ObservableProperty]
        private Thickness _playPauseIconMargin = new Thickness(0);

        private readonly Timer _timer;
        private bool _isSeeking = false;

        [ObservableProperty]
        private TimeSpan _trackDuration; // Total duration of the track
        [ObservableProperty]
        private TimeSpan _trackPosition; // Current playback position

        [ObservableProperty]
        private string _currentTitle;

        [ObservableProperty]
        private TrackDisplayModel _currentlyPlayingTrack;

        [ObservableProperty]
        private List<Artists> _currentArtists;

        [ObservableProperty]
        private string _currentAlbumTitle;

        [ObservableProperty]
        private Bitmap _currentTrackImage;

        [ObservableProperty]
        private object _shuffleIcon;

        [ObservableProperty]
        private object _repeatIcon;

        [ObservableProperty]
        private object _playPauseIcon;

        private bool _finishLastSongOnSleep;

        [ObservableProperty]

        private object _sleepIcon;

        [ObservableProperty]
        private object _volumeIcon;

        private float _previousVolume = 0.5f;
        private bool _isMuted = false;

        #region Button ToolTips

        [ObservableProperty]
        private string _playPauseToolTip;

        [ObservableProperty]
        private string _shuffleToolTip;

        [ObservableProperty]
        private string _repeatToolTip;

        [ObservableProperty]
        private string _favoriteToolTip;

        [ObservableProperty]
        private string _sleepToolTip;

        [ObservableProperty]
        private string _muteToolTip;

        #endregion

        private void TimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_audioFileReader != null && _isSeeking == false)
            {
                TrackPosition = _audioFileReader.CurrentTime;
            }
        }
        public void Seek(double newPosition)
        {
            if (_audioFileReader != null && newPosition >= 0 && newPosition <= TrackDuration.TotalSeconds)
            {
                _isSeeking = true;
                TrackPosition = TimeSpan.FromSeconds(newPosition);
            }
        }
        public void StopTimer()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }
        }

        public void StopSeeking()
        {
            _isSeeking = false;
            if (_audioFileReader == null) return;
            _audioFileReader.CurrentTime = TrackPosition.TotalSeconds <= 0 ? TimeSpan.Zero : TrackPosition;

        }
        public void ChangeVolume(double newVolume)
        {
            // Volume should be between 0.0f (mute) and 1.0f (max)
            if (newVolume < 0) return;
            TrackVolume = (float)newVolume;
            SetVolume();

        }

        public async void SetVolume()
        {
            // Volume should be between 0.0f (mute) and 1.0f (max)
            if (TrackVolume < 0 || _audioFileReader == null) return;

            _audioFileReader.Volume = TrackVolume;
        }

        partial void OnTrackVolumeChanged(float value)
        {
            // Only update the mute state if the change wasn't triggered by the ToggleMute command
            if (value > 0 && _isMuted)
            {
                _isMuted = false;
            }
            // Ensure volume is properly applied to audio system
            SetVolume();
            UpdateVolumeIcon();
        }

        [RelayCommand]
        public void ToggleMute()
        {
            if (_isMuted)
            {
                // Restore volume
                _isMuted = false;
                TrackVolume = _previousVolume > 0 ? _previousVolume : 0.5f; // Default to 50% if previous was 0
            }
            else
            {
                // Mute volume
                _previousVolume = TrackVolume;
                _isMuted = true;
                TrackVolume = 0;
            }

            UpdateVolumeIcon();
            SetVolume();
        }

        private void UpdateVolumeIcon()
        {
            if (Application.Current == null) return;

            if (_isMuted || TrackVolume <= 0)
            {
                VolumeIcon = Application.Current.FindResource("MuteIcon");
                MuteToolTip = _localizationService["UnmuteToolTip"];
                return;
            }
            else if (TrackVolume < 0.15f)
            {
                VolumeIcon = Application.Current.FindResource("VeryLowAudioIcon");
            }
            else if (TrackVolume < 0.4f)
            {
                VolumeIcon = Application.Current.FindResource("LowAudioIcon");
            }
            else if (TrackVolume < 0.7f)
            {
                VolumeIcon = Application.Current.FindResource("MediumAudioIcon");
            }
            else
            {
                VolumeIcon = Application.Current.FindResource("HighAudioIcon");
            }

            MuteToolTip = _localizationService["MuteToolTip"];
        }


        [RelayCommand]
        public async Task PlayOrPause()
        {
            var currentTrack = GetCurrentTrack();
            if (_audioFileReader == null && currentTrack == null) { return; }

            if (IsPlaying != PlaybackState.Playing)
            {
                if (_audioFileReader == null && currentTrack != null)
                {
                    ReadyTrack(currentTrack);
                    await UpdateTrackInfo();
                }
                PlayTrack();
            }
            else
            {
                PauseTrack();
            }

        }

        public void UpdateDynamicPause(bool enabled)
        {
            _audioMonitorService.EnableDynamicPause(enabled);
        }

        public void PauseTrack()
        {
            _waveOut.Pause();
            IsPlaying = _waveOut.PlaybackState;
            UpdatePlayPauseIcon();
            _timer.Stop();

        }
        public void PlayTrack()
        {
            _waveOut.Play();
            IsPlaying = _waveOut.PlaybackState;
            UpdatePlayPauseIcon();
            _timer.Start();

        }

        private void UpdatePlayPauseIcon()
        {
            if (Application.Current == null) return;
            var isPlayingState = IsPlaying == PlaybackState.Playing;

            PlayPauseIcon = isPlayingState ?
                Application.Current.FindResource("PauseIcon") :
                Application.Current.FindResource("PlayIcon");

            PlayPauseIconMargin = isPlayingState ?
                new Thickness(0, 0, 1, 0) :
                new Thickness(5, 0, 0, 0);

            PlayPauseToolTip = isPlayingState ?
                _localizationService["Pause"] :
                _localizationService["Play"];
        }

        public void ReadyTrack(TrackDisplayModel track)
        {
            if (track == null) return;

            try
            {
                // Verify file exists
                if (!System.IO.File.Exists(track.FilePath))
                {
                    _errorHandlingService.LogError(
                        ErrorSeverity.Playback,
                        _localizationService["TrackFileNotFound"],
                        $"File not found: {track.FilePath}");

                    // Skip to next track if file not found
                    Task.Run(async () => await PlayNextTrack());
                    return;
                }

                InitializeWaveOut();// Stop any previous playback and Initialize playback device

                _audioFileReader = new AudioFileReader(track.FilePath); // FilePath is the path to the audio file
                SetVolume();

                // Hook up the audio file to the player
                _waveOut.Init(_audioFileReader);
            }
            catch (Exception ex)
            {
                _errorHandlingService.LogError(
                    ErrorSeverity.Playback,
                    _localizationService["TrackLoadError"],
                    $"Error loading track: {track.Title}",
                    ex);

                // Skip to next track if there's an error
                Task.Run(async () => await PlayNextTrack());
            }
        }

        public async Task PlayCurrentTrack(TrackDisplayModel track, ObservableCollection<TrackDisplayModel> allTracks)
        {
            if (track == null || allTracks == null) return;

            await _errorHandlingService.SafeExecuteAsync(async () =>
            {
                // Set the new track as currently playing
                _trackQueueViewModel.PlayThisTrack(track, allTracks);

                ReadyTrack(track);
                PlayTrack();
                await UpdateTrackInfo();
            },
            _localizationService["ErrorPlayingTrack"],
            ErrorSeverity.Playback);
        }


        public async Task PlaySelectedTracks(TrackDisplayModel track)
        {
            if (track == null) return;

            await _errorHandlingService.SafeExecuteAsync(async () =>
            {
                StopPlayback();
                ReadyTrack(track);
                PlayTrack();
                await UpdateTrackInfo();
            },
            _localizationService["ErrorPlayingSelectedTrack"],
            ErrorSeverity.Playback);
        }


        private void InitializeWaveOut()
        {
            // Initialize playback device (e.g., WaveOutEvent for default output)
            StopPlayback();
            _waveOut = new WaveOutEvent();
            _waveOut.PlaybackStopped += HandlePlaybackStopped;
        }

        public void StopPlayback()
        {
            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                IsPlaying = _waveOut.PlaybackState;
                _waveOut = null;

                UpdatePlayPauseIcon();
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }
        }

        private void HandlePlaybackStopped(object sender, StoppedEventArgs e)
        {
            try
            {
                if (_audioFileReader == null) return;

                // Check if an error caused the playback to stop
                if (e.Exception != null)
                {
                    _errorHandlingService.LogError(
                        ErrorSeverity.Playback,
                        _localizationService["TrackPlaybackError"],
                        $"Error playing track: {CurrentTitle}",
                        e.Exception);

                    // Skip to next track automatically
                    Task.Run(async () => await PlayNextTrack());
                    return;
                }

                TimeSpan timeRemaining = _audioFileReader.TotalTime - _audioFileReader.CurrentTime;
                double secondsRemaining = timeRemaining.TotalSeconds;
                if (secondsRemaining < 1)
                {
                    if (_finishLastSongOnSleep && !SleepTimerManager.Instance.TimerExpiredNaturally)
                    {
                        StopSleepTimer();
                        PauseTrack();
                        return;
                    }
                    if (_trackQueueViewModel.RepeatMode == RepeatMode.One)
                    {
                        _audioFileReader.CurrentTime = TimeSpan.Zero;
                        PlayTrack();
                        _trackQueueViewModel.IncrementPlayCount();
                        return;
                    }
                    PlayNextTrack();
                }

                _trackQueueViewModel.UpdateDurations();
            }
            catch (Exception ex)
            {
                _errorHandlingService.LogError(
                    ErrorSeverity.Playback,
                    _localizationService["PlaybackStoppedError"],
                    "Error handling playback stopped event",
                    ex);

                // Attempt to continue playback by moving to the next track
                Task.Run(async () => await PlayNextTrack());
            }
        }

        private ObservableCollection<TrackDisplayModel> GetCurrentQueue()
        {
            return new ObservableCollection<TrackDisplayModel>(_trackQueueViewModel.NowPlayingQueue);
        }
        private TrackDisplayModel GetCurrentTrack()
        {
            return _trackQueueViewModel.CurrentTrack;
        }

        [RelayCommand]
        public void AdvanceBySeconds()// "int seconds" use variable for customable seconds skip, for now fixed to 5
        {
            //_audioFileReader.CurrentTime = _audioFileReader.TotalTime.Subtract(TimeSpan.FromSeconds(3)); // for testing, advances to 3 sec before the end of the track
            if (_audioFileReader != null)
            {
                var newPosition = _audioFileReader.CurrentTime.Add(TimeSpan.FromSeconds(5));
                _audioFileReader.CurrentTime = newPosition < _audioFileReader.TotalTime ? newPosition : _audioFileReader.TotalTime;
                TrackPosition = _audioFileReader.CurrentTime;
            }
        }
        [RelayCommand]
        public void RegressBySeconds() // "int seconds" use variable for customable seconds skip, for now fixed to 5
        {
            if (_audioFileReader != null)
            {
                var newPosition = _audioFileReader.CurrentTime.Subtract(TimeSpan.FromSeconds(5));
                _audioFileReader.CurrentTime = newPosition > TimeSpan.Zero ? newPosition : TimeSpan.Zero;
                TrackPosition = _audioFileReader.CurrentTime;
            }
        }


        [RelayCommand]
        private void ShowNowPlaying()
        {
            var currentQueue = _trackQueueViewModel.NowPlayingQueue.ToList();
            _navigationService.NavigateToNowPlaying(
                _trackQueueViewModel.CurrentTrack,
                currentQueue,
                currentQueue.IndexOf(_trackQueueViewModel.CurrentTrack)
            );

        }

        [RelayCommand]
        public void OpenImage()
        {
        }
        [RelayCommand]
        public async Task OpenArtist(Artists artist)
        {
            var artistDisplay = await _artistDisplayService.GetArtistByIdAsync(artist.ArtistID);
            if (artistDisplay != null)
            {
                _navigationService.NavigateToArtistDetails(artistDisplay);
            }
        }
        [RelayCommand]
        public async Task OpenAlbum()
        {
            var albumDisplay = await _albumDisplayService.GetAlbumByIdAsync(CurrentlyPlayingTrack.AlbumID);
            if (albumDisplay != null)
            {
                _navigationService.NavigateToAlbumDetails(albumDisplay);
            }
        }
        [RelayCommand]
        public async void Shuffle()
        {
            _trackQueueViewModel.ToggleShuffle();
            UpdateShuffleIcon();

            await _trackQueueViewModel.SaveCurrentQueueState().ConfigureAwait(false);
        }
        private void UpdateShuffleIcon()
        {
            if (Application.Current == null) return;

            ShuffleIcon = _trackQueueViewModel.IsShuffled ?
                Application.Current.FindResource("ShuffleOnIcon") :
                Application.Current.FindResource("ShuffleOffIcon");

            ShuffleToolTip = _trackQueueViewModel.IsShuffled ?
                _localizationService["ShuffleOnToolTip"] :
                _localizationService["ShuffleOffToolTip"];
        }

        [RelayCommand]
        public async void ToggleRepeat()
        {
            _trackQueueViewModel.ToggleRepeatMode();
            UpdateRepeatIcon();

            await _trackQueueViewModel.SaveCurrentQueueState().ConfigureAwait(false);
        }

        private void UpdateRepeatIcon()
        {
            if (Application.Current == null) return;

            switch (_trackQueueViewModel.RepeatMode)
            {
                case (RepeatMode.None):
                    RepeatIcon = Application.Current.FindResource("RepeatNoneIcon");
                    RepeatToolTip = _localizationService["RepeatNoneToolTip"];
                    break;
                case (RepeatMode.All):
                    RepeatIcon = Application.Current.FindResource("RepeatAllIcon");
                    RepeatToolTip = _localizationService["RepeatAllToolTip"];
                    break;
                case (RepeatMode.One):
                    RepeatIcon = Application.Current.FindResource("RepeatOneIcon");
                    RepeatToolTip = _localizationService["RepeatOneToolTip"];
                    break;
                default:
                    RepeatIcon = Application.Current.FindResource("RepeatNoneIcon");
                    RepeatToolTip = _localizationService["RepeatNoneToolTip"];
                    break;

            }
        }

        [RelayCommand]
        public async Task PlayNextTrack()
        {
            var nextTrackIndex = _trackQueueViewModel.GetNextTrack();
            if (nextTrackIndex >= 0)
            {
                var nextTrack = _trackQueueViewModel.NowPlayingQueue[nextTrackIndex];
                _trackQueueViewModel.UpdateCurrentTrackIndex(nextTrackIndex);
                StopPlayback();
                ReadyTrack(nextTrack);
                PlayTrack();
                await UpdateTrackInfo();
            }
            else if (_audioFileReader != null)
            {
                // No next track and no repeat - stop playback
                StopPlayback();
            }
        }

        [RelayCommand]
        public async Task PlayPreviousTrack()
        {
            // Check if the current track is playing and the current time is more than 5 seconds
            if (_audioFileReader == null) { return; }

            if (_audioFileReader.CurrentTime.TotalSeconds > 5)
            {
                _audioFileReader.CurrentTime = TimeSpan.Zero;
            }
            else
            {
                var previousTrackIndex = _trackQueueViewModel.GetPreviousTrack();
                if (previousTrackIndex >= 0)
                {
                    var previousTrack = _trackQueueViewModel.NowPlayingQueue[previousTrackIndex];
                    _trackQueueViewModel.UpdateCurrentTrackIndex(previousTrackIndex);
                    StopPlayback();
                    ReadyTrack(previousTrack);
                    PlayTrack();
                    await UpdateTrackInfo();
                }
            }
        }

        [RelayCommand]
        private async Task ToggleCurrentTrackLike()
        {
            if (CurrentlyPlayingTrack == null) return;

            CurrentlyPlayingTrack.IsLiked = !CurrentlyPlayingTrack.IsLiked;

            UpdateFavoriteIcon();

            await _trackStatsService.UpdateTrackLike(
                CurrentlyPlayingTrack.TrackID,
                CurrentlyPlayingTrack.IsLiked);
        }

        [RelayCommand]
        public async Task SleepMode()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                if (mainWindow == null || !mainWindow.IsVisible) return;

                var dialog = new SleepTimerDialog
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var result = await dialog.ShowDialog<(int minutes, bool finishLastSong)>(mainWindow);

                if (result.minutes > 0)
                {
                    StartSleepTimer(result.minutes, result.finishLastSong);
                }
                else if (result.minutes == -1) // Stop was pressed
                {
                    StopSleepTimer();
                }
            }
        }

        [RelayCommand]
        public async Task ShowTrackProperties()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                if (mainWindow == null || !mainWindow.IsVisible) return;

                var dialog = new TrackPropertiesDialog();
                dialog.Initialize(GetCurrentTrack());
                await dialog.ShowDialog(mainWindow);
            }
        }

        private void StartSleepTimer(int minutes, bool finishLastSong)
        {
            _finishLastSongOnSleep = finishLastSong;
            SleepTimerManager.Instance.StartTimer(minutes, finishLastSong);
            UpdateSleepIcon();
        }

        private void StopSleepTimer()
        {
            SleepTimerManager.Instance.StopTimer();
            _finishLastSongOnSleep = false;
            UpdateSleepIcon();
        }

        private void UpdateSleepIcon()
        {
            if (Application.Current == null) return;

            SleepIcon = IsSleepTimerActive ?
                Application.Current.FindResource("SleepOnIcon") :
                Application.Current.FindResource("SleepOffIcon");

            SleepToolTip = IsSleepTimerActive ?
                _localizationService["SleepOnToolTip"] :
                _localizationService["SleepOffToolTip"];

        }
        private void UpdateFavoriteIcon()
        {
            if (Application.Current == null || CurrentlyPlayingTrack == null) return;

            CurrentlyPlayingTrack.LikeIcon = Application.Current?.FindResource(
                CurrentlyPlayingTrack.IsLiked ? "LikeOnIcon" : "LikeOffIcon");

            FavoriteToolTip = CurrentlyPlayingTrack.IsLiked ?
                _localizationService["FavoriteOnToolTip"] :
                _localizationService["FavoriteOffToolTip"];

        }

        public void UpdateMainIcons()
        {
            UpdatePlayPauseIcon();
            UpdateShuffleIcon();
            UpdateRepeatIcon();
            UpdateSleepIcon();
            UpdateVolumeIcon();
            UpdateFavoriteIcon();
        }

        private async Task UpdateTrackInfoWithIcons()
        {
            await UpdateTrackInfo();
            UpdateMainIcons();
        }

        public async Task UpdateFromQueueState()
        {
            var currentTrack = GetCurrentTrack();
            if (currentTrack != null)
            {
                CurrentlyPlayingTrack = currentTrack;
                await UpdateTrackInfoWithIcons();
            }
        }

        public async Task UpdateTrackInfo()
        {
            var track = GetCurrentTrack();

            if (track == null)
            {
                // clear values
                CurrentTitle = String.Empty;
                CurrentArtists = new List<Artists>();
                CurrentAlbumTitle = String.Empty;
                CurrentTrackImage = null;
                TrackDuration = TimeSpan.Zero;
                TrackPosition = TimeSpan.Zero;
                return;
            }

            CurrentlyPlayingTrack = track;

            if (_audioFileReader == null)
            {
                ReadyTrack(track);
            }

            if (_navigationService.IsCurrentlyShowingNowPlaying())
            {
                ShowNowPlaying();
            }

            CurrentTitle = track.Title;
            CurrentArtists = track.Artists;
            CurrentAlbumTitle = track.AlbumTitle;

            track.Artists.Last().IsLastArtist = false;// Hides the Comma of the last Track
            await _trackDService.LoadMediumQualityThumbnailAsync(track);// Load track Thumbnail

            CurrentTrackImage = track.Thumbnail;
            TrackDuration = _audioFileReader.TotalTime;
            TrackPosition = TimeSpan.Zero;

            await _trackQueueViewModel.SaveCurrentQueueState().ConfigureAwait(false);
        }



        private async void ShowMessageBox(string message)
        {
            var messageBox = MessageBoxManager.GetMessageBoxStandard("DI Resolution Result", message, ButtonEnum.Ok, Icon.Info);
            await messageBox.ShowWindowAsync(); // shows custom messages
        }
    }
    public class NowPlayingInfo
    {
        public TrackDisplayModel CurrentTrack { get; set; }
        public Artists Artist { get; set; }
        public int AlbumID { get; set; }
        public List<TrackDisplayModel> AllTracks { get; set; }
        public int CurrentTrackIndex { get; set; }
    }


}
