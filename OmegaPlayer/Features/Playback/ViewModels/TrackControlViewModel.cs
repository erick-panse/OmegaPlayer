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
using System.Diagnostics;
using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Infrastructure.Services.Config;
using OmegaPlayer.Features.Library.Services;
using OmegaPlayer.Infrastructure.Data.Repositories;
using OmegaPlayer.Core.ViewModels;
using OmegaPlayer.Features.Shell.ViewModels;
using System.Threading.Tasks;
using OmegaPlayer.Core.Navigation.Services;
using Avalonia;
using Avalonia.Controls;

namespace OmegaPlayer.Features.Playback.ViewModels
{
    public partial class TrackControlViewModel : ViewModelBase
    {

        private readonly TrackDisplayService _trackDService;
        private readonly TrackQueueViewModel _trackQueueViewModel;
        private readonly AllTracksRepository _allTracksRepository;
        private readonly ArtistDisplayService _artistDisplayService;
        private readonly AlbumDisplayService _albumDisplayService;
        private readonly INavigationService _navigationService;
        private readonly IMessenger _messenger;

        public List<TrackDisplayModel> AllTracks { get; set; }

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
            INavigationService navigationService,
            IMessenger messenger)
        {
            _trackDService = trackDService;
            _trackQueueViewModel = trackQueueViewModel;
            _allTracksRepository = allTracksRepository;
            _artistDisplayService = artistDisplayService;
            _albumDisplayService = albumDisplayService;
            _navigationService = navigationService;
            _messenger = messenger;

            AllTracks = _allTracksRepository.AllTracks;
            LoadTrackQueue();
            InitializeWaveOut(); // Ensure _waveOut is initialized

            _timer = new Timer(250); // Initialize but do not start the timer
            _timer.Elapsed += TimerElapsed;

            UpdateShuffleIcon();
            UpdateRepeatIcon();


            messenger.Register<TrackQueueUpdateMessage>(this, (r, m) =>
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
                        UpdateTrackInfo();
                    }
                }
            });
        }

        private async void LoadTrackQueue()
        {
            try
            {
                await _trackQueueViewModel.LoadLastPlayedQueue();

                // Set the last played track and start playing
                var currentTrack = _trackQueueViewModel.CurrentTrack;
                if (currentTrack != null)
                {
                    // Update the track information and play the current track
                    UpdateTrackInfo();
                }
            }
            catch
            {
                ShowMessageBox("Error when trying to fetch all tracks");
            }
        }

        [ObservableProperty]
        public PlaybackState _isPlaying = PlaybackState.Stopped;
        private readonly Timer _timer;
        private bool _isSeeking = false;
        private bool _isDragging = false;

        [ObservableProperty]
        private TimeSpan _trackDuration; // Total duration of the track
        [ObservableProperty]
        private TimeSpan _trackPosition; // Current playback position

        [ObservableProperty]
        private double trackTitleMaxWidth;

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
            if (newVolume < 0 || _audioFileReader == null) return;
            TrackVolume = (float)newVolume;
            SetVolume();
        }

        public void SetVolume()
        {
            // Volume should be between 0.0f (mute) and 1.0f (max)
            if (TrackVolume < 0 || _audioFileReader == null) return;

            _audioFileReader.Volume = TrackVolume;
        }


        [RelayCommand]
        public void PlayOrPause()
        {
            var currentTrack = GetCurrentTrack();
            if (_audioFileReader == null && currentTrack == null) { return; }

            if (IsPlaying != PlaybackState.Playing)
            {
                if (_audioFileReader == null && currentTrack != null)
                {
                    ReadyTrack(currentTrack);
                    UpdateTrackInfo();
                }
                PlayTrack();
            }
            else
            {
                PauseTrack();
            }

        }

        public void PauseTrack()
        {
            _waveOut.Pause();
            IsPlaying = _waveOut.PlaybackState;
            _timer.Stop();

        }
        public void PlayTrack()
        {
            _waveOut.Play();
            IsPlaying = _waveOut.PlaybackState;
            _timer.Start();

        }

        public void ReadyTrack(TrackDisplayModel track)
        {
            if (track == null) { return; }

            InitializeWaveOut();// Stop any previous playback and Initialize playback device

            _audioFileReader = new AudioFileReader(track.FilePath); // FilePath is the path to the audio file
            SetVolume();

            // Hook up the audio file to the player
            _waveOut.Init(_audioFileReader);

        }

        public async void PlayCurrentTrack(TrackDisplayModel track, ObservableCollection<TrackDisplayModel> allTracks)
        {
            if (track == null || allTracks == null) { return; }

            // Set the new track as currently playing
            _trackQueueViewModel.PlayThisTrack(track, allTracks);

            ReadyTrack(track);
            PlayTrack();
            UpdateTrackInfo();
        }

        public async void PlaySelectedTracks(TrackDisplayModel track)
        {
            if (track == null) { return; }

            StopPlayback();
            ReadyTrack(track);
            PlayTrack();
            UpdateTrackInfo();
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
                _waveOut = null;
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }
        }

        private async void HandlePlaybackStopped(object sender, StoppedEventArgs e)
        {
            TimeSpan timeRemaining = _audioFileReader.TotalTime - _audioFileReader.CurrentTime;
            double secondsRemaining = timeRemaining.TotalSeconds;
            if (secondsRemaining < 1)
            {
                if (_trackQueueViewModel.RepeatMode == RepeatMode.One)
                {
                    _audioFileReader.CurrentTime = TimeSpan.Zero;
                    PlayTrack();
                    return;

                }
                PlayNextTrack();
            }

            await _trackQueueViewModel.UpdateDurations();
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
        private async Task ShowNowPlaying()
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
        public void Shuffle()
        {
            _trackQueueViewModel.ToggleShuffle();
            UpdateShuffleIcon();
        }
        private void UpdateShuffleIcon()
        {
            if (Application.Current != null)
            {
                ShuffleIcon = _trackQueueViewModel.IsShuffled ?
                    Application.Current.FindResource("ShuffleOnIcon") :
                    Application.Current.FindResource("ShuffleOffIcon");
            }
        }

        [RelayCommand]
        public void ToggleRepeat()
        {
            _trackQueueViewModel.ToggleRepeatMode();
            UpdateRepeatIcon();
        }

        private void UpdateRepeatIcon()
        {
            if (Application.Current == null) return;

            RepeatIcon = _trackQueueViewModel.RepeatMode switch
            {
                RepeatMode.None => Application.Current.FindResource("RepeatNoneIcon"),
                RepeatMode.All => Application.Current.FindResource("RepeatAllIcon"),
                RepeatMode.One => Application.Current.FindResource("RepeatOneIcon"),
                _ => Application.Current.FindResource("RepeatOffIcon")
            };
        }

        [RelayCommand]
        public void PlayNextTrack()
        {
            var nextTrackIndex = _trackQueueViewModel.GetNextTrack();
            if (nextTrackIndex >= 0)
            {
                var nextTrack = _trackQueueViewModel.NowPlayingQueue[nextTrackIndex];
                _trackQueueViewModel.UpdateCurrentTrackIndex(nextTrackIndex);
                StopPlayback();
                ReadyTrack(nextTrack);
                PlayTrack();
                UpdateTrackInfo();
            }
            else if (_audioFileReader != null)
            {
                // No next track and no repeat - stop playback
                StopPlayback();
            }
        }
        [RelayCommand]
        public void PlayPreviousTrack()
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
                    UpdateTrackInfo();
                }
            }
        }

        private async void UpdateTrackInfo()
        {
            var track = GetCurrentTrack();

            if (track == null) { return; }

            CurrentlyPlayingTrack = track;

            if (_audioFileReader == null)
            {
                ReadyTrack(track);
            }

            if (_navigationService.IsCurrentlyShowingNowPlaying())
            {
                await ShowNowPlaying();
            }

            //if (_audioFileReader.FileName == track.FilePath) { return; }

            CurrentTitle = track.Title;
            CurrentArtists = track.Artists;
            CurrentAlbumTitle = track.AlbumTitle;

            track.Artists.Last().IsLastArtist = false;// Hides the Comma of the last Track
            await _trackDService.LoadHighResThumbnailAsync(track);// Load track Thumbnail

            CurrentTrackImage = track.Thumbnail;
            TrackDuration = _audioFileReader.TotalTime;
            TrackPosition = TimeSpan.Zero;
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
